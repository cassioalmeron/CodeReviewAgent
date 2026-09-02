using System.Text;
using System.Text.Json;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// LLM client that drives the local Claude CLI directly (no SDK), using this machine's
/// Claude Code subscription instead of the metered API. Parses the CLI's stream-json
/// output and ignores message types it doesn't need (e.g. <c>rate_limit_event</c>),
/// which keeps it resilient to new CLI message types — the failure mode that breaks the
/// SDK-based <see cref="ClaudeCodeClient"/>. Requires the Claude CLI installed and logged in.
/// The diff goes through stdin so a large payload can't overflow the command-line limit.
/// </summary>
internal class ClaudeCliClient : ILlmClient
{
    private readonly string _executable = ResolveExecutable();
    private readonly string? _model;

    public ClaudeCliClient()
    {
        // Optional: blank falls back to the CLI's default model.
        _model = Environment.GetEnvironmentVariable("CLAUDE_CODE_MODEL");
    }

    public MessageResponse Request(object requestBody)
    {
        var (systemPrompt, diff) = ClaudeCodeShared.BuildRequest(requestBody);

        var args = new List<string>
        {
            "--output-format", "stream-json", // JSONL event stream on stdout.
            "--verbose",                      // required by stream-json.
            "--max-turns", "1",               // single answer; don't loop with tools.
            "--tools", "",                    // disable ALL built-in tools: otherwise the
                                              // model runs agentically (Read / ReportFindings /
                                              // StructuredOutput), taking extra turns, minutes
                                              // of latency, and 10x the tokens — and answering
                                              // via a tool call instead of the text we parse.
            "--setting-sources", "",          // load no settings/CLAUDE.md/skills, so the
                                              // reviewed repo's config can't hijack the format.
            "--system-prompt", systemPrompt,  // replace the default agent prompt.
            "--print",                        // non-interactive; read the prompt from stdin.
        };
        if (!string.IsNullOrWhiteSpace(_model))
        {
            args.Add("--model");
            args.Add(_model);
        }

        // Run from a neutral directory (not the repo): otherwise the CLI loads the
        // reviewed repository's CLAUDE.md / skills, which hijack the output format.
        var output = ProcessRunner.RunWithInput(
            _executable, diff, Path.GetTempPath(), EnvironmentToScrub(), [.. args]);
        return Parse(output);
    }

    // Environment variables the review subprocess must NOT inherit:
    //  - ANTHROPIC_API_KEY / ANTHROPIC_AUTH_TOKEN: an API key takes precedence over the
    //    claude.ai login, so the CLI would bill the metered API instead of the subscription
    //    (the whole point of this engine). The parent keeps them for the API engine / judge.
    //  - CLAUDECODE / CLAUDE_CODE_*: set when running inside a Claude Code session; they make
    //    the nested CLI load the parent session's skills/output style and mangle the format.
    private static IReadOnlyCollection<string> EnvironmentToScrub()
    {
        var names = new List<string> { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" };
        foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>())
            if (key.StartsWith("CLAUDE_CODE", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("CLAUDECODE", StringComparison.OrdinalIgnoreCase))
                names.Add(key);
        return names;
    }

    // On Windows the PATH entry is claude.cmd, a batch shim whose %* forwarding mangles
    // the multi-line --system-prompt (and drops the empty --setting-sources value). Run
    // the real claude.exe next to it so arguments are passed verbatim. CLAUDE_CLI_PATH
    // overrides the lookup for non-standard installs.
    private static string ResolveExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        if (!OperatingSystem.IsWindows())
            return "claude";

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var nested = Path.Combine(dir, "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");
            if (File.Exists(nested))
                return nested;
            var direct = Path.Combine(dir, "claude.exe");
            if (File.Exists(direct))
                return direct;
        }
        return "claude.cmd"; // fall back to the shim (multi-line prompts may be mangled).
    }

    // Each stdout line is one JSON event. We only care about the assistant text and the
    // final result (usage + cost); every other type is skipped.
    private static MessageResponse Parse(string streamJson)
    {
        var text = new StringBuilder();
        string? model = null;
        int inputTokens = 0, outputTokens = 0;
        decimal? cost = null;

        foreach (var line in streamJson.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            JsonElement root;
            try { root = JsonDocument.Parse(trimmed).RootElement; }
            catch (JsonException) { continue; }

            if (!root.TryGetProperty("type", out var typeProp))
                continue;

            switch (typeProp.GetString())
            {
                case "assistant":
                    var message = root.GetProperty("message");
                    if (message.TryGetProperty("model", out var m))
                        model ??= m.GetString();
                    foreach (var block in message.GetProperty("content").EnumerateArray())
                        if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text")
                            text.Append(block.GetProperty("text").GetString());
                    break;

                case "result":
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        // The CLI reports `input_tokens` as the uncached portion only; the
                        // bulk is under the cache fields. Sum them for an honest input count.
                        inputTokens = GetInt(usage, "input_tokens")
                                    + GetInt(usage, "cache_read_input_tokens")
                                    + GetInt(usage, "cache_creation_input_tokens");
                        outputTokens = GetInt(usage, "output_tokens");
                    }
                    if (root.TryGetProperty("total_cost_usd", out var c) &&
                        c.ValueKind == JsonValueKind.Number)
                        cost = c.GetDecimal();
                    break;
            }
        }

        return new MessageResponse
        {
            Model = model ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_MODEL"),
            Content = [new ContentBlock { Type = "text", Text = ClaudeCodeShared.StripFences(text.ToString()) }],
            Usage = new Usage { InputTokens = inputTokens, OutputTokens = outputTokens },
            Cost = cost,
        };
    }

    private static int GetInt(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0;
}

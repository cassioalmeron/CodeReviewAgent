using System.Text;
using System.Text.Json;
using ClaudeAgentSdk;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Infra;

/// <summary>
/// LLM client backed by the locally installed Claude CLI (via the ClaudeAgentSdk).
/// Uses this machine's Claude Code subscription instead of the metered API, so there
/// is no per-token API charge. The CLI has no native JSON-schema enforcement, so the
/// schema is injected into the system prompt and the response is parsed as JSON text.
/// Requires the Claude CLI installed and logged in.
/// </summary>
internal class ClaudeCodeClient : ILlmClient
{
    private readonly string? _model;

    public ClaudeCodeClient()
    {
        // Optional: blank falls back to the CLI's default model.
        _model = Environment.GetEnvironmentVariable("CLAUDE_CODE_MODEL");
    }

    public MessageResponse Request(object requestBody)
    {
        var (systemPrompt, diff) = ClaudeCodeShared.BuildRequest(requestBody);

        var stderr = new StringBuilder();
        var options = new ClaudeAgentOptions
        {
            SystemPrompt = systemPrompt,
            Model = string.IsNullOrWhiteSpace(_model) ? null : _model,
            MaxTurns = 1,
            AllowedTools = [], // Review only the given diff; don't let the CLI read the repo.
            Stderr = line => stderr.AppendLine(line),
        };

        return CollectAsync(diff, options, stderr).GetAwaiter().GetResult();
    }

    private static async Task<MessageResponse> CollectAsync(
        string prompt, ClaudeAgentOptions options, StringBuilder stderr)
    {
        var text = new StringBuilder();
        string? model = null;
        int inputTokens = 0, outputTokens = 0;
        decimal? cost = null;

        try
        {
            await foreach (var message in ClaudeAgent.QueryAsync(prompt, options))
            {
                switch (message)
                {
                    case AssistantMessage assistant:
                        model ??= assistant.Model;
                        foreach (var block in assistant.Content)
                            if (block is TextBlock t)
                                text.Append(t.Text);
                        break;

                    case ResultMessage result:
                        inputTokens = GetInt(result.Usage, "input_tokens");
                        outputTokens = GetInt(result.Usage, "output_tokens");
                        if (result.TotalCostUsd is double c)
                            cost = (decimal)c;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            var detail = stderr.Length > 0 ? stderr.ToString().Trim() : "(no stderr captured)";
            throw new InvalidOperationException(
                $"Claude CLI failed: {ex.Message}\n--- CLI stderr ---\n{detail}", ex);
        }

        return new MessageResponse
        {
            Model = model ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_MODEL"),
            Content = [new ContentBlock { Type = "text", Text = ClaudeCodeShared.StripFences(text.ToString()) }],
            Usage = new Usage { InputTokens = inputTokens, OutputTokens = outputTokens },
            Cost = cost,
        };
    }

    private static int GetInt(Dictionary<string, object>? usage, string key)
    {
        if (usage is null || !usage.TryGetValue(key, out var value))
            return 0;
        return value switch
        {
            JsonElement e when e.TryGetInt32(out var n) => n,
            long l => (int)l,
            int i => i,
            _ => 0,
        };
    }
}

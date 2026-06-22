# Code Review Agent

An agent for automated code reviewing, powered by an LLM.

> Developed as an activity for the **Rambo EPC method**, specifically **Pilar 3**.

It reads a diff (the local working tree or a real pull request), sends it to an
LLM with a versioned system prompt and a JSON schema, and gets back a structured
list of findings — each with file, line, severity, category, problem, and
suggestion. Every run is logged (model, prompt version, tokens, cost, latency).

## Tech stack

- .NET 10 solution split into three projects (Core / Console / Tests)
- **Core** as a reusable class library — the whole review flow lives here, so it can
  be driven by more than one front end (console, web, desktop, MCP)
- Raw `HttpClient` calls to the LLM APIs (no SDK)
- Pluggable **LLM engines** behind `ILlmClient` (Ollama / Claude)
- Pluggable **diff sources** behind `IDiffSource` (local HEAD / pull request)
- Native **structured output** (JSON schema) → findings as C# records
- xUnit tests over the flow with fake `ILlmClient` / `IDiffSource` implementations

## Project layout

```
src/CodeReviewerAgent/
├── CodeReviewerAgent.slnx               # Solution: Core + Console + Tests
│
├── CodeReviewerAgent.Core/              # Class library — the reusable review flow
│   ├── CodeReviewer.cs                  # Orchestrates: get diff → call LLM → parse → display/log/save
│   ├── ILlmServer.cs                    # ILlmClient + AnthropicClient + OllamaClient
│   ├── LlmClientFactory.cs              # Picks the LLM client based on LLM_ENGINE
│   ├── MessageResponse.cs               # Neutral LLM response shape (model, content, usage)
│   ├── IDiffSource.cs                   # Diff source strategy
│   ├── DiffSourceFactory.cs             # Picks the diff source based on the CLI args
│   ├── LocalDiffSource.cs               # Local repo: staged if any, else git diff HEAD
│   ├── StagedDiffSource.cs              # git diff --staged
│   ├── FilesDiffSource.cs               # git diff HEAD -- <paths>
│   ├── PullRequestDiffSource.cs         # gh pr diff <number>
│   ├── ProcessRunner.cs                 # Runs external commands (git / gh)
│   ├── Finding.cs                       # Finding + ReviewResult records, Severity/Category enums
│   ├── CostCalculator.cs                # Per-model cost estimate (Ollama = free)
│   ├── Logger.cs                        # Appends JSON log lines (JSONL)
│   ├── EnvLoader.cs                     # Minimal .env loader
│   └── prompts/
│       └── review-v1.md                 # Versioned review system prompt
│
├── CodeReviewerAgent.Console/           # Executable — thin entry point over Core
│   ├── Program.cs                       # Load .env, pick engine + diff source, run review
│   └── .env.example                     # Sample configuration
│
└── CodeReviewerAgent.Tests/             # xUnit tests over the review flow
    ├── CodeReviewerTests.cs             # Empty diff / valid findings / invalid JSON
    └── Fakes/                           # FakeLlmClient + FakeDiffSource
```

## Prerequisites

- .NET 10 SDK
- For `LLM_ENGINE=ollama`: a running [Ollama](https://ollama.com) with the model pulled (`ollama pull qwen2.5-coder:7b`)
- For `LLM_ENGINE=claude`: an Anthropic API key
- For reviewing pull requests: the [GitHub CLI](https://cli.github.com) installed and authenticated (`gh auth login`)

## Configuration

Configuration is read from a `.env` file (gitignored). Copy `.env.example` and
fill in your values:

```
# Which engine to use: ollama | claude
LLM_ENGINE=ollama

# Which versioned prompt to use (prompts/review-<version>.md)
PROMPT_VERSION=v1

# Claude (used when LLM_ENGINE=claude)
ANTHROPIC_API_KEY=your-key-here
ANTHROPIC_MODEL=claude-opus-4-8

# Ollama (used when LLM_ENGINE=ollama)
OLLAMA_HOST=http://localhost:11434
OLLAMA_MODEL=qwen2.5-coder:7b
```

> The API key is never committed — keep it only in your local `.env`.

## Running

```bash
cd src/CodeReviewerAgent/CodeReviewerAgent.Console

dotnet run                       # review the local repo (staged if any, else git diff HEAD)
dotnet run -- staged             # review only the staged changes (git diff --staged)
dotnet run -- files A.cs B.cs    # review specific files (git diff HEAD -- A.cs B.cs)
dotnet run -- pr 42              # review pull request #42 (gh pr diff 42)
```

Example output:

```
The diff adds a cost calculator but hardcodes pricing and lacks an unknown-model guard.

Findings: 2
  [Warning] CostCalculator.cs:12 (Maintainability) — Pricing is hardcoded -> Move prices to configuration
  [Info] CostCalculator.cs:28 (Bug) — Unknown model silently returns 0 -> Log or surface unmapped models
Review saved to .../reviews/review-2026-06-20-101500.json
```

## Tests

```bash
cd src/CodeReviewerAgent
dotnet test            # runs CodeReviewerAgent.Tests
```

The tests exercise `CodeReviewer.Review()` end to end with fake `ILlmClient` and
`IDiffSource` implementations (no network, no git) — covering the empty-diff,
valid-findings, and invalid-JSON paths — plus `DiffSourceFactory`, asserting each
CLI command routes to the right diff source.

## How it works

The review pipeline (`CodeReviewer.Review()`):

1. **Get the diff** from the `IDiffSource` selected by `DiffSourceFactory` from the
   CLI args (`LocalDiffSource` → staged or `git diff HEAD`, `StagedDiffSource` →
   `git diff --staged`, `FilesDiffSource` → `git diff HEAD -- <paths>`, or
   `PullRequestDiffSource` → `gh pr diff <n>`).
2. **Build the request** with the versioned system prompt (`prompts/review-<version>.md`)
   and a JSON schema describing the expected output.
3. **Call the LLM** through the selected `ILlmClient`. Each engine translates the
   neutral request into its own API shape (Claude's `output_config.format`,
   Ollama's `format`) and maps the response back to a shared `MessageResponse`.
4. **Parse** the structured response into a `ReviewResult` (a summary plus a list
   of `Finding` records).
5. **Display, log, and save** — print the summary and findings, append a JSON log
   line (engine, model, prompt version, tokens, cost, latency, findings count),
   and write the structured review to `reviews/`.

### Findings schema

```csharp
record Finding(string File, int Line, Severity Severity, Category Category,
               string Problem, string Suggestion);

enum Severity { Info, Warning, Critical }
enum Category { Bug, Security, Performance, Style, Maintainability }
```

### Structured log (JSONL)

Each run appends one JSON object to `logs/llm-<date>.jsonl`:

```json
{"timestamp":"2026-06-20T10:15:00Z","engine":"claude","model":"claude-haiku-4-5","prompt_version":"v1","input_tokens":820,"output_tokens":260,"total_tokens":1080,"cost_usd":0.0021,"latency_ms":3100,"findings_count":2}
```

Logging the prompt + model version per run makes it possible to compare results
across iterations ("did prompt v2 find more than v1?").

## Extending

- **New LLM engine** — implement `ILlmClient`, add a case in `LlmClientFactory`, document its env vars.
- **New diff source** — implement `IDiffSource` (e.g. read a `.diff` file) and add a case in `DiffSourceFactory`.
- **New prompt version** — add `prompts/review-v2.md` and set `PROMPT_VERSION=v2`.

## Roadmap

- [ ] Read the diff from a file / stdin (`FileDiffSource`)
- [ ] Validate finding line numbers against the parsed diff
- [ ] Post findings back as PR review comments

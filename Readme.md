# Code Review Agent

An agent for automated code reviewing, powered by an LLM.

> Developed as an activity for the **Rambo EPC method**, specifically **Pilar 3**.

It reads a diff (the local working tree or a real pull request), sends it to an
LLM with a versioned system prompt and a JSON schema, and gets back a structured
list of findings — each with file, line, severity, category, problem, and
suggestion. Every run is logged (model, prompt version, tokens, cost, latency).

It also ships an **evaluation harness** to measure review quality with method, not
by eye: a deterministic **golden set** (does the agent catch known problems?) and
an **LLM-as-judge** (are the comments good?).

## Tech stack

- .NET 10 solution split into three projects (Core / Console / Tests)
- **Core** is a reusable class library — the whole review and evaluation flow lives
  here, so it can be driven by more than one front end (console, web, desktop, MCP)
- Raw `HttpClient` calls to the LLM APIs (no SDK)
- Pluggable **LLM engines** behind `ILlmClient` (Ollama / Claude)
- Pluggable **diff sources** behind `IDiffSource` (local / staged / files / pull request)
- Native **structured output** (JSON schema) → findings as C# records
- xUnit tests over the flow with fake `ILlmClient` / `IDiffSource` implementations

## Project layout

```
src/CodeReviewerAgent/
├── CodeReviewerAgent.Core/              # Class library — review + evaluation flow
│   ├── CodeReviewer.cs                  # Review pipeline: diff → LLM → parse → ground → log/save
│   ├── ILlmServer.cs                    # ILlmClient + AnthropicClient + OllamaClient
│   ├── LlmClientFactory.cs              # Picks the LLM client based on LLM_ENGINE
│   ├── MessageResponse.cs               # Neutral LLM response shape (model, content, usage)
│   ├── IDiffSource.cs / *DiffSource.cs  # Diff source strategy (local/staged/files/pr) + factory
│   ├── ProcessRunner.cs                 # Runs external commands (git / gh)
│   ├── DiffParser.cs                    # Parses a unified diff into files/hunks/lines
│   ├── FindingValidator.cs             # Grounds findings to added lines; derives the line number
│   ├── Finding.cs                       # Finding + ReviewResult records, Severity/Category enums
│   ├── CostCalculator.cs                # Per-model cost estimate (Ollama = free)
│   ├── Logger.cs                        # Appends JSON log lines (JSONL)
│   ├── ReportGenerator.cs              # Markdown review report
│   ├── GoldenEvaluator.cs              # Golden set: detection measurement (computational layer)
│   ├── Judge.cs                         # LLM-as-judge: quality scoring (inferential layer)
│   ├── JudgeRunner.cs / JudgeReportGenerator.cs  # Drives + reports the judge
│   ├── EnvLoader.cs                     # Minimal .env loader
│   ├── prompts/review-v1..v5.md         # Versioned review system prompts (v3 is the default)
│   ├── rubrics/judge-v1.md              # Versioned judge rubric
│   └── golden/                          # cases.json + the known-problem diffs
│
├── CodeReviewerAgent.Console/           # Executable — thin entry point over Core
│   ├── Program.cs                       # Routes CLI args to review / eval / judge / all
│   └── .env.example                     # Sample configuration
│
└── CodeReviewerAgent.Tests/             # xUnit tests with fake ILlmClient / IDiffSource
```

## Prerequisites

- .NET 10 SDK
- For `LLM_ENGINE=ollama`: a running [Ollama](https://ollama.com) with the model pulled (`ollama pull qwen2.5-coder:7b`)
- For `LLM_ENGINE=claude` (and the judge): an Anthropic API key
- For reviewing pull requests: the [GitHub CLI](https://cli.github.com) installed and authenticated (`gh auth login`)

## Configuration

Configuration is read from a `.env` file (gitignored). Copy `.env.example` and
fill in your values:

```
# Which engine runs the review: ollama | claude
LLM_ENGINE=claude

# Which versioned prompt to use (prompts/review-<version>.md)
PROMPT_VERSION=v3

# Claude (used when LLM_ENGINE=claude, and always for the judge)
ANTHROPIC_API_KEY=your-key-here
ANTHROPIC_MODEL=claude-haiku-4-5

# Ollama (used when LLM_ENGINE=ollama)
OLLAMA_HOST=http://localhost:11434
OLLAMA_MODEL=qwen2.5-coder:7b

# Evaluation
GOLDEN_RUNS=3                  # runs per golden case (averages out non-determinism)
JUDGE_MODEL=claude-sonnet-4-6  # stronger than the executor, to avoid self-preference bias
RUBRIC_VERSION=v1              # rubrics/judge-<version>.md
```

> The API key is never committed — keep it only in your local `.env`.

## Running

```bash
cd src/CodeReviewerAgent/CodeReviewerAgent.Console

# Review a diff
dotnet run                       # local repo (staged if any, else git diff HEAD)
dotnet run -- staged             # git diff --staged
dotnet run -- files A.cs B.cs    # git diff HEAD -- A.cs B.cs
dotnet run -- pr 42              # pull request #42 (gh pr diff 42)

# Evaluate the agent
dotnet run -- eval               # golden set (detection) → writes reviews/eval-results.json
dotnet run -- judge             # judge scores the persisted reviews
dotnet run -- all                # eval + judge in a single run
```

## How a review works

`CodeReviewer` pipeline:

1. **Get the diff** from the `IDiffSource` selected by `DiffSourceFactory` from the CLI args.
2. **Build the request** with the versioned system prompt (`prompts/review-<version>.md`)
   and a JSON schema describing the expected output.
3. **Call the LLM** through the selected `ILlmClient`; each engine translates the
   neutral request into its own API shape (Claude's `output_config.format`, Ollama's
   `format`) and maps the response back to a shared `MessageResponse`.
4. **Parse and ground** — deserialize into a `ReviewResult` (summary + findings), then
   `FindingValidator` keeps only findings whose cited `code_snippet` matches an added
   line of the diff, deriving the real line number from it.
5. **Display, log, and save** — print, append a JSON log line, and write the review.

### Findings schema

```csharp
record Finding(string? File, string? CodeSnippet, Severity? Severity, Category? Category,
               string? Problem, string? Suggestion, int? Line);

enum Severity { Info, Warning, Critical }
enum Category { Bug, Security, Performance, Style, Maintainability }
```

The model cites the affected line verbatim in `CodeSnippet`; `Line` is not trusted
from the model but derived by matching the snippet against the parsed diff.

## Evaluation

Two layers measure different things on the same diffs:

### Golden set — *does the agent catch the problem?* (computational)

`golden/` holds a handful of diffs, each with a known, planted problem and its
expectations (`cases.json`). `GoldenEvaluator` runs the agent over each diff
`GOLDEN_RUNS` times (LLM output is non-deterministic, so one run is a noisy sample)
and checks, in code, whether some finding points at the right file and mentions an
expected keyword. The result is a **detection rate** per case (e.g. `2/3`).

### LLM-as-judge — *are the comments good?* (inferential)

`Judge` sends the diff + the agent's review to a **stronger** model (`JUDGE_MODEL`)
with a versioned rubric (`rubrics/judge-<version>.md`) and gets back structured
scores (1–5) for **correctness, actionability, calibration, signal-to-noise**, plus
an overall score and rationale. The judge is intentionally a different, stronger
model than the executor to avoid **self-preference bias**.

`eval` persists the reviews to `reviews/eval-results.json` so `judge` can score them
without re-invoking the executor; `all` runs both in one go.

### Iterating prompts

Every run logs `prompt_version` and `model`, so prompt/model changes can be compared
with numbers, not by eye — the golden set guards detection, the judge guards quality.

## Tests

```bash
cd src/CodeReviewerAgent
dotnet test
```

The tests exercise the flow end to end with fake `ILlmClient` and `IDiffSource`
implementations (no network, no git), plus `DiffSourceFactory` routing.

## Extending

- **New LLM engine** — implement `ILlmClient`, add a case in `LlmClientFactory`, document its env vars.
- **New diff source** — implement `IDiffSource` and add a case in `DiffSourceFactory`.
- **New prompt / rubric version** — add `prompts/review-v6.md` (or `rubrics/judge-v2.md`) and point the env var at it.
- **New golden case** — add a `.diff` and an entry in `golden/cases.json`.

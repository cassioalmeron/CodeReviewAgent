# Code Review Agent

An agent for automated code reviewing, powered by an LLM.

> Developed as an activity for the **Rambo EPC method**, specifically **Pilar 3**.

It reads a diff (the local working tree or a real pull request), sends it to an
LLM with a versioned system prompt and a JSON schema, and gets back a structured
list of findings — each with file, line, severity, category, problem, and
suggestion. Diffs and analyses are persisted as first-class, id-addressable
entities (each analysis carries its run metadata: model, prompt version, tokens,
cost, latency), so a report can be regenerated from a stored assessment without
re-running the LLM.

It also ships an **evaluation harness** to measure review quality with method, not
by eye: a deterministic **golden set** (does the agent catch known problems?) and
an **LLM-as-judge** (are the comments good?).

## Tech stack

- .NET 10 backend split into five projects (Core / Infra / Console / Api / Tests)
- **Core** is a reusable class library — the whole review and evaluation flow lives
  here, so it can be driven by more than one front end (console, web, desktop, MCP)
- **Infra** holds the implementations behind Core's contracts — persistence (the only
  project depending on EF Core) and the LLM engines + HTTP transport + `LlmClientFactory`
- HTTP engines call the LLM APIs with raw `HttpClient` (no SDK), behind a resilient transport (retry + exponential backoff on transient failures)
- Pluggable **LLM engines** (in Infra) behind Core's `ILlmClient` — Ollama / Claude / OpenAI / OpenRouter over HTTP, plus Claude via your local CLI/subscription; the factory is instantiated by Console/Api, never by Core
- Pluggable **diff sources** behind `IDiffSource` (local / staged / files / pull request)
- Native **structured output** (JSON schema) → findings as C# records
- xUnit tests over the flow with fake `ILlmClient` / `IDiffSource` implementations
- **Api** — a read-only ASP.NET Core minimal API (Swagger/OpenAPI) that exposes the store to a web viewer
- **web/** — a React 19 + TypeScript + Vite viewer (React Router, styled-components) to browse projects/reviews/assessments/evaluations

## Project layout

```
src/CodeReviewerAgent/
├── CodeReviewerAgent.Core/              # Class library — review + evaluation flow
│   ├── CodeReviewer.cs                  # Review pipeline: diff → LLM → parse → ground (pure step, no I/O)
│   ├── ILlmClient.cs                    # LLM client contract (implementations live in Infra)
│   ├── MessageResponse.cs               # Neutral LLM response shape (model, content, usage, optional real cost)
│   ├── IDiffSource.cs / *DiffSource.cs  # Diff source strategy (local/staged/files/pr) + factory
│   ├── ProcessRunner.cs                 # Runs external commands (git / gh)
│   ├── DiffParser.cs                    # Parses a unified diff into files/hunks/lines
│   ├── FindingValidator.cs             # Grounds findings to added lines; derives the line number
│   ├── Finding.cs                       # Finding + ReviewResult records, Severity/Category enums
│   ├── Entities.cs                      # Project / Review (+ ContentHash) / Assessment / Evaluation (1→N→N→N)
│   ├── ProjectResolver.cs               # Resolves the current Project from REPO_DIR / cwd (folder = key)
│   ├── IRepository.cs                   # IProject / IReview / IAssessment / IEvaluation repository contracts
│   ├── CostCalculator.cs                # Per-model cost (Claude + OpenAI; Ollama/subscription/OpenRouter = no estimate)
│   ├── ReportGenerator.cs              # Markdown review report
│   ├── GoldenEvaluator.cs              # Golden set: detection measurement; persists diffs + analyses
│   ├── Judge.cs                         # LLM-as-judge: quality scoring (inferential layer)
│   ├── JudgeRunner.cs / JudgeReportGenerator.cs  # Drives + reports the judge
│   ├── EnvLoader.cs                     # Minimal .env loader
│   ├── prompts/review-v1..v5.md         # Versioned review system prompts (v3 is the default)
│   ├── rubrics/judge-v1.md              # Versioned judge rubric
│   └── golden/                          # cases.json + the known-problem diffs
│
├── CodeReviewerAgent.Infra/              # Implementations behind Core's contracts (persistence + LLM engines)
│   ├── FileRepository.cs                # File-backed repos (projects/ + reviews/ + assessments/ + evaluations/, id = max+1)
│   ├── Hashing.cs                        # SHA-256 of diff content (content-addressed reuse)
│   ├── CodeReviewDbContext.cs            # Single EF context; Fluent mapping; findings as a child table
│   ├── DbProviderStrategy.cs             # Sqlite / Postgres provider strategies (no if-chain)
│   ├── EfRepository.cs                   # EF-backed repos (one impl, both providers)
│   ├── RepositoryFactory.cs             # Picks File/EF from STORAGE; EnsureCreated for EF
│   ├── LlmClientFactory.cs              # Picks the ILlmClient from LLM_ENGINE (called by Console/Api)
│   ├── HttpTransport.cs                 # IHttpTransport + resilient decorator (retry / backoff)
│   ├── AnthropicClient.cs / OllamaClient.cs / OpenAiClient.cs / OpenRouterClient.cs  # HTTP engines
│   └── ClaudeCliClient.cs / ClaudeCodeClient.cs / ClaudeCodeShared.cs  # Subscription engines (Claude CLI / SDK)
│
├── CodeReviewerAgent.Console/           # Executable — thin entry point over Core
│   ├── Program.cs                       # Routes CLI args to diff / review / report / judge / judge-report / eval / all
│   └── .env.example                     # Sample configuration
│
├── CodeReviewerAgent.Api/               # Read-only minimal API over the store (Swagger UI at /swagger)
│   ├── Program.cs                       # /api/projects, /api/reviews, /api/assessments, /api/evaluations (+ /{id}, nested lists, ?projectId=)
│   └── .env.example                     # Only STORAGE / DB_CONNECTION — no LLM keys, no writes
│
├── CodeReviewerAgent.Tests/             # xUnit tests with fake ILlmClient / IDiffSource
│
└── web/                                 # React + TypeScript + Vite viewer for the Api
    ├── src/App.tsx                      # Routes: diffs / analyses / evaluations (list + detail)
    ├── src/services/api.ts              # fetch wrapper over /api/* (proxied to the Api in dev)
    ├── src/pages/                       # One folder per route
    └── src/components/{layout,features,ui}/
```

## Prerequisites

- .NET 10 SDK
- For `LLM_ENGINE=ollama`: a running [Ollama](https://ollama.com) with the model pulled (`ollama pull qwen2.5-coder:7b`)
- For `LLM_ENGINE=claude` (and the judge): an Anthropic API key
- For `LLM_ENGINE=openai`: an OpenAI API key
- For `LLM_ENGINE=openrouter`: an [OpenRouter](https://openrouter.ai) API key
- For `LLM_ENGINE=claude-code` / `claude-cli`: a logged-in [Claude CLI](https://claude.com/claude-code) (uses your subscription, no API key)
- For reviewing pull requests: the [GitHub CLI](https://cli.github.com) installed and authenticated (`gh auth login`)
- For the web viewer: Node.js (for `web/`)

## Configuration

Each runnable project reads its own `.env` file (gitignored). Copy the project's
`.env.example` and fill in your values. This is `CodeReviewerAgent.Console`'s:

```
# Which engine runs the review: ollama | claude | openai | openrouter | claude-code | claude-cli
LLM_ENGINE=claude

# Which versioned prompt to use (prompts/review-<version>.md)
PROMPT_VERSION=v3

# Where reviews + assessments are stored: files | sqlite | postgres
# sqlite always uses %LOCALAPPDATA%/CodeReviewerAgent/review.db; DB_CONNECTION is the Postgres
# connection string only (files and sqlite ignore it).
STORAGE=files
DB_CONNECTION=

# Claude (used when LLM_ENGINE=claude, and always for the judge)
ANTHROPIC_API_KEY=your-key-here
ANTHROPIC_MODEL=claude-haiku-4-5

# Ollama (used when LLM_ENGINE=ollama)
OLLAMA_HOST=http://localhost:11434
OLLAMA_MODEL=qwen2.5-coder:7b

# OpenAI (used when LLM_ENGINE=openai)
OPENAI_API_KEY=your-key-here
OPENAI_MODEL=gpt-4o-mini

# OpenRouter (used when LLM_ENGINE=openrouter)
OPENROUTER_API_KEY=your-key-here
OPENROUTER_MODEL=openai/gpt-4o-mini

# Evaluation
GOLDEN_RUNS=3                  # runs per golden case (averages out non-determinism)
JUDGE_MODEL=claude-sonnet-4-6  # stronger than the executor, to avoid self-preference bias
RUBRIC_VERSION=v1              # rubrics/judge-<version>.md
```

> The API key is never committed — keep it only in your local `.env`. `CodeReviewerAgent.Api` has
> its own `.env` (see its `.env.example`) but only needs `STORAGE` / `DB_CONNECTION` — it's
> read-only and never calls an LLM.

## Running

```bash
cd src/CodeReviewerAgent/CodeReviewerAgent.Console

# Review a diff (capture + review + report in one shot, persisting both)
dotnet run                       # local repo (staged if any, else git diff HEAD)
dotnet run -- staged             # git diff --staged
dotnet run -- files A.cs B.cs    # git diff HEAD -- A.cs B.cs
dotnet run -- pr 42              # pull request #42 (gh pr diff 42)

# Or as independent, id-addressable steps against the store
dotnet run -- review             # capture the local diff → "Review saved with id N"  (no LLM)
dotnet run -- review staged      # same, from any diff source (staged / files ... / pr N)
dotnet run -- assess N           # analyze stored review N via the LLM → "Assessment saved with id M"
dotnet run -- report M           # regenerate the review report from assessment M  (no LLM)
dotnet run -- judge M            # judge stored assessment M → saves an Evaluation
dotnet run -- judge-report E     # report for evaluation E (diff + scores)            (no LLM)
dotnet run -- judge-report assessmentId M   # report for every evaluation of assessment M (no LLM)
dotnet run -- judge-report golden           # consolidated report over the golden set   (no LLM)
dotnet run -- projects           # list the stored projects
dotnet run -- project rename N <name>   # rename a project (the Api is read-only)

# Evaluate the agent
dotnet run -- eval               # golden set (detection); persists diffs + analyses to the store
dotnet run -- judge              # golden judge over reviews/eval-results.json (aggregate report)
dotnet run -- all                # eval + judge in a single run
```

### Web viewer

A read-only browser for whatever is in the store (files/sqlite/postgres, per `STORAGE`).
Two processes, run separately:

```bash
cd src/CodeReviewerAgent/CodeReviewerAgent.Api
dotnet run                       # http://localhost:5180 (Development) — Swagger UI at /swagger

cd src/CodeReviewerAgent/web
npm install                      # first run only
npm run dev                      # http://localhost:5173 — Vite proxies /api/* to :5180
```

Only `review`, `judge M`, and the combined review commands call the LLM; `diff`, `report`, and
every `judge-report` do not. A diff can have many analyses (re-run `review N` with a different
prompt/model); an analysis can have many judge evaluations.

## How a review works

`CodeReviewer` pipeline:

1. **Get the diff** from the `IDiffSource` selected by `DiffSourceFactory` from the CLI args.
2. **Build the request** with the versioned system prompt (`prompts/review-<version>.md`)
   and a JSON schema describing the expected output.
3. **Call the LLM** through the selected `ILlmClient` (implemented in Infra); each engine
   translates the neutral request into its own API shape (Claude's `output_config.format`,
   Ollama's `format`, OpenAI's / OpenRouter's `response_format.json_schema`) and maps the
   response back to a shared `MessageResponse`. HTTP engines send through a resilient transport
   that retries transient failures (429 / 5xx / network / timeout) with backoff.
4. **Parse and ground** — deserialize into a `ReviewResult` (summary + findings), then
   `FindingValidator` keeps only findings whose cited `code_snippet` matches an added
   line of the diff, deriving the real line number from it.

Cost is priced from a per-model table (`CostCalculator`) for the metered Claude / OpenAI engines.
OpenRouter instead reports its real per-call cost (via `usage: { include: true }`, read back onto
`MessageResponse.Cost`), so the pipeline uses that directly — `response.Cost ?? CostCalculator.Estimate(...)`.

`Review()` is a **pure step**: it returns the `ReviewResult` and does no I/O. Persistence is a
separate step — the Console layer resolves the `Project` (`ProjectResolver`) and stores the review
(diff) and the assessment through the repositories (`IReviewRepository` / `IAssessmentRepository`)
selected by `STORAGE`. The run metadata (model, prompt version, tokens, cost, latency) lives on the
persisted `Assessment`; there is no separate run log.

### Storage

Four id-addressable entities behind repositories (`CodeReviewerAgent.Infra/`) — `Project (1) → Review (N) →
Assessment (N) → Evaluation (N)`, with `Finding` hanging off `Assessment` — so the review, assess,
report, and judge steps are all independent. Only `Review` points at `Project`; the rest cascade:

- `STORAGE=files` — JSON files under `projects/`, `reviews/`, `assessments/`, `evaluations/`; the
  next id is the highest existing id + 1, read from the file names (`review-<id>-<timestamp>.json`).
  Findings stay nested inside each assessment's JSON.
- `STORAGE=sqlite | postgres` — `Ef*Repository` over a single `CodeReviewDbContext`; the relational
  provider is chosen by a strategy (`UseSqlite` / `UseNpgsql`) from `STORAGE`. **sqlite** always uses
  `%LOCALAPPDATA%/CodeReviewerAgent/review.db` (per-user, survives rebuilds, shared by Console + Api);
  **postgres** takes `DB_CONNECTION`. Schema is created with `EnsureCreated()` (no migrations) and findings
  are their own child table (cascade-deleted with the assessment).

`Review` carries a `ContentHash` (SHA-256) indexed together with `ProjectId`.
`IReviewRepository.GetOrAdd` reuses an existing review of the **same project** with the same content
instead of inserting a duplicate — the golden set relies on this so a case's diff is stored once and
reused across runs, while each run still adds its own assessment.

### Findings schema

```csharp
record Finding(string? File, string? CodeSnippet, Severity? Severity, Category? Category,
               string? Problem, string? Suggestion, int? Line)
{ int Id; int AssessmentId; }   // persistence-only, JsonIgnore(WhenWritingDefault)

enum Severity { Info, Warning, Critical }
enum Category { Bug, Security, Performance, Style, Maintainability, Convention }
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
expected keyword. The result is a **detection rate** per case (e.g. `2/3`). It also
persists to the store: each case's diff via `GetOrAdd` (reused across runs) and each
run's `Analysis`, so the golden analyses are addressable by id afterwards.

### LLM-as-judge — *are the comments good?* (inferential)

`Judge` sends the diff + the agent's review to a **stronger** model (`JUDGE_MODEL`)
with a versioned rubric (`rubrics/judge-<version>.md`) and gets back structured
scores (1–5) for **correctness, actionability, calibration, signal-to-noise**, plus
an overall score and rationale. The judge is intentionally a different, stronger
model than the executor to avoid **self-preference bias**.

There are two ways to run the judge. The **golden** `judge` loads `reviews/eval-results.json`
and writes one aggregate report (`all` = `eval` + `judge`). The **per-assessment** `judge M` scores
a stored assessment and **persists** an `Evaluation` in the store, one LLM call per assessment.
`judge-report` then renders stored evaluations without the LLM — per evaluation, per assessment
(`assessmentId M`), or `golden` for the consolidated set — each including the reviewed diff.

### Iterating prompts

Every analysis records its `promptVersion` and `model`, so prompt/model changes can be compared
with numbers, not by eye — the golden set guards detection, the judge guards quality.

## Tests

```bash
cd src/CodeReviewerAgent
dotnet test
```

The tests exercise the flow end to end with fake `ILlmClient` and `IDiffSource`
implementations (no network, no git), plus `DiffSourceFactory` routing.

`web/` has no test suite yet; `npm run lint` (oxlint) is available.

## Extending

- **New LLM engine** — implement `ILlmClient` in `CodeReviewerAgent.Infra/`, add a case in `LlmClientFactory` (also Infra), document its env vars. HTTP engines take an `IHttpTransport` and are wrapped with the resilient transport in the factory; CLI/SDK engines self-heal and skip it. Core never references the factory — Console/Api instantiate it.
- **New diff source** — implement `IDiffSource` and add a case in `DiffSourceFactory`.
- **New storage backend** — implement the repository contracts (`IProjectRepository` / `IReviewRepository` / `IAssessmentRepository` / `IEvaluationRepository`) in `CodeReviewerAgent.Infra/` and add a case in `RepositoryFactory`.
- **New DB provider** — add an `IDbProviderStrategy` (e.g. `UseSqlServer`) and register it in `RepositoryFactory`; the context and model are unchanged.
- **New prompt / rubric version** — add `prompts/review-v6.md` (or `rubrics/judge-v2.md`) and point the env var at it.
- **New golden case** — add a `.diff` and an entry in `golden/cases.json`.
- **New Api endpoint** — add a route in `CodeReviewerAgent.Api/Program.cs` (read-only, against the store); mirror the shape in `web/src/types/index.ts` and `web/src/services/api.ts`.
- **New web page** — add a folder under `web/src/pages/` and a route in `web/src/App.tsx`.

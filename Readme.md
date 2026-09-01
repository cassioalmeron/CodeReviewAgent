# Code Review Agent

An agent for automated code reviewing, powered by an LLM.

It reads a diff (the local working tree or a real pull request), sends it to an
LLM with a versioned system prompt and a JSON schema, and gets back a structured
list of findings — each with file, line, severity, category, problem, and
suggestion. Reviews and assessments are persisted as first-class, id-addressable
entities (each assessment carries its run metadata: model, prompt version, tokens,
cost, latency), so a report can be regenerated from a stored assessment without
re-running the LLM.

It also ships an **evaluation harness** to measure review quality with method, not
by eye: a deterministic **golden set** (does the agent catch known problems?) and
an **LLM-as-judge** (are the comments good?).

## Why this exists

Calling an LLM is easy. Everything around the call is not, and that is where
systems built on models fall over in production.

Code review was chosen as the use case because it has a property most demos lack:
you can tell whether the output is any good. A wrong line number is wrong. A
planted bug that goes unreported was missed. That makes the system measurable
instead of merely impressive.

So the agent is the excuse and the harness is the point:

- **Prompts are versioned**, so a change in quality can be attributed to a change in the prompt.
- **Findings are grounded** against the parsed diff. The model cites the offending line, the application derives the line number. Anything computable exactly is never delegated to a model (see [ADR-003](docs/ADRs/ADR-003-line-numbers-in-application.md)).
- **Every run records** model, prompt version, tokens, cost and latency, so a regression has evidence attached.
- **Three evaluation layers** measure different things: a golden set for detection, a trigger eval for skill selection, and an LLM-as-judge for quality — the judge deliberately using a stronger model than the executor to avoid self-preference bias.
- **HTTP calls retry and degrade gracefully**, so an unstable API never blocks a pull request.
- **Engines and storage sit behind interfaces**, so swapping Claude for Ollama, or files for Postgres, is configuration rather than a code change.

None of that is specific to code review. It is what any LLM-powered system needs
before it can be trusted in production, and it is the part that usually gets
skipped.

Every non-trivial decision here was written down before implementation and
revisited afterwards against what actually happened, including the ones that were
reversed. They are in **[docs/ADRs](docs/ADRs/README.md)**.

## Tech stack

- .NET 10 backend split into five projects (Core / Infra / Console / Api / Tests)
- **Core** is a reusable class library — the whole review and evaluation flow lives
  here, so it can be driven by more than one front end (console, web, desktop, MCP)
- **Infra** holds the implementations behind Core's contracts — persistence (the only
  project depending on EF Core) and the LLM engines + HTTP transport + `LlmClientFactory`
- HTTP engines call the LLM APIs with raw `HttpClient` (no SDK), behind a resilient transport (retry + exponential backoff on transient failures)
- Pluggable **LLM engines** (in Infra) behind Core's `ILlmClient` — Ollama / Claude / OpenAI / OpenRouter over HTTP, plus Claude via your local CLI/subscription; the factory is instantiated by Console/Api, never by Core
- Pluggable **diff sources** behind `IDiffSource` (local / staged / files / pull request)
- **Skills** loaded by progressive disclosure — the review prompt carries only the guidelines the diff actually needs, chosen by the model, by globs, or explicitly (`SKILLS`)
- Native **structured output** (JSON schema) → findings as C# records
- xUnit tests over the flow with fake `ILlmClient` / `IDiffSource` implementations
- **Api** — a read-only ASP.NET Core minimal API (Swagger/OpenAPI) that exposes the store to a web viewer
- **web/** — a React 19 + TypeScript + Vite viewer (React Router, styled-components) to browse projects/reviews/assessments/evaluations

## Project layout

```
src/CodeReviewerAgent/
├── CodeReviewerAgent.Core/              # Class library — review + evaluation flow
│   ├── CodeReviewer.cs                  # Review pipeline: diff → skills → LLM → parse → ground (pure step, no I/O)
│   ├── ILlmClient.cs                    # LLM client contract (implementations live in Infra)
│   ├── MessageResponse.cs               # Neutral LLM response shape (model, content, usage, optional real cost)
│   ├── Finding.cs                       # Finding + ReviewResult records, Severity/Category enums
│   ├── FindingValidator.cs              # Grounds findings to added lines; derives the line number
│   ├── Entities.cs                      # Project / Review (+ ContentHash) / Assessment / Evaluation (1→N→N→N)
│   ├── ProjectResolver.cs               # Resolves the current Project from REPO_DIR / cwd (folder = key)
│   ├── IRepository.cs                   # IProject / IReview / IAssessment / IEvaluation repository contracts
│   ├── ProcessRunner.cs                 # Runs external commands (git / gh)
│   ├── CostCalculator.cs                # Per-model cost (Claude + OpenAI; Ollama/subscription/OpenRouter = no estimate)
│   ├── ReportGenerator.cs               # Markdown review report
│   ├── PrCommentFormatter.cs / PrPublisher.cs   # `pr <n> --publish`: format the review, post it with `gh pr comment`
│   ├── Diff/                            # Diff sources + parsing
│   │   ├── IDiffSource.cs / DiffSourceFactory.cs      # Strategy (local/staged/files/pr) + selection from CLI args
│   │   ├── DiffParser.cs / ParsedDiff.cs             # Unified diff → files/hunks/lines with absolute numbers
│   │   └── DiffFilter.cs / DiffSplitter.cs           # Drop .md files; split a diff per file
│   ├── Golden/                          # Golden set
│   │   ├── GoldenCase.cs                # Golden vocabulary: expectations (finding | noFinding), case, result, condition
│   │   ├── GoldenScorer.cs              # The golden verdict as a pure function — detection + trap resistance
│   │   └── GoldenEvaluator.cs / GoldenEvaluatorReport.cs  # Runs + scores the cases / publishes the report
│   ├── Judge/                           # LLM-as-judge
│   │   ├── Judge.cs                     # The judge call: absolute scoring + pairwise comparison (inferential layer)
│   │   ├── JudgeRunner.cs / JudgeResultsStore.cs / PairwiseJudgeReport.cs  # Pairwise: pairs + judges reviews / appends each outcome as JSON Lines / renders the verdict report
│   │   └── JudgeReportGenerator.cs      # Absolute: renders stored-assessment scores
│   ├── Skill/                           # Progressive disclosure of review guidelines
│   │   ├── SkillCatalog.cs / SkillFrontmatter.cs / SkillPrompt.cs   # Discovery, parsing, prompt fragments
│   │   ├── ISkillSelector.cs / SkillSelectorFactory.cs / LlmSkillSelector.cs / SkillSelectors.cs  # Who picks the skills (SKILLS)
│   │   └── SkillTriggerEvaluator.cs     # Trigger eval: are the right skills selected?
│   └── assets/                          # Copied to the build output and read at runtime
│       ├── prompts/review-v1..v5.md     # Versioned review system prompts (v3 is the default)
│       ├── prompts/skill-selection-v1.md / skill-guidelines-v1.md   # Skill prompt fragments
│       ├── rubrics/judge-v1.md / judge-v2.md  # Versioned judge rubrics (v1 absolute, v2 pairwise)
│       ├── skills/{csharp,csharp-modern,react}/SKILL.md   # Bundled skills (not versioned — see below)
│       ├── evals/golden/                # cases.json + 15 diffs + a ground-truth .md each (detection + traps)
│       └── evals/triggers/              # cases.json + 10 labelled diffs (skill selection)
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
│   ├── Telemetry.cs                     # OpenTelemetry wiring — traces/metrics/logs over OTLP
│   └── .env.example                     # Only STORAGE / DB_CONNECTION — no LLM keys, no writes
│
├── CodeReviewerAgent.Tests/             # xUnit tests with fake ILlmClient / IDiffSource
│
└── web/                                 # React + TypeScript + Vite viewer for the Api
    ├── src/App.tsx                      # Routes: projects / reviews / assessments / evaluations (list + detail)
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
# Repository to analyze — git/gh run here. Blank = the current directory.
REPO_DIR=

# Which engine runs the review: ollama | claude | openai | openrouter | claude-code | claude-cli
LLM_ENGINE=claude

# Which versioned prompt to use (assets/prompts/review-<version>.md)
PROMPT_VERSION=v3

# Optional: a second prompt version for `eval` to compare against PROMPT_VERSION — one pass over
# the same diffs, both sides scored separately. Unset, `eval` behaves exactly as it does today.
PROMPT_VERSION_COMPARISON=

# Which skills reach the review prompt:
#   all (or unset) — the model picks from the catalog
#   globs          — mechanical `applies-to` matching, no extra LLM call
#   off            — no skills at all
#   <name>,<name>  — exactly these, no extra LLM call
SKILLS=all
SKILL_PROMPT_VERSION=v1        # assets/prompts/skill-selection-<version>.md + skill-guidelines-<version>.md

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

# Claude subscription engines (LLM_ENGINE=claude-code | claude-cli) — no API key
CLAUDE_CODE_MODEL=             # optional model override
CLAUDE_CLI_PATH=               # optional explicit path to claude.exe (claude-cli only)

# Evaluation
GOLDEN_RUNS=3                  # runs per golden case (averages out non-determinism)
GOLDEN_PARALLELISM=4           # concurrent LLM calls in the golden set
SKILL_EVAL_RUNS=3              # runs per trigger-eval case (`skills-eval`)
JUDGE_MODEL=claude-sonnet-4-6  # stronger than the executor, to avoid self-preference bias
RUBRIC_VERSION=                # assets/rubrics/judge-<version>.md; unset = v2 for `judge` (pairwise), v1 for `judge <assessmentId>` (absolute)
JUDGE_RUNS=3                   # executions per pair in the pairwise judge, slots re-randomised each time
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
dotnet run -- pr 42 --publish    # ...and post the findings back with `gh pr comment`
dotnet run -- --json             # same review, findings as JSON on stdout (progress goes to stderr)
dotnet run -- 12 --json          # re-print stored assessment 12 in that same shape (no LLM)

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

# Inspect the skills
dotnet run -- skills             # list the discovered catalog + validation diagnostics  (no LLM)
dotnet run -- skills csharp      # print the block that skill injects into the prompt    (no LLM)

# Evaluate the agent
dotnet run -- eval               # golden set (detection + traps); persists reviews + assessments to the store
dotnet run -- eval extension-block          # only the named case(s), comma-separated
dotnet run -- judge              # pairwise judge over reviews/eval-results.json (aggregate report)
dotnet run -- all                # eval + judge in a single run
dotnet run -- skills-eval        # trigger eval: are the right skills selected? (selection call only)
```

Only `assess N`, `judge`, `judge M`, `eval`, `all` and the one-shot review commands call the LLM;
`review`, `report`, `skills`, `projects` and every `judge-report` do not. A review can have many
assessments (re-run `assess N` with a different prompt or model); an assessment can have many judge
evaluations.

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

### Observability (optional)

The Api ships OpenTelemetry: traces, metrics and logs over OTLP (`Telemetry.cs`). Point it at any
OTLP receiver by setting one variable:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18889
```

Any OTLP collector works. The **Aspire Dashboard** is a convenient one for local use, and it runs as
a plain container:

```bash
docker run -d --restart unless-stopped -p 18888:18888 -p 18889:18889 \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

On Windows, `scripts/otel-dashboard.ps1` does that for you and is idempotent — it reuses any running
Aspire Dashboard container instead of starting a second one (`-Force` to create regardless,
`-Remove` to drop only the one it created).

The dashboard is shared infrastructure, not part of this project. One instance serves any number of
applications, so if you already run one, reuse it and just point the variable at it. The Api defaults
to `localhost:18889` in its `launchSettings.json`.

Telemetry is off whenever `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, so a collector is never required to
run the Api. Instrumentation is automatic only: incoming requests, `HttpClient` calls and EF Core
commands (the SQL shows up on the span when `STORAGE=sqlite|postgres`; `files` produces no database
spans).

## How a review works

`CodeReviewer` pipeline:

1. **Get the diff** from the `IDiffSource` selected by `DiffSourceFactory` from the CLI args.
2. **Pick the skills** — `SkillCatalog.Discover()` reads just the `name` + `description` of each
   `assets/skills/<name>/SKILL.md`, and the `ISkillSelector` chosen by `SKILLS` decides which ones
   apply. Only those have their full instructions loaded into the prompt.
3. **Build the request** with the versioned system prompt (`assets/prompts/review-<version>.md`),
   the selected skills, and a JSON schema describing the expected output.
4. **Call the LLM** through the selected `ILlmClient` (implemented in Infra); each engine
   translates the neutral request into its own API shape (Claude's `output_config.format`,
   Ollama's `format`, OpenAI's / OpenRouter's `response_format.json_schema`) and maps the
   response back to a shared `MessageResponse`. HTTP engines send through a resilient transport
   that retries transient failures (429 / 5xx / network / timeout) with backoff.
5. **Parse and ground** — deserialize into a `ReviewResult` (summary + findings), then
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

## Skills

Review guidelines that would bloat every prompt live as **skills** — one folder per skill under
`assets/skills/`, each with a `SKILL.md` carrying frontmatter (`name`, `description`,
`metadata.applies-to`) and the instructions themselves. Three ship with the repo: `csharp`
(conventions), `csharp-modern` (the .NET 10 / C# 14 language baseline, so current syntax isn't
reported as an error) and `react`.

They load by **progressive disclosure**: only the catalog — name and description — is offered up
front, and only the selected skills have their body injected. `SKILLS` decides who selects:

| `SKILLS` | Who picks | Extra LLM call |
| --- | --- | --- |
| `all` (or unset) | the model, from the catalog | yes, a small selection call |
| `globs` | `metadata.applies-to` against the changed paths | no |
| `off` | nobody — no skills at all | no |
| `<name>,<name>` | you | no |

`dotnet run -- skills` lists the catalog with validation diagnostics, and `dotnet run -- skills
<name>` prints the exact block that skill injects — both without calling an LLM.

> `assets/skills/` is **not versioned in this repository** (it is excluded locally, in
> `.git/info/exclude`): the guidelines change often and will eventually come from a separate
> project. A fresh clone runs with an empty catalog and stays silent about it, so golden-set
> numbers from a clone are baseline numbers, not harness numbers.

## Evaluation

Three layers measure different things:

### Golden set — *does the agent catch the problem, and does it stay quiet when there is none?*

`assets/evals/golden/` holds **15 diffs whose correct outcome is known**, declared in `cases.json`.
Each runs `GOLDEN_RUNS` times (LLM output is non-deterministic, so one run is a noisy
sample), and each carries one of two expectations:

- **Detection** (12 cases) — a problem was planted, and some finding must point at the
  right file and mention an expected keyword.
- **Trap resistance** (3 cases) — the code is **correct**, and carries a bait: a modern
  C# construct an older model mistakes for a syntax error. The model loses the case only
  by flagging that bait; a legitimate remark elsewhere in the diff is ignored, otherwise
  the trap would just reward saying little.

The two rates are reported **separately and never summed** — detection and resistance
answer opposite questions, and one number hides which side failed. The report also breaks
them down by the C# version each case requires, and states which skills were active, so a
run with the harness is never confused with one without it.

Each case has a ground-truth `.md` next to its `.diff` explaining the defect — or, for a
trap, why the code is correct. Tests enforce that every case has one.

The LLM calls run concurrently (`GOLDEN_PARALLELISM`); scoring and persistence stay
sequential. Results persist to the store: each case's diff via `GetOrAdd` (reused across
runs) and each run's `Assessment`, so the golden assessments are addressable by id
afterwards.

`dotnet run -- eval <case>` narrows to named cases — the tight loop for tuning a prompt or
a skill without paying for a full pass.

### Trigger eval — *are the right skills selected?*

`assets/evals/triggers/` holds **10 labelled diffs** (`expectedSkills`, split `train` /
`validation`), including near-misses that should trigger nothing. `dotnet run -- skills-eval` runs
**only the selection step** — no review — so a full pass costs a fraction of one review. A skill
passes a case when its trigger rate over `SKILL_EVAL_RUNS` clears 0.5 while the others stay below.

It honours `SKILLS`, so `SKILLS=globs` scores the mechanical strategy on the same cases with no LLM
call at all — its baseline is **8/10**, missing both near-miss cases by over-triggering. Nothing is
persisted to the store; the output is a console table plus a markdown report.

### LLM-as-judge — *are the comments good?* (inferential)

`Judge` sends a diff (plus what it needs to compare) to a **stronger** model (`JUDGE_MODEL`) with a
versioned rubric (`assets/rubrics/judge-<version>.md`) and gets back a structured judgment. The judge is
intentionally a different, stronger model than the executor to avoid **self-preference bias**.

There are two independent ways to run it, on purpose: a real diff reviewed once has nothing to
compare, so it gets an absolute score; the golden set reviews the same diff under two prompt
versions on purpose, so it gets a comparative verdict.

- **`judge` (no id), pairwise, rubric v2** — loads `reviews/eval-results.json`, which must carry
  exactly two `PromptVersion`s per diff (produce it with `PROMPT_VERSION_COMPARISON` set on `eval`),
  and pairs them positionally — run *i* of one side against run *i* of the other. Each pair is
  judged `JUDGE_RUNS` times, re-randomising which review sits in prompt slot A vs B every execution
  so a judge that favours a slot shows up as an unstable verdict rather than a stable wrong one.
  Verdicts (`A`/`B`/`tie` per criterion — correctness, actionability, calibration, signal-to-noise,
  conciseness, overall) are majority-voted, translated back into prompt-version terms, and rendered
  as a verdict table — never averaged, since there is no numeric distance between "v3", "tie" and
  "v5". `all` = `eval` + `judge`. Nothing is persisted to the store.

  Every judged execution is appended to `reviews/judge-results.jsonl` (JSON Lines, one execution per
  line) the moment it comes back, not batched until the run ends — so a fatal error mid-run (quota,
  network, anything) loses at most the call in flight. Re-running `judge` reads that file back,
  skips every `(diff, pair, run)` already recorded, and judges only what's left; running it again
  after a fully complete run costs nothing, since everything is skipped and the report is just
  rebuilt from disk. The report always comes from `judge-results.jsonl`, never from memory, and
  flags itself as a **partial run** whenever the file doesn't yet cover every planned pair.
- **`judge M`, absolute, rubric v1** — scores one stored assessment on a 1–5 scale per criterion and
  **persists** an `Evaluation`, one LLM call per assessment. `judge-report` then renders stored
  evaluations without the LLM — per evaluation, per assessment (`assessmentId M`), or `golden` for
  the consolidated set — each including the reviewed diff.

### Iterating prompts

Every assessment records its `promptVersion` and `model`, so prompt/model changes can be compared
with numbers, not by eye — the golden set guards detection, the judge guards quality.

## Tests

```bash
cd src/CodeReviewerAgent
dotnet test
```

216 xUnit tests, no network and no git: the review flow runs against fake `ILlmClient` /
`IDiffSource` implementations, the HTTP transport against a stub handler, and the EF repositories
against a temporary SQLite file. They also cover the diff parser and filter, finding grounding,
cost, the golden scorer and reports, the skill catalog and selectors, and the judge (both paths).
`GoldenFixtureTests` additionally guards the fixtures themselves — every case needs its ground-truth
`.md`, and every trap's bait snippet must sit on an added line.

`web/` has no test suite yet; `npm run lint` (oxlint) and `npm run build` are available.

## Extending

- **New LLM engine** — implement `ILlmClient` in `CodeReviewerAgent.Infra/`, add a case in `LlmClientFactory` (also Infra), document its env vars. HTTP engines take an `IHttpTransport` and are wrapped with the resilient transport in the factory; CLI/SDK engines self-heal and skip it. Core never references the factory — Console/Api instantiate it.
- **New diff source** — implement `IDiffSource` and add a case in `DiffSourceFactory`.
- **New storage backend** — implement the repository contracts (`IProjectRepository` / `IReviewRepository` / `IAssessmentRepository` / `IEvaluationRepository`) in `CodeReviewerAgent.Infra/` and add a case in `RepositoryFactory`.
- **New DB provider** — add an `IDbProviderStrategy` (e.g. `UseSqlServer`) and register it in `RepositoryFactory`; the context and model are unchanged.
- **New prompt / rubric version** — add `assets/prompts/review-v6.md` (or `assets/rubrics/judge-v3.md`) and point the env var at it. Anything new under `assets/` also needs a matching `<None Include>` glob in `CodeReviewerAgent.Core.csproj`, or it never reaches the build output the code reads from.
- **New golden case** — add a `.diff`, a `.md` with the ground truth, and an entry in `assets/evals/golden/cases.json` (`expect` is `"$type": "finding"` or `"noFinding"`). For a trap, check the code actually compiles first.
- **New trigger case** — add a `.diff` and an entry in `assets/evals/triggers/cases.json` (`expectedSkills` + `set`).
- **New skill** — add `assets/skills/<name>/SKILL.md` with `name` / `description` frontmatter (and `metadata.applies-to` if it should work under `SKILLS=globs`).
- **New Api endpoint** — add a route in `CodeReviewerAgent.Api/Program.cs` (read-only, against the store); mirror the shape in `web/src/types/index.ts` and `web/src/services/api.ts`.
- **New web page** — add a folder under `web/src/pages/` and a route in `web/src/App.tsx`.

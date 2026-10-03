# RUNBOOK

Operating this system without relying on anybody's memory. Five scenarios: normal operation,
deploy, failure recovery, debugging, and rolling a versioned prompt forward and back.

## What "production" means here

This is not a hosted service. There is no container, no compose file and no deployment target in
this repository, and writing this document as if there were would make it useless. What actually
runs unattended is this:

- **Two GitHub Actions workflows.** [`build.yml`](../.github/workflows/build.yml) builds, tests and
  lints on every push to `main` and every pull request, and the test step is also the architecture
  fitness function. [`golden.yml`](../.github/workflows/golden.yml) runs the golden set against a
  stored baseline whenever a prompt, a skill, a golden case or the workflow itself changes, and it
  spends real money on every run, around US$ 0.03 on `gpt-4o-mini`.
- **A store that outlives any single run** — files, SQLite or Postgres, per `STORAGE`.

Everything else is started by hand: the Console CLI, the read-only Api and the web viewer. So
**deploy** below means promoting a change to `main`, where the unattended part lives, and
**production** means that store and those two workflows.

## Before any scenario: where configuration lives

One `.env` at the repository root configures the Console and the Api. `EnvFile` (Infra) walks up
from the executable and loads every one it finds, nearest first, never overwriting a value already
set, and a variable already exported in the shell wins over every file. Start from the configuration
template at the repository root, described in the Readme's Configuration section.

Three variables decide what a command does and what it costs:

| Variable | What it decides |
|---|---|
| `LLM_ENGINE` | which provider is called; `ollama` and the Claude subscription engines cost nothing per call |
| `PROMPT_VERSION` | which file under `assets/prompts/` is sent as the system prompt |
| `STORAGE` | where reviews and assessments are written |

And the commands split cleanly. `assess`, `judge`, `eval` and `all` call the model. `review`,
`report`, `skills`, `projects` and every `judge-report` do not, which makes them always safe to
re-run while diagnosing something.

---

## 1. Normal operation

Reviewing a change, day to day. Run from
`src/CodeReviewerAgent/CodeReviewerAgent.Console`.

```bash
dotnet run                       # the local working tree: staged if anything is staged, else git diff HEAD
dotnet run -- staged             # only what is staged
dotnet run -- files A.cs B.cs    # only these files
dotnet run -- pr 42              # pull request 42, through `gh pr diff`
dotnet run -- pr 42 --publish    # and post the findings back with `gh pr comment`
```

One command captures the diff, reviews it and writes the report, persisting the review and the
assessment. Markdown files are dropped before the call: they are prose, not code to review.

The same work is also three independent, id-addressable steps, which is what to use when something
needs to be repeated without paying twice:

```bash
dotnet run -- review             # capture the diff only, no model call  -> "Review saved with id N"
dotnet run -- assess N           # send stored review N to the model     -> "Assessment saved with id M"
dotnet run -- report M           # regenerate the report from assessment M, no model call
```

Reviewing a pull request needs the GitHub CLI installed and authenticated (`gh auth login`).
`REPO_DIR` points `git` and `gh` at the repository to analyse; blank means the current directory.

**Routine checks, none of which cost anything:** `dotnet run -- projects` lists the stored projects,
`dotnet run -- skills` lists the discovered skill catalogue with its validation diagnostics, and
`dotnet test` from `src/CodeReviewerAgent` runs the suite including the five architecture rules.

---

## 2. Deploy

Promoting a change to `main`, which is where the unattended part runs.

1. **Work on a branch.** Never commit straight to `main`.
2. **Run `dotnet test` locally** from `src/CodeReviewerAgent`. This is also the fitness function:
   Core must not reference Infra or the Api, no provider type may leak into the domain, Core must
   not write to the terminal or read the environment, and the Infra-to-Core arrow must not invert.
   A violation fails here, before CI.
3. **Open the pull request.** `build.yml` runs. If the change touched
   `assets/prompts/**`, `assets/skills/**`, `assets/evals/golden/**` or `golden.yml` itself, then
   `golden.yml` runs too and compares the result against the stored baseline.
4. **Watch for one caveat:** `golden.yml` skips pull requests opened from forks, because a fork gets
   no repository secrets and the run could not call the model. On a fork contribution the golden
   check only happens after the merge, on `main`.
5. **Merge.** `build.yml` runs again on `main`.
6. **If the change touched prompts, skills or golden cases, confirm the golden run on `main` went
   green.** That run is the one that matters, because it is the one nobody is watching.

**Rolling back code** is the same path in reverse: revert the commit on a branch, let both workflows
run on the pull request, merge. Rolling back a *prompt* is scenario 5, and it is not the same thing.

The Api and the viewer are started by hand and have nothing to deploy:

```bash
cd src/CodeReviewerAgent/CodeReviewerAgent.Api && dotnet run   # http://localhost:5180, Swagger at /swagger
cd src/CodeReviewerAgent/web && npm run dev                    # http://localhost:5173
```

The Api is read-only and never calls a model. It needs only `STORAGE` and `DB_CONNECTION`, and an Api
pointed at a different store than the Console shows an empty screen, which is the usual cause of
"the viewer lost my data".

---

## 3. Failure recovery

### A paid golden run died part way

**Symptom.** `Golden set stopped: <message>` and `N round(s) were kept. Run eval again to resume
from them.` on stderr, exit code 1, and **no report**. That is deliberate: a rate over a partial set
is not a smaller truth, it is a wrong number.

**Action.** Run `dotnet run -- eval` again. Every paid round was appended to
`reviews/golden-rounds.jsonl` under `EVAL_OUTPUT_DIR` the moment it came back, so the rerun reads
that file and buys only what is missing. A run that died on round 55 of 60 keeps the 54 it paid for.

**The one trap.** A stored round is reused only when **the model and `SKILLS` both match**. Change
either and the old rounds are ignored and bought again, by design: a round from another model answers
a different question, and silently counting it would publish the old model's numbers under the new
model's name. When the model cannot be established at all, which happens on the CLI engines with the
model override blank, rounds are still recorded but never reused, and the run says so.

### The provider is unstable or rate limiting

Already handled, and worth knowing before treating it as an incident. The HTTP transport retries up
to 5 attempts with exponential backoff, 1s then 2s then 4s, and honours the server's own
`Retry-After` when it names one, because a token-per-minute window reopens on the provider's
schedule and not on a curve we invented. Only 429, 5xx, network errors and timeouts retry;
every other 4xx fails fast.

So a single transient failure in the output is not an incident. A command that fails after
exhausting the attempts is.

### A slow model keeps timing out

Raise the timeout, do not retry harder: **a timed-out attempt is still billed.**
`OPENROUTER_TIMEOUT_SECONDS` and `OLLAMA_TIMEOUT_SECONDS` default to 600 for that reason, since
models that reason by default outlast a 120 second limit.

### The store is broken or empty

With `STORAGE=files`, entities are JSON files and the next id is the highest existing id plus one,
read from the file names, so renaming or deleting those files changes id allocation.

With `sqlite` or `postgres`, the schema comes from the EF migrations in `Infra/Migrations/` and is
applied on first use. **A database created by the old `EnsureCreated()` cannot be migrated:** point
`DB_CONNECTION` at a new one. SQLite with a blank connection string falls back to
`%LOCALAPPDATA%/CodeReviewerAgent/review.db`.

### CI went red on the golden set

Read the comparison table on the run's summary page first. There are two different red lights, and
they are not the same problem: a **regression**, meaning a case lost ground against the baseline, or
a **configuration mismatch**, meaning the run and the baseline are not comparable at all. The second
one is scenario 5.

---

## 4. Debugging in production

The governing idea: **reproduce without paying.** Every assessment records its model, prompt
version, tokens, cost and latency, so a regression arrives with its evidence attached, and the
report can be rebuilt from storage.

```bash
dotnet run -- projects              # which projects exist
dotnet run -- report M              # rebuild the report from stored assessment M, no model call
dotnet run -- M --json              # reprint stored assessment M as JSON, no model call
dotnet run -- judge-report golden   # consolidated report over the golden set, no model call
```

**The viewer** is the fastest way to look around: projects, reviews, assessments, evaluations, and a
**Runs** page listing every golden run with its model, skills, duration, cost, tokens, latency and
verdict, which opens into the five gates against their floors and the 15 cases. The project
dashboard reports latency as P50, P95 and P99 rather than an average, because an average hides the
tail that actually hurts.

**One number to read carefully: *discarded*.** Findings dropped by grounding are counted separately.
A high discarded count means the model saw the problem and cited a line the diff does not add, which
is a different failure from never seeing it, and the two call for different fixes.

**Traces, metrics and logs** are off unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so no collector is
ever required to run the Api. Point it at any OTLP receiver; on Windows
[`scripts/otel-dashboard.ps1`](../scripts/otel-dashboard.ps1) starts an Aspire Dashboard and is
idempotent, reusing a running container instead of starting a second one. Instrumentation is
automatic only, covering incoming requests, `HttpClient` calls and EF Core commands, so
`STORAGE=files` produces no database spans at all.

**In CI**, the golden comparison table is written to the run's summary page, and the full report is
uploaded as the `golden-report` artifact even when the step failed.

---

## 5. Updating and rolling back a versioned prompt

The prompt is a production artefact, so it is versioned and rolled back like one, and that is not
the same operation as rolling back code.

### How a prompt version exists

A version is a file, `assets/prompts/review-<version>.md`, read at runtime from the build output.
`review-v1.md` through `review-v5.md` exist today. **Versions are added, never edited in place:** an
assessment recorded under v5 has to keep meaning what it meant when it was recorded.

### Which version runs is decided in two places

| Where | What it governs |
|---|---|
| `PROMPT_VERSION` in `.env` | every local run |
| `PROMPT_VERSION` in `.github/workflows/golden.yml` | the unattended CI check |

**Change one and not the other and the gate measures a version nobody runs.** This is the single
easiest mistake to make here.

### Moving a prompt forward

1. **Add the new file**, `review-v6.md`. Do not edit an existing version. No project file needs
   touching: the Core project copies `assets/prompts/**/*.md` to the build output by wildcard, so a
   new version is picked up by the next build. The Readme's warning about adding a `<None Include>`
   glob applies to a new *kind* of asset folder, not to a new version of an existing one.
2. **Measure it against the current one in a single pass.** Set `PROMPT_VERSION_COMPARISON` to the
   old version and run `dotnet run -- eval`: both sides run over the same diffs and are scored
   separately. The run announces that this doubles its cost.
   For a tight loop while tuning, `dotnet run -- eval <case>` narrows to named cases.
3. **If it is better, accept it on purpose.** Set `PROMPT_VERSION` to v6 in `.env` *and* in
   `golden.yml`, then write the new reference by pointing `GOLDEN_BASELINE_WRITE` at
   `src/CodeReviewerAgent/CodeReviewerAgent.Core/assets/evals/golden/baseline.json`. The acceptance
   then appears in the commit as a diff of that file, which is the whole point: a change in what the
   model is told is never accepted silently.
4. **A narrowed run cannot become a baseline.** `GOLDEN_BASELINE_WRITE` is ignored, with exit code 1,
   when the run covered only some cases, because as a baseline it would silently drop the others.

### Rolling a prompt back

1. **Point `PROMPT_VERSION` back, in both places.**
2. **Restore the matching `baseline.json` from git history.** The baseline carries the configuration
   it was measured under, which is model, skills setting, prompt version, rounds and temperature, and
   a run under a different configuration is **refused rather than compared**. So a rollback that
   moves the version and leaves the newer baseline in place turns CI red with a configuration
   mismatch, not with a regression, and the message says which.
3. **No code change and no redeploy are involved.** The prompt is an asset, and the version is
   configuration.

### What the gate does and does not tell you

A case counts as regressed when it loses **3 or more of its 5 clean rounds**: the tolerance is 2,
measured in plan 015, because at the provider's default temperature identical runs moved a case by up
to 3 of 5 and no tolerance could separate a real drop from the dice. That is why the CI run pins
`LLM_TEMPERATURE` to 0.

The baseline on `main` today was measured with `gpt-4o-mini`, skills `globs`, prompt v3, 5 rounds,
temperature 0. **It speaks for that configuration and no other.** Rolling a prompt back says nothing
about how that prompt behaves under a different model, and a green golden run is not a statement
about the engine someone runs locally.

---

## Where to look first

| Symptom | First place to look |
|---|---|
| The viewer shows nothing | `STORAGE` and `DB_CONNECTION` differ between the Api and the Console |
| A golden run exited 1 with no report | It died part way; rerun `eval`, which resumes from `golden-rounds.jsonl` |
| CI red on the golden set | The comparison table on the run's summary page: regression or configuration mismatch |
| Golden rounds being bought again | The model or `SKILLS` changed, so stored rounds no longer match |
| A prompt change had no effect | `PROMPT_VERSION` was changed in `.env` but not in `golden.yml`, or the reverse |
| Many findings on the right problem, wrong lines | The *discarded* count, not the detection rate |
| One failed call in the output | Normal: the transport retries 5 times with backoff |
| Repeated timeouts | Raise the engine's timeout; a timed-out attempt is billed anyway |
| An architecture rule failed in CI | The message names the class; the five rules are in `ArchitectureTests.cs` |

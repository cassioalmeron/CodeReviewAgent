# ADR-013: Rebuild the skills harness on the Agent Skills specification, with activation decided by the model instead of mechanical glob matching

**Date:** 2026-08-05

**Status:**
- [ ] Proposed
- [x] Accepted
- [ ] Deprecated
- [ ] Superseded by ADR-___

**Decision maker:** Cassio Almeron

**Stakeholders consulted:** Rodrigo (Rambo)

---

## Block 1: Context

### 3.1 What is the problem or need

Skill support had existed since plan 004, in a single file, `SkillLoader.cs`, and it was entirely mechanical. `Load()` scanned `skills/*/SKILL.md`, `Parse()` read the frontmatter with two regular expressions and picked up **only** `applies-to`, and `BuildGuidelines()` matched globs against the diff's paths and concatenated the entire body of every skill that matched, under a `# Project guidelines` heading.

Three things follow from that. The model never knew skills existed and never decided anything: it received text. There was no disclosure tier, so a skill that matched cost its whole body in the prompt. And `name` and `description`, which the specification treats as required, were not read at all, so a skill without `applies-to` was silently discarded.

Two skills had been delivered this way, `csharp` and `react`, and it was never verified that they added anything.

The problem is not only what the loader failed to do today, it is what it would prevent tomorrow. Skills are the intended path for carrying knowledge to the model, and every skill written on top of the mechanical base would be born tied to it: no tiers, no selection, no trace of what had been active. Fixing this with two skills costs a refactor; fixing it with ten costs a migration. Keeping the old loader would not have been a saving, it would have opened a line of technical debt that only gets more expensive.

### 3.2 Why this decision matters now

The next delivery in the sprint, golden set v2, depends on a question the old loader cannot answer: does injected knowledge make up for what the model does not know?

Answering it requires being able to run the same cases with **no skill at all**, and to attribute the result to the skill rather than to the model. Text substitution offers neither. There is no baseline condition, there is no record of what was active, and there is no notion that a choice was ever made.

Deciding now, before the golden set, means having the measuring instrument ready before the measurement.

### 3.3 Constraints

- Time: Sprint 5 runs to 2026-08-09, with its review at Meeting 03 on 2026-08-10. This work was not in the scope of US-009 and competes with it
- Cost: a tight monthly API cap, and any call added here multiplies across every review
- Architecture: a review is a single call, a workflow and not an agent (ADR-002). There is no tool loop available
- Performance: this is a POC, nothing critical
- Other: the MVP is 100% independent of third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Keep the mechanical loader and improve the globs

**Description:** Keep `applies-to` as the selection criterion and invest in better glob patterns and better skill bodies.

**Pros:**
- Zero additional cost and latency, because there is no extra call
- Fully deterministic: the same diff always receives the same skills
- It already worked, so no refactor
- Measured afterwards, this strategy scores 8/10 on the trigger eval, consistently

**Cons:**
- The model never knows skills exist, so "the model chose" can be neither expressed nor measured
- No tiers: a skill that matches always costs its whole body
- `description` goes unused, so selection does not improve by writing better prose
- A glob does not read content, so it over-fires on near-miss diffs, for example a `.cs` file where only a comment changed
- There is no baseline condition, which is precisely what blocks the golden set's measurement

**Estimated cost:** zero to implement, but it leaves the measurement question with no possible answer

---

### Alternative B: Rebuild on the Agent Skills specification, with activation decided by the model, chosen

**Description:** Adopt the pattern published at agentskills.io: progressive disclosure in three tiers, activation decided by the model from each skill's `description`, lenient validation, and the skill body wrapped in `<skill_content>`. Selection becomes a strategy behind `ISkillSelector`, chosen by the `SKILLS` variable: `all` lets the model choose, `globs` keeps the mechanical path, `off` disables skills entirely, and an explicit list pins exactly which ones apply.

**Pros:**
- A published pattern instead of an invention of my own, with the design questions already answered
- Tier 1 sends only names and descriptions, so the catalogue stays cheap however many skills exist
- Tier 2 injects only the chosen bodies, so an unrelated skill costs nothing
- `SKILLS=off` gives the baseline the golden set needs, and `SKILLS=globs` preserves alternative A as a control group
- Selection becomes measurable in its own right, which is what became the trigger eval
- Lenient validation makes a malformed skill degrade rather than vanish silently

**Cons:**
- One extra LLM call on every review, with its cost and latency
- It depends on the model honouring structured output, and some do not
- More code: 854 lines in Core, against one file before

**Estimated cost:** a few days of implementation, plus a small permanent cost per review

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, rebuild on the Agent Skills specification

### Rationale

The deciding factor is not the quality of the selection, it is that alternative A makes the next measurement impossible. Without a strategy boundary there is no `SKILLS=off`, and without `SKILLS=off` there is no baseline to compare against. The entire golden set experiment rests on that single variable.

Following a published specification instead of extending a design of my own paid off in a way worth recording. Three decisions came straight from it and would not have been reached otherwise: progressive disclosure in three tiers, lenient validation, and activation from the `description` with `applies-to` demoted to a fallback. Left to my own devices, the natural path was to keep improving the glob.

The most consequential effect was not in the code. Reading the specification's document on evaluating skills, with the plan already under way, forced a redesign of its phase 4, and it was out of that redesign that the `SKILLS=off` condition came. The original plan did not include it. Studying the pattern did not only shape the implementation, it produced the instrument the next phase would need.

Keeping the mechanical path as `SKILLS=globs`, instead of deleting it, costs nothing and turns the previous design into a control group.

### Accepted trade-offs

**One extra LLM call on every review.** Its tokens, cost and latency are recorded on the `Assessment` alongside the review's own, so the price is visible instead of hidden.

**The uncomfortable trade-off: the trigger eval says the mechanical strategy ties with or beats the model.** The glob scored 8/10 across five passes; the model varied between 6/10 and 8/10 across six. The two cases the glob gets wrong are deliberate near-misses, which exist precisely to expose that weakness.

That result is recorded, not buried. It is mitigated by two facts, and not erased by them: on the golden set's real diffs the model-based selector was right in 45 of 45 rounds, and every reading of the trigger eval so far used one round per case, which is dominated by noise. The comparison that would settle it, with three rounds or more, has not been run yet.

### What was NOT considered, and why

Discovering skills inside the repository under review, in `.claude/skills/` or similar, was left out for now but is a planned future step. The obstacle is not one of principle, it is one of order: reading skills from a repository under review turns it into a surface for third-party instructions to reach the model, and that needs a trust design of its own before it can exist. Add to that the fact that skills are going to start coming from a separate project rather than from the agent itself, which changes their origin and therefore the safe way to discover them. It waits until that origin is settled.

Giving `ILlmClient` a tool loop, so the model could open tier 3 resources by itself. The architecture has been a workflow and not an agent since ADR-002, so this was never on the table as a path for this decision.

Versioning skills along the lines of `prompts/review-vN.md`, skills in the judge flow, and delegation to a subagent were left out as scope, not as oversight.

---

## Block 4: Consequences

### Expected positive consequences

- `SKILLS=off` exists, and with it a baseline condition. The golden set experiment reused this wholesale
- Selection is a strategy the pipeline knows nothing about: `CodeReviewer` stopped reading `SKILLS` and stopped branching on it
- The active skills are persisted in `Assessment.Skills` and show up in the report, in the `--json` payload and in the web viewer. Before this, editing a `SKILL.md` changed the effective prompt without leaving a trace
- Adding a skill requires no code, only a folder with a valid `SKILL.md`
- The quality of the selection became measurable in its own right, and cheaply: the trigger eval runs the selection call only, at around US$ 0.0006 per pass over ten cases

### Known negative consequences

- Every review pays for a second call, even when no skill applies
- A model that does not honour structured output returns prose, and the selection comes back empty. Observed with `openrouter` and `tencent/hy3-preview`
- Tier 3 is enumerated but never read: the HTTP engines have no file access, so anything outside the `SKILL.md` body is invisible to the model. Only `claude-cli` and `claude-code` would be able to open it
- Measured afterwards, and unexpected: **every configuration with a skill lowered real bug detection**, from 80.6% at baseline to 63.9%, 55.6% and 68.3% across three versions of the same skill. The harness did not make the model more capable, it reallocated its attention

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The model returns prose instead of the schema, and no skill is selected | High on some providers | Medium | The parser accepts every legitimate shape; an unreadable response is counted in a column of its own instead of being hidden. The fallback to globs was removed on purpose, because it made the trigger eval indistinguishable from the mechanical baseline |
| The extra call's cost grows with usage | Medium | Low | Cost recorded per assessment; `SKILLS=globs` and `SKILLS=off` remain available |
| The mechanical strategy turns out to simply be better | Medium | Medium | Both live behind the same interface, so switching is an environment variable, not a rewrite |
| Skills keep suppressing real bug detection | **Confirmed** | High | Measured and published. Every skill change is now evaluated against the baseline, not adopted on faith |

### When to revisit this decision

- [ ] If the trigger eval, run with three or more rounds per case, shows the mechanical strategy consistently ahead of the model
- [ ] If plan 011 extracts the harness into an independent project, because the selection boundary would become a public API
- [ ] If any engine gains a tool loop, which would make tier 3 real instead of decorative

---

## Block 5: Post-decision validation

_Pending. The decision dates from 2026-08-05 and the golden set phase has already produced measurements against it, but over three days and with one round per case on the trigger eval. To be filled in when there are more results, in particular `skills-eval` with three or more rounds and the baseline re-measured._

### Did the decision prove correct?

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, I will write a later ADR to reverse it

### What I learned from this decision

_Pending._

### What I would do differently in hindsight

_Pending._

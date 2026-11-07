# ADR-017: Measure cost-based model routing and leave it off, keeping one model per run

**Date:** 2026-11-07

**Status:**
- [ ] Proposed
- [x] Accepted
- [ ] Deprecated
- [ ] Superseded by ADR-___

**Decision maker:** Cassio Almeron

**Stakeholders consulted:** Rodrigo (Rambo)

The decision was taken without prior consultation, with the measurement report in hand.

---

## Block 1: Context

### 3.1 What is the problem or need

The agent picks one model per run, read from an environment variable before any diff arrives. Every
diff in the run is reviewed by that same model, whether it changes 3 lines or 700.

The requirement was to route by cost: a small diff to the cheap model, a large one to the strong one.

**The premise was inverted, and that surfaced before any code was written.** The agent already ran on
`gpt-4o-mini`, the cheapest of the eight paid models in the model matrix, in both workflows. There was
no expensive default to save against, so routing could only raise the cost.

Worse: of those eight paid models, one passed all five floors of ADR-015, and it sits near the cheap
end. In that matrix, an expensive model is not a good model.

So the real question became a different one: does paying more on large diffs buy quality?

### 3.2 Why this decision matters now

The measurement exists and the code is built. The rule, the set of ten large cases and the report
command are in the repository, and the only thing left is an environment variable.

Leaving that undecided is the worst state available: whoever arrives next finds a finished mechanism
switched off, with no way to tell an oversight from a choice.

### 3.3 Constraints

- **Cost:** a paid measurement needs approval first. This one cost US$ 0.4431 over 375 reviews,
  within the US$ 0.94 approved.
- **Architecture:** ADR-004 already put the models behind a Strategy and a factory, so plugging a
  model in is cheap. ADR-015 fixes the five floors, and they were calibrated on the fifteen small
  cases, so a mixed set cannot be judged against them.
- **The subscription engines are out of scope:** the two that reach Claude through the CLI have no
  per-call price, so cost-based routing lives inside the engines paid by token.
- **Performance:** not critical, this is a proof of concept.

---

## Block 2: Alternatives considered

### Alternative A: Switch routing on in the live path

**Description:** the rule picks the model per diff, in the real pull-request review path. A diff of at
most 50 changed lines and 3 touched files goes to `gpt-4o-mini`, everything else to DeepSeek V4 Flash.

**The comparison is against what production does today.** Production runs `gpt-4o-mini` on every diff,
so it is the report's "cheap everywhere" column, not the strong model's.

**Pros:**

- Improves 4 of the 5 floors against production: detection from 50.5% to 57.1%, precision from 76% to
  79%, exact calibration from 69.8% to 73.3%, and noise down from 3.65 to 2.45 per round.
- Doubles detection on large diffs, from 15.6% to 31.1%, which is where the extra money goes.
- Costs 73% of running the strong model everywhere, which is the other way to buy that improvement.

**Cons:**

- Doubles the cost against production, from US$ 0.0870 to US$ 0.1764, a 103% rise.
- The requirement asked for lower cost. Against the project's real default, it raises it.
- Worsens trap resistance, from 40% to 35%, which is the floor that measures the model accusing
  correct code.
- The improvement approves nothing. None of the three columns passes all five floors.

**Estimated cost:** zero to implement, because the code already exists. The recurring cost is 103%
more per review.

---

### Alternative B: Leave it off, keeping the rule (chosen)

**Description:** routing lives in the repository, versioned and tested, and production keeps one model
per run. Switching it on is an environment variable on the day the decision changes.

**Pros:**

- Production keeps the lowest cost of the three columns, and trap resistance at 40% instead of 35%,
  which is the one floor routing makes worse.
- The measurement is done and dated. The next time this comes up, the number already exists and
  nobody pays again to find it.
- The ten large cases now serve any future measurement, not only this one. That is what the
  repository did not have: every golden case changes 1 to 16 lines.
- It lets the right order emerge. The call's size weighs more than the choice of model.

**Cons:**

- Leaves a measured improvement on 4 of the 5 floors on the table.
- Production stays at 50.5% detection and 3.65 noise per round, the worst of the three columns.
- No saving is realised, because there was none to make: routing cost more than the current default.
- Code built and unused is debt: it has to keep compiling and passing its tests without delivering
  value.
- From the outside it can look as though nothing was delivered. The deliverable is the measurement and
  the decision, not the mechanism switched on.

**Estimated cost:** zero. The cost is maintenance: 15 tests on the rule and 13 on the composer, which
keep running in CI.

---

### Alternative C: Close the requirement as unworkable, building nothing

**Description:** answer that the requirement does not hold, because the agent already runs the
cheapest model in the matrix, and close it with no code and no measurement.

**Pros:**

- Honest about the premise. The requirement asks to reduce cost, and there was no cost to reduce.
- Zero cost, in money and in time.
- It avoided building a mechanism that might end up switched off, which is exactly what happened.

**Cons:**

- The question behind the requirement still stood, and it is a good one: does a large diff deserve a
  better model?
- Closing without measuring would leave the answer as an opinion. This project has already learned,
  in ADR-015 and ADR-016, that opinion about model quality gets it wrong.
- The repository would still have no large case to measure with. Every golden case changes 1 to 16
  lines, while real diffs are the opposite: 48 of the last 55 commits exceed 50 lines or 3 files.
- It would lose the finding the measurement produced for free, that the call's size costs more than
  the choice of model.

**Estimated cost:** zero, which is exactly why it was weighed. Rejected on 2026-11-07, before phase 1.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, leave it off, keeping the rule.

### Rationale

The deciding factor is that routing doubles the cost and buys an improvement that approves nothing.

103% more cost, 6.6 points more detection, 3 more of precision, 5 fewer of trap resistance. And all
three columns fail the five floors. Routing is not the difference between an approved reviewer and a
rejected one, it is a more expensive reviewer that is still rejected.

Where the extra money goes, on the large diffs, the best result anyone reached was 31%. Two planted
defects in three go unseen. Paying double for 31% is paying more for a path that is broken for another
reason.

The floor that gets worse is the one that measures the model accusing correct code. In pull-request
review, that is the wrong comment in front of the person who wrote it.

Alternative C was rejected for a different reason. It saved everything and would have been right if
cost were the only question. What it would have lost is the finding about the call's size: on diffs of
350 to 700 lines the approved model detects 31% and the cheap one 15.6%, against 68% and 78% on small
diffs. That was the most valuable result of the day, and it would not exist had the requirement been
closed in the morning.

### Accepted trade-offs

**A measured improvement on 4 of the 5 floors was left on the table.** Routing delivers more
detection, more precision, better calibration and less noise than the current default. Refusing that
over cost, in a project where a review costs cents, is a choice that has to be said out loud rather
than hidden.

**The measurement contradicts the choice on one point, and it is recorded.** If quality were the only
criterion, switching it on was the right call. What weighed against it was price, and price here is
not absolute but proportional: twice a small amount is still small. Someone reading this in six months
could reach the opposite decision from the same numbers and be reasonable.

**Production keeps the worst numbers of the three columns**, 50.5% detection and 3.65 noise per round.
That is the direct consequence of not switching it on, and it is not a detail: it is the reviewer the
project actually uses.

### What was NOT considered, and why

**Routing down, which is the requirement's literal reading.** Never viable: the agent already ran the
cheapest of the eight paid models, so there was no expensive default to save against. It became
context for the problem rather than an alternative on the table.

**Tuning the rule's thresholds to improve the result.** The 50 and the 3 were chosen before measuring,
deliberately. Moving them after seeing the result is fitting the ruler to the outcome, which is the
error ADR-015 exists to prevent.

**A lower quality floor for the cheap route.** A defensible way out, left aside for time: it needs a
second ruler written and justified, which is work for its own ADR rather than a paragraph in this one.

**Routing with a third model in between.** Of the eight paid models, one passed all five floors, so
there is no measured middle ground to draw from.

---

## Block 4: Consequences

### Expected positive consequences

- The repository now has ten large-diff cases with ground truth, 350 to 700 changed lines each. That
  is what was missing to measure anything at real size.
- The routing rule is finished and versioned. Switching it on is an environment variable, not a
  project.
- The `routing-report` command composes the comparison from rounds already recorded, buying nothing.
  Any new pair of models can be compared for the price of two passes.
- It is now established that the ruler measures the model and not the day: the strong pass reproduced
  September, 68.3% against 68%, two months later under the same condition.
- The roadmap's order changed on evidence: the call's size comes before the choice of model.

### Known negative consequences

- Production keeps the worst measured numbers of the three columns, 50.5% detection and 3.65 noise per
  round.
- Code built and unused stays in the repository: the rule, the composer, the command and 43 tests that
  run in CI without delivering value today.
- The large case set needs maintenance. It is made of real commits with a defect planted by hand, and
  if the repository's history is ever rewritten, the cases lose the trace of where they came from.
- **Measured afterwards, the same day:** production runs with `SKILLS: globs`, and in that condition
  detection on a large diff is half that of the same model with skills off, 3 hits in 45 against 7 in
  45. That is not a consequence of this decision, it is a problem this decision exposed, and it is
  recorded here because this is where it surfaced.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Someone switches routing on without reading this record, because the code is ready | Medium | Medium | This record, plus the execution log of plan 017. There is no guard in the code, and that is accepted |
| The ten large cases age and stop representing the code | High, over time | Medium | Each ground truth names the commit it came from and the lines changed, so the case can be rebuilt |
| The large set being read as a verdict on a model | **Confirmed** | High | No model passed 31% on it. The five floors were calibrated on the fifteen small cases, and the report says so. The set is for comparing, not for approving |
| The measurement not holding for another pair of models | Medium | Low | `routing-report` recomposes the comparison from recorded rounds, so measuring a new pair costs two passes |
| The rule rotting unused | Medium | Low | 43 tests in CI, including one that pins version `v1` together with both thresholds |

### When to revisit this decision

- [ ] When the split-call question is settled and a review stops sending the whole diff in one call.
  The size of the diff reaching the model changes, and the rule starts measuring something else.
- [ ] When a pull-request review comes to cost more than US$ 0.05. Today it costs less than a cent,
  and that is what makes twice as much irrelevant.
- [ ] When a new model enters the matrix, passes all five floors and costs less than DeepSeek V4
  Flash. The strong end changes and the arithmetic is different.
- [ ] When `gpt-4o-mini` passes 50% trap resistance in a measurement. The one floor routing makes
  worse stops being the obstacle.

---

## Block 5: Post-decision validation

### Did the decision prove correct?

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, a later record will reverse it

_Pending._ The decision is from today and production runs exactly as before, so there is nothing to
validate yet.

**What validates it:** the result of the split-call measurement. If splitting the call raises detection
on large diffs, the chosen order was right and routing still does not pay. If splitting does not fix
it, doubling detection from 15.6% to 31.1% comes back to the table with more weight.

### What I learned from this decision

**Initial reading, 2026-11-07.** A comparison is only worth something against the default that
actually runs.

The comparison was built with three columns, and the rationale was nearly closed against the wrong
one: the strong model everywhere, which was never this project's default. Against that column, routing
saved 27%. Against what production actually does, it doubles the cost. The number did not change, what
it was being read against did, and that inverted the conclusion.

The error was caught before the record closed, by reading `pr-review.yml` instead of trusting the
report from memory.

### What I would do differently in hindsight

_Pending._

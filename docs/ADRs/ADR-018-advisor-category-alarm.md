# ADR-018: Advisor with a category alarm

**Date:** 2026-11-14

**Status:**
- [ ] Proposed
- [x] Accepted
- [ ] Deprecated
- [ ] Superseded by ADR-___

**Decision maker:** Cassio Almeron

**Stakeholders consulted:** Rodrigo (Rambo)

The decision was taken without prior consultation. The options were laid out with Claude's help, and
the choice was mine, based on the free check of the alarms and on a ruler written before the paid
measurement.

---

## Block 1: Context

### 3.1 What is the problem or need

The agent reviews each diff with a single model, `gpt-4o-mini`, the cheapest in the model matrix. It
catches the obvious and misses the subtle. In the measurement of 2026-11-07, over the 25 cases, it
detected 50.5% of the defects and fell for 60% of the traps.

The strong model, DeepSeek V4 Flash, costs almost 3 times as much, US$ 0.2427 against US$ 0.0870, and
it also fails the five floors of ADR-015. Running the strong model everywhere does not solve it. The
size-based routing of ADR-017 doubled the cost without passing anything.

The requirement asked for another path: the cheap model reviews everything, and the strong one comes in
only when the cheap one is in doubt. This is the pattern Anthropic calls the advisor. The strong model
gives guidance, and the cheap one writes the final answer.

The problem is that the agent has no way to know when the cheap model is in doubt. A finding carries no
measure of confidence, and a defect the cheap model never saw appears nowhere in its answer.

### 3.2 Why this decision matters now

This work was scheduled for an earlier sprint and waited while the routing measurement closed. The
comparison of the review approaches depends on its result.

The flow is built and measured, and it sits in the repository behind an environment variable. Without
a written decision, whoever comes later finds a finished mechanism switched off, with no way to tell
whether it was left that way by oversight or by choice.

### 3.3 Constraints

- **Time:** built and measured between 2026-11-10 and 2026-11-14.
- **Cost:** every paid measurement needs approval first. This one cost about US$ 0.39, including a run
  that turned out invalid.
- **Architecture:** ADR-002 keeps the agent a workflow, with its steps fixed in code. So the advisor
  gets a single turn, and a rule decides whether to escalate, not a model. ADR-015 fixes the five
  floors. ADR-008, ADR-016 and ADR-017 already left off patterns that did not pass the ruler.
- **Comparison:** the measurement reuses the cheap and strong passes recorded on 2026-11-07, with skills
  off and prompt v3. Only the advisor pass is new, so the ruler compares reviews made on different
  days.
- **Performance:** not critical; this is a proof of concept.

---

## Block 2: Alternatives considered

### Alternative A: A confidence trigger

**Description:** the cheap model marks on each finding how sure it is, and the strong one is called
when any finding comes with low confidence. This is the literal reading of the requirement.

**Pros:**
- It follows what the requirement asked, without changing the criterion.
- The doubt comes from the model itself, finding by finding, and the strong model could look only at
  the doubtful findings.

**Cons:**
- It requires changing the finding's format and the prompt. Prompt v3 is the one every measurement
  used, and changing it breaks the comparison with the recorded passes.
- Confidence is tied to a finding. A defect the cheap model never saw has nowhere to mark doubt, and
  that is exactly the case that matters most.
- It cannot be tested for free. The recorded reviews carry no confidence, so only a paid pass would
  tell whether the marking works.

**Estimated cost:** the change to the finding's format and the prompt, plus a new paid pass of the cheap
model on top of the advisor pass.

---

### Alternative B: Alarm B, on no findings or mostly Convention

**Description:** the strong model is called when the cheap review has no finding at all, or when 80%
or more of its findings are Convention. The idea is that a cheap model talking only about style did not
look at the logic.

**Pros:**
- It fires less, so it costs less. In production it would fire on 58% of the reviews, against 75% for
  alarm A.
- It fires less for nothing: on 20% of the reviews that were already right, against 44% for A.
- It reads only the answer the cheap model already returned, with no extra call.

**Cons:**
- It depends on the convention skill. With skills off, it fired on only 10 of the 52 reviews that
  missed the defect, 19%. It measures the skill's noise, and not whether the review was weak.
- In production it lets more missed defects through: it fired on 84% of them, against 96% for A.

**Estimated cost:** the same code as alarm A, with a different rule. The check of both alarms was free,
over the recorded reviews.

---

### Alternative C: Alarm A, measured against the ruler (chosen)

**Description:** the strong model is called when the cheap review has no Bug and no Security finding.
It receives the diff and the cheap model's findings and returns only hints: file, snippet and what to
check. The cheap model reviews again with the hints, and that second review is the final answer.
Switching it on in production depended on a ruler written before the measurement: pass all five floors
and cost less than the strong model everywhere.

**Pros:**
- It reads only the answer the cheap model already returned, so deciding to escalate costs nothing.
- It catches almost every missed defect: in production, it fired on 96% of the reviews that missed
  the defect.
- It does not depend on the convention skill, so it works with skills on or off.
- When the strong model answers, it helps. Merging the two reviews, with a cap on the strong model's
  reasoning, detection reached 62.9%, the first column to pass that floor on the 25 cases, without
  losing any of the cheap model's findings.

**Cons:**
- It fires a lot. In production it would call the strong model on 75% of the reviews, and on 44% of
  those that were already right.
- It does not reach the traps. On a trap, the cheap model usually reports something and takes the bait,
  so the alarm stays quiet and the strong model is never called. Trap resistance stayed between 35% and
  45%, below the 50% floor.
- DeepSeek sometimes reasons until it spends the whole limit and returns nothing. Without a cap this
  happened on 29 of the 63 calls. With a cap of 12,000 tokens for reasoning, on 6.
- It cost more than the strong model everywhere, even in its best case: US$ 0.2634 against
  US$ 0.2427.

**Estimated cost:** the code is built and tested. Switched on, a review would cost about 3 times the
cheap model alone, US$ 0.2634 against US$ 0.0870 over the 25 cases.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative C, alarm A, measured against the ruler.

### Rationale

The deciding factor in choosing the alarm was that a defect the cheap model never saw must call the
strong one. Alarm A is the only one of the three that catches that case, because it fires exactly when
the review reports nothing serious. Confidence has nowhere to mark what was not seen, and alarm B
depends on a skill that may be off.

Whether to switch it on in production was decided by the ruler, written on 2026-11-10, before any paid
call. The advisor did not pass: no column passed the five floors, and the flow cost more than the
strong model everywhere. So it stays off behind `REVIEW_PATTERN=advisor`, like Voting, Sectioning and
routing.

Having the ruler written in advance is what made that part easy. When detection passed its floor for
the first time, there was a pull to switch it on. But the ruler already said that one good floor does
not make up for the other four.

### Accepted trade-offs

**I left on the table the first column that passed the detection floor.** Merging the two reviews, with
the reasoning cap, the advisor found 13 defects the cheap model had missed and lost none. No other
approach measured in this project reached 60%. Production stays with the cheap model alone, at 50.5%.

**The cost gap that failed the advisor is small.** The flow cost US$ 0.2634 against US$ 0.2427 for the
strong model. The 6 calls where DeepSeek did not answer cost US$ 0.0314, and without them the flow
would come in under the strong model. Even so, it would not pass the five floors.

**The reasoning cap came after seeing the first measurement.** The ruler did not change, but the strong
model's configuration changed after the result. The capped column has that advantage, and it is stated
here.

**The measurement mixes days.** The cheap and strong reviews are from 2026-11-07, and the advisor's are
from 2026-11-11 to 2026-11-14. Day-to-day variation is left uncontrolled.

### What was NOT considered, and why

**A trigger on subtle code.** The requirement offered it, but there is no measure of "subtle" in the
code, and measuring it with a model is the extra call the savings were meant to avoid.

**The strong model returning findings directly.** It goes against the acceptance criterion, because
the strong model would produce the final answer.

**Flagging the review for a person.** It improves no finding, so it leaves no quality gain to measure.

**An agentic loop**, where the cheap model asks for more hints until it is satisfied. ADR-002 keeps the
agent a workflow, and a loop takes the decision to stop out of the code. It stays as the next
experiment, now that the single turn showed a gain in detection.

**Turning off DeepSeek's reasoning.** It would solve the empty answers, but the strong model would
become a different model from the one the "strong everywhere" column measured.

---

## Block 4: Consequences

### Expected positive consequences

- The flow is built and tested. Switching it on is an environment variable, not a project.
- The `advisor-run` command builds the advisor pass on top of the recorded reviews, paying only for the
  hints and the second review. Testing another strong model costs a partial pass.
- An empty or cut-off answer from the strong model is recorded as a failure, with its real cost,
  instead of silently becoming "no hints".
- The measurement showed that telling the cheap model where to look works: 13 more defects found,
  with none lost. That figure goes into the comparison of the review approaches.

### Known negative consequences

- Production stays with the cheap model alone, with detection at 50.5% and noise at 3.65 per round.
- More unused code sits in the repository: the alarm, the strong model's call, the second review, the
  two commands and 51 tests that run in CI.
- The reasoning cap was tuned for one model and one provider, DeepSeek through OpenRouter. Changing the
  strong model may need another tuning.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Someone switches the advisor on without reading this record, because the code is ready | Medium | Medium | This record. There is no lock in the code, and that is accepted |
| The strong model reasons until the limit and does not answer | **Confirmed** | Medium | A 12,000-token reasoning cap, and the failure is recorded with its cost, keeping the first review. It still happened on 6 of 63 |
| The second review loses what the first one found | **Confirmed** | Medium | Using only the second review, 7 findings were lost. Merging the two reviews, none |
| The alarm was chosen on the same rounds it was measured on | **Confirmed** | Medium | Both alarms were defined before looking at the outcome. Only a measurement on new diffs confirms the alarm |
| An instruction planted in the diff reaches the second review through the hints | Low | High | The hints have a single free-text field, "what to check". It is the same class of risk ADR-013 refused, and it is recorded as a limit of the pattern |

### When to revisit this decision

- [ ] When the strong model's calls on the 63 rounds cost under US$ 0.0903 together, whether through
  another model or a price change. That is the amount that puts the flow under the strong model
  everywhere, and `advisor-report` redoes the arithmetic at no cost.
- [ ] When there is an alarm that also fires on the traps, where the cheap model accuses correct code.
  That is the floor the advisor does not reach.
- [ ] When the agentic loop is measured. If it passes the ruler, it supersedes this decision.

---

## Block 5: Post-decision validation

### Did the decision prove correct?

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, I will write a later ADR to reverse it

_Pending._ The decision is from today, and production runs exactly as before, so there is nothing to
validate yet.

**What validates it:** the comparison of the review approaches. If another approach passes the five
floors at a lower cost, leaving the advisor off was right. If none passes, the capped advisor is still
the only column that passed on detection, and it comes back to the table.

### What I learned from this decision

**Initial reading, 2026-11-14.** An empty answer is not a "no".

The first paid pass showed 46 calls where the strong model "gave no hints". In 45 of them it had been
cut off at the token limit while still reasoning, and the code read the cut-off answer as an answer
with no hints. Had I read the report straight, the conclusion would have been that the strong model
does not help, and it would have been wrong.

The signal was in the numbers: 45 calls at exactly 4,000 tokens. I now check the zeros before reading
the result.

### What I would do differently in hindsight

_Pending._

# ADR-015: A composite metric and an approval criterion, replacing the golden set's boolean verdict

**Date:** 2026-09-04

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

The golden set's ruler returns yes or no per round. It looks at whether the planted bug was found and ignores everything else the model said.

As a result, two different rounds end up with the same score. In the report of 2026-08-16, the `sql-injection` case had one round with a single finding and another with two findings for the same problem, the same line of SQL split in two. Both count as a clean hit.

What gets left out is measured: 75 rounds produced 91 findings, and the ruler does not look at 28 of them.

The same happens on the traps. `collection-spread` scores 5/5 and still spoke twice about correct code.

In the end, a model that finds the bug and invents three more scores the same as one that finds only the bug.

### 3.2 Why this decision matters now

US-013 puts six models through the same golden set, and it needs the result to tell them apart.

Except that the same model, measured three times with nothing changed, gave 68.3%, 73.3% and 63.3% detection. Different models landed between 66.7% and 71.7%. A model's variation against itself is larger than the distance between models.

While that remains true, adding models to the matrix adds numbers and not an answer. The composite metric comes first because it measures axes the current ruler does not look at, and that is where a difference can still show up.

The approval criterion has to be written before the matrix runs. Written afterwards, it would mean choosing the cut-off that gives the conclusion I already wanted.

### 3.3 Constraints

- **The ruler cannot use an LLM.** It runs on every evaluation. A model inside it would charge per finding and give a different result on every execution.
- **Detection and traps do not become a single score.** They are 12 cases on one side and 3 on the other, measuring different things.
- **A new axis requires new data in `cases.json`,** written by hand, case by case.
- **This is a POC.** Performance is not a constraint.

---

## Block 2: Alternatives considered

### Alternative A: A list of acceptable findings per case, chosen

**Description:** Each case in `cases.json` gains, besides the planted bug, a list of what else is legitimate to point out in that diff. A finding outside the list is a false positive.

**Pros:**
- Deterministic and free, with no model call at all
- It gives a real ground truth, case by case
- The list grows with what the measurement shows, it does not have to be born complete

**Cons:**
- It requires re-reading the 15 diffs by hand
- Whatever the author fails to anticipate becomes the model's error, which punishes precisely the more attentive model

**Estimated cost:** manual re-reading of the cases. Zero API.

---

### Alternative B: Count only duplication and fragmentation

**Description:** A false positive becomes only a finding that repeats a problem already counted. An extra opinion about something else in the diff does not count against.

**Pros:**
- No new ground truth needed, nothing to write in `cases.json`
- It attacks directly the vice the sprint names, which is the same problem split across several findings
- It does not punish a model for seeing something real that the author did not anticipate

**Cons:**
- It does not catch pure invention: a made-up finding, about nothing at all, passes freely

**Estimated cost:** implementation only. Zero API and zero manual work.

---

### Alternative C: An LLM judge classifies every extra finding

**Description:** For each finding that is not the planted bug, a model decides whether it was legitimate to say or not.

**Pros:**
- It covers everything, including pure invention, with no hand-written list needed
- It does not punish the model for seeing something the author did not anticipate, because it does not depend on what the author anticipated

**Cons:**
- It charges one call per finding, and the ruler runs on every evaluation
- It returns to the deterministic ruler exactly the instability this US exists to reduce: the same finding could receive different classifications on different executions

**Estimated cost:** one model call per finding, forever.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative A, a list of acceptable findings per case, with alternative B's duplication rule folded in.

### Rationale

The deciding factor is a criterion I adopted for this project: whatever ordinary code can solve, ordinary code solves. A model only comes in where code cannot reach.

That rules out C, even though it is the only one that solves the whole problem. It would charge one call per finding, on every evaluation: the more the model talked, the more expensive it would be to measure that it talks too much.

B on its own only sees repetition, so a finding invented from nothing would pass clean.

A had a defect the measurement showed before I implemented it. On `null-dereference` the model gave two findings across the five rounds, and the second one was correct and outside my ground truth. A list that has to anticipate everything punishes the more attentive model, which is the opposite of what I want to measure.

The way out was to split the extra findings into three groups instead of two. A duplicate counts against. An anticipated one does not count. An unanticipated one does not count yet: it goes into the report, I read it, and I promote it to the list if it recurs.

On the traps, every finding outside the list counts against, because there the diff is correct by construction.

### The approval criterion

The second half of the decision, and it answers two different questions.

**Per case.** A round is clean when it finds the planted bug, does not repeat a finding and gets the severity right. A case is approved with 4 clean rounds out of 5. I do not require 5 of 5, because then the instrument's variation decides on its own, and I do not accept 3 of 5, because with five rounds that is nearly a coin toss.

**Per model.** Five gates, all of which must pass, and none of which is averaged with another:

| Gate | Floor |
|---|---|
| Detection | ≥ 60% |
| Trap resistance | ≥ 50% |
| Precision on the detection cases | ≥ 85% |
| Exact calibration | ≥ 75% of the rounds that found the bug |
| Noise on traps | ≤ 1 finding per round |

The floors came from a real Haiku run, from 2026-08-16, scored again with the new metric. They were not invented: precision and calibration had never been measured, and a number guessed there would either approve everyone or fail everyone.

Haiku passes all five by construction, since the floors came from it. It enters the matrix as a reference row, not as an approved model.

### Accepted trade-offs

**Pure invention still passes.** On a detection case, a finding that is not the bug and is not on the list does not count against. That is the price of not punishing the model for seeing what I did not anticipate, and it goes away as the list matures.

**The rule differs between the two kinds of case,** and it looks like an inconsistency to anyone reading the table. It is deliberate: one diff has a planted bug and may have other legitimate things to say about it, the other is correct all the way through.

**The floors have less headroom than the instrument's variation.** The headroom on detection is 6.7 points, and the same model has already varied 10 points against itself. Haiku itself, which served as the reference, can fail its own gate on a bad pass. I record this instead of hiding it: the criterion is honest about what it measures, and the instrument is still noisy.

**The judgement rests on a single pass,** decided on cost. The alternative was to judge by the worst of three, which would make variation cost the model something. With one pass, the verdict for a model on the borderline changes with the day. It serves to separate extremes, not to read small differences between neighbours in the table.

### What was NOT considered, and why

**Summing the axes into a single score.** It was never evaluated at all. Detection and resistance have different denominators, and a single score would hide exactly what the metric exists to show: a model with high detection and low precision would come out average.

---

## Block 4: Consequences

### Expected positive consequences

- **Runs that used to be identical start to separate.** Six old golden set v1 runs published a perfect score, 15/15. Under the new metric they range from 76.2% to 94.4% precision and from 8 to 14 clean rounds out of 15. Two of them are the same model two days apart.
- **Every rate in the report is traceable.** Each finding receives a verdict, so it is possible to point at which findings produced each percentage.
- **The past can be rescored for free,** because the archived reports keep every finding with its severity and text.
- **The criterion changes verdicts for real.** `collection-spread` had 5/5 and drops to 3 clean rounds out of 5, because it spoke twice about correct code.

### Known negative consequences

- **`cases.json` now requires permanent manual work:** the expected severity on each case, and the list of acceptable findings growing with every measurement.
- **Someone has to read the unanticipated section** after each run and decide what to promote. If nobody reads it, the list never matures and precision stays incomplete forever.
- **The floors depend on a reference run.** If it is rescored or replaced, all five numbers move with it.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The instrument's variation decides the verdict for a model sitting near the floor | **Confirmed** | High | Read any difference smaller than 10 points as "cannot be said from one pass". The matrix serves to separate extremes |
| Nobody reads the unanticipated section, the list never grows and precision stays incomplete | Medium | Medium | The section appears in every run's report, with the finding's text and not only the count |
| The floors turn out too loose or too tight, having come from a single model | Medium | Medium | Compare against the result of the first matrix |
| A missing expected severity in `cases.json` silently becomes `Info` and produces a plausible number | Low | High | Loading refuses the case and names it, instead of assuming zero |

### When to revisit this decision

- [ ] When the US-013 matrix runs: if all six models pass, or all six fail, the floors separate nothing and have to be redone
- [ ] When any case accumulates three entries in its acceptable list, reassess whether the "unanticipated" bucket should start counting against
- [ ] When any model scores above zero on trap precision, the noise gate can go back to being a rate instead of a count

---

## Block 5: Post-decision validation

_To be filled in after use in production._

### Did the decision prove correct?

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, I will write a later ADR to reverse it

### What I learned from this decision

_Pending._

### What I would do differently in hindsight

_Pending._

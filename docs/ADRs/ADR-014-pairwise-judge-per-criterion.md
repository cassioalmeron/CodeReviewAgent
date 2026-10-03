# ADR-014: Design the pairwise judge with a verdict per criterion and a legitimate tie, instead of a single verdict per pair

**Date:** 2026-08-10

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

The statement of US-011 fixes the four practices of the new judge: pairwise, chain-of-thought, randomised order and N executions. What it does not fix is the shape of the verdict.

Comparing two reviews can mean different things. The judge can return a single winner per pair, or a winner on each criterion of the rubric. It can be forced to pick a side, or it can be allowed to declare a tie. These choices are not implementation details: they define the response schema, the rule for aggregating the N executions, and what the report is able to claim at the end.

The old ruler had already shown the price of getting this wrong. On 2026-06-29 it judged three prompt versions with an absolute score and returned all twenty scores between 4.27 and 4.73, two tenths separating the best from the worst. A saturated instrument produces a number with the appearance of a result, and that number became, for almost two months, the belief that `v3` was the best version.

### 3.2 Why this decision matters now

The first application of the new judge was already scheduled: comparing prompt `v3` with `v5`, two versions written in June and never validated. The shape of the verdict had to be decided before the measurement, because it defines the schema, and a schema is not swapped after the calls have been paid for.

The golden set had just narrowed the window. On 2026-08-18 it measured both versions and returned a tie on everything that can be counted: 16/24 detection each, 4/6 on traps, 35 findings on each side, summaries of 150 and 152 characters. If any difference existed between the two prompts, it was not in the quantity, it was in the way they wrote.

That settled the question of the verdict. A single winner per pair would answer "which is better" without saying where, and the "where" was the only thing that could still show up.

### 3.3 Constraints

- **Time:** Sprint 6 closes on 2026-08-23, with its review at Meeting 04 on 2026-08-24. The measurement had to fit before that.
- **Cost:** a monthly API cap, and pairwise multiplies the calls. Thirty pairs times three executions came to 90 calls and US$ 1.20. During the run itself the cap was reached, and access was blocked.
- **A run has to survive interruption.** The first attempt died on call 72 of 90, out of quota, and recorded nothing: the report was only saved after all three loops. Seventy-two paid judgements lost, around US$ 0.90. From then on, any design that accumulates results in memory is out.
- **A judge stronger than the executor**, a constraint from the statement, to avoid self-preference bias. The executor ran on `claude-haiku-4-5` and the judge on `claude-sonnet-4-6`.
- **Two judging flows in the system**, answering different questions. The `judge` command evaluates experiment reviews, produced on purpose in two versions. The `assess` path evaluates a review of a real repository, where no pair exists.
- **Structure beats instruction.** ADR-013 recorded the measurement in which two versions of a skill explicitly forbidding a phrase did not move the model, while one concrete example moved it from 0/3 to 5/5. That constrains how to ask for reasoning before the verdict: through the order of the schema, not through the text of the rubric.

---

## Block 2: Alternatives considered

### Alternative A: A single verdict per pair, with forced choice

**Description:** the judge returns one winner per pair, `A` or `B`, with no tie. It is the most common shape in pairwise benchmarks.

**Pros:**
- Maximises signal per comparison: no paid call comes back without information.
- Minimal schema, simple report, trivial aggregation.
- Fewer output tokens, therefore cheaper per pair.

**Cons:**
- Converts noise into apparent preference. With two equivalent versions, the model is forced to invent a difference, and the result looks exactly like a real one.
- It says which won and not where. Since the golden set had already shown a tie on quantity, the "where" was the only information still available.
- A majority vote over three forced executions hides the judge's disagreement with itself.

**Estimated cost:** the cheaper of the two, around 20% fewer output tokens per pair.

---

### Alternative B: A verdict per criterion, with a tie as a legitimate verdict, chosen

**Description:** the judge returns a verdict on each of the rubric's five criteria, plus a holistic `overall`, and each one can be `A`, `B` or `tie`. The `overall` is not the sum of the five. The rubric declares the tie a valid answer, with a positive condition: choose a tie when the difference would not change what the reviewer does with the output.

**Pros:**
- It says where the difference is, which was the only information still available after the golden set.
- A legitimate tie prevents fabricating a difference between equivalent versions.
- It exposes the judge's own inconsistency: if it gives all five criteria to one side and the `overall` to the other, that is information about the judge.
- Five axes give five chances for the difference to appear, instead of one.

**Cons:**
- More output tokens per call, therefore more expensive.
- Risk of a tie on everything, which would return a measurement with no conclusion.
- Criteria can overlap and punish the same defect twice, in particular `signal_to_noise` and `conciseness`.
- Aggregating six categorical verdicts needs an explicit rule, and a majority vote may have no majority.

**Estimated cost:** 90 calls, US$ 1.1969, 18 minutes.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, a verdict per criterion with a tie as a legitimate verdict

### Rationale

The deciding factor was what the golden set had just shown. On 2026-08-18 it measured `v3` and `v5` and returned a tie on detection, on traps, on the number of findings and on summary length. One question was left: **where**, if anywhere, the two differ.

A single verdict per pair does not answer that question by construction. It would return "`v5` won" and the next question, inevitably, would be "won at what", with no answer in the data.

The legitimate tie came from the same reading, by another route. With two versions measurably equivalent on everything that can be counted, forcing the judge to pick a side would produce a preference where there is no difference, and the result would be indistinguishable from a real one. It is the same failure the absolute score committed, by another mechanism: generating a number where there is no signal.

The chosen alternative's drawbacks were accepted because they are visible when they happen. A tie on everything shows up in the report. Overlap between criteria was handled in the wording of the rubric, separating noise in quantity from excess of text. A vote with no majority is reported as such, instead of being broken.

### Accepted trade-offs

**Higher cost per pair.** Five verdicts and a line of reasoning cost more tokens than a single verdict. The run came to US$ 1.1969 over 90 calls.

**The measurement came back almost entirely inconclusive, and that was foreseeable.** Of the six criteria, one passes the significance test. `calibration` came out 30 to 12 among the non-tied verdicts, p = 0.008. The other five fall within what chance produces at this n, including `overall`, 39 to 28, p = 0.22. Five axes gave five chances, and four came back empty.

**The conciseness criterion, created precisely for this pair, found nothing.** `conciseness` returned 64 ties out of 90, and the score among the non-tied ones leans slightly towards `v3`. It was added to the rubric because the difference between `v3` and `v5` was supposed to be in the way they wrote, and the result is that it is not.

### What was NOT considered, and why

**A global ranking of several versions, Elo or Bradley-Terry style.** It is the natural step after pairwise, and it needs many pairs to stabilise. With 15 cases it does not hold up.

**A human judge as the reference for calibrating the LLM judge.** It would be the gold standard, and the time of the only evaluator available makes it unfeasible in the current cycle.

---

## Block 4: Consequences

### Expected positive consequences

- Prompt comparison no longer depends on a difference of tenths on a saturated scale.
- Position bias becomes a number. The slot draw came out 49 to 41 in the first run, and a large deviation would invalidate translating the verdicts back into versions.
- The judge's instability within a single case becomes visible: in 7 of the 15 cases the six executions formed no majority.
- The raw judgements stay on disk, so the report and the aggregation rule can change without paying again.

### Known negative consequences

- The result is ordinal and local to the pair. It does not become a historical series comparable with older measurements, which the absolute score did produce.
- Every future measurement needs both sides produced beforehand, which doubles the cost of the review stage.
- **Measured afterwards:** five of the six criteria came back inconclusive at 30 pairs. The ruler does discriminate, but it needs more cases than assumed to claim anything outside calibration.
- The front-end chart is still wired to the absolute-score path and does not see pairwise results. A decision recorded on 2026-08-19, not an omission.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| A tie on almost everything, a ruler with no conclusion | **Confirmed** | Medium | Five axes instead of one. `calibration` produced a result, the other four did not |
| Variance within a case larger than between versions | **Confirmed** | High | 7 of 15 cases with no majority. The fix is more cases, not more rounds |
| `signal_to_noise` and `conciseness` punishing the same defect | Low | Medium | Separated by rule in the rubric: one counts a finding that should not exist, the other counts words inside a finding that should |
| Position bias in the judge | Low | High | Drawn per execution. It came out 49/41, within expectation |
| Silent misconfiguration between the two judge modes | **Confirmed** | High | None. `RUBRIC_VERSION` is read by both paths, and a US$ 0.32 run went out against the wrong rubric with no error at all. Still open |

### When to revisit this decision

- [ ] If a measurement comes back tied on **all** six criteria, indicating a blind ruler rather than equivalent versions
- [ ] If the number of versions to compare goes past three, when pure pairwise stops scaling and the case for a ranking opens
- [ ] If the cost of one measurement goes past US$ 2, half the project's typical monthly spend

---

## Block 5: Post-decision validation

_Pending._

### Did the decision prove correct?

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, I will write a later ADR to reverse it

### What I learned from this decision

_Pending._

### What I would do differently in hindsight

_Pending._

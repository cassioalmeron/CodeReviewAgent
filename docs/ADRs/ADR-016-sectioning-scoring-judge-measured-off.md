# ADR-016: Measure Sectioning on the scoring judge and leave it off, keeping the single call as the project's judge

**Date:** 2026-11-06

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

The scoring judge reads one review and returns four criterion scores plus an `overall`, in a single call. It separates nothing: on 2026-06-29 the twenty scores it returned all sat between 4.27 and 4.73, and for almost two months the belief that prompt v3 was the best version rested on that number.

ADR-014 took that judge off the comparison path and blamed the absolute scale, 1 to 5 with no anchor per score. It never separated the scale from the single call, and the reports left room for the second explanation: of the 24 criterion scores in that run, 14 were exactly 5, and in 3 of its 6 reviews all four criteria carried the same score.

One pass of reasoning produces all four numbers, and the four numbers move together. Nobody had tested whether the pass was the cause.

### 3.2 Why this decision matters now

The pairwise judge solved the comparison between two reviews, and it does not solve the case that matters: a real pull request carries one review, and there is no pair.

The agent now comments on pull requests by itself, with nobody filtering first. The absolute judge is the only one that works there, and it is exactly the one that does not measure.

Beyond that, Sectioning was the open half of US-021. Voting was measured and closed on 2026-11-01, with ADR-008. This is the other half.

### 3.3 Constraints

- **Time:** sprint 11 closed on 2026-11-01 and the review meeting passed on 2026-11-02 with this half of US-021 at zero.
- **Cost:** a monthly API cap of US$ 20. Sectioning multiplies calls by four, and on the pairwise judge that would take one comparison close to US$ 10.
- **Architecture:** the ruler is the one from ADR-015, floors written beforehand and never averaged with one another. And the principle from ADR-003 applies here: what the application can compute exactly does not go to the model.
- **Performance:** this is a proof of concept. Four calls in parallel did not make the wall clock worse, 4.2 seconds against 4.5 for the single call.
- **Other:** the reviews were already paid for and stored in the database, so the measurement paid only for the judge calls.

---

## Block 2: Alternatives considered

### Alternative A: Sectioning on the pairwise judge

**Description:** apply Sectioning to the other judge, the one that compares two reviews and declares a winner per criterion. Each of its five criteria would become a call of its own.

**Pros:**
- It is the judge that runs today. The `all` path uses it, so changing it changes what is in use rather than a dormant path.
- It already separates better than the absolute scale, because a tie is a legitimate answer instead of a forced preference. Sectioning would land on a base that works.
- It has five criteria instead of four, so the pattern's effect, if it existed, would show up larger.

**Cons:**
- It costs five times more than the chosen option. One pairwise run already costs US$ 1.84, measured on 2026-09-08 over 15 diffs, 2 pairs each, 3 executions per pair. Sectioning takes a comparison close to US$ 10, half the monthly cap.
- It does not solve the problem that motivated the measurement. A real pull request has one review, and the pairwise judge has nothing to pair there.

**Estimated cost:** close to US$ 10 per comparison, against a monthly cap of US$ 20. Implementation effort equivalent to the chosen option.

---

### Alternative B: A fifth call for `overall`

**Description:** under the sectioned pattern no call sees the other three criteria, so none of them can give a holistic score. This alternative solved that with a fifth call, which would receive the four scores and the review and answer with the `overall`.

**Pros:**
- It keeps `overall` comparable across the two patterns. On both sides it would be the model's reading, and the ruler's repeatability row would work as written.
- `overall` would stay an independent reading instead of becoming a function of the other four scores.
- **The measurement proved this merit right.** The `overall` row had to be dropped from the comparison precisely because the two sides produce different kinds of number. With the fifth call, that row would have counted.

**Cons:**
- One more call, a quarter more expensive on the sectioned side. On the measurement that ran, about US$ 0.21 more.
- It asks the model for a number the application computes exactly, which contradicts ADR-003.

**Estimated cost:** 25% more on the sectioned side. Small implementation, one call and one schema.

---

### Alternative C: Sectioning on the scoring judge, measured and switched off by the result (chosen)

**Description:** build the pattern on the scoring judge, with four calls in parallel, one rubric per criterion, and `overall` computed by the application as the rounded mean. Write the ruler's four floors before any paid run. Measure both sides on the same golden run, the same day and the same model. The pattern stays in the code behind `JUDGE_PATTERN`, off.

**Pros:**
- It is the judge that serves where the agent works: a pull request has one review, and this judge is the only one that reads a review on its own.
- It is the cheap option among the viable ones. It cost US$ 1.13, against almost US$ 10 for Alternative A.
- The reviews were already paid for and stored, so the measurement paid only for judge calls.
- The floors written beforehand allowed the result to come back negative, and it did. A measurement that cannot come back negative is not a measurement.
- Turning it on later is one environment variable, and the history can tell the two patterns apart through the new column.

**Cons:**
- It carries code that does not run: four rubric files, a branch in the judge and a column in the database serving a disabled path.
- It failed. Saturation at 56.1% against a floor of 40%, collapse at 33.3% against a floor of 25%, both worse than the single call on the same day.
- The judge's problem stays open. The pattern is not what solves it.

**Estimated cost:** US$ 1.13 already spent. Recurring cost is zero while it stays off.

---

## Block 3: Decision

### Chosen alternative

**Chose:** Alternative C, Sectioning on the scoring judge, measured and switched off by the result.

### Rationale

There were two decisions at different moments, and they are worth separating.

**Which judge would receive the pattern, decided on cost, before measuring.** The pairwise judge costs US$ 1.84 per run today. Sectioned, one comparison would pass US$ 10, half the monthly cap, and it still would not serve the case I care about, because a pull request has no pair. The scoring judge cost US$ 1.13 and is the only one that reads a review on its own.

**Whether the pattern would stay on, decided by the measurement.** I expected splitting the reasoning pass to spread the scores out. The opposite happened: the scores rose and the four criteria ended up closer together.

That is what settles it. Had the pattern improved saturation and missed the floor by a little, there would be room to change the prompt and try again. It got worse at exactly what it was meant to fix, and a prompt does not fix a design that is wrong.

The explanation is one I can defend from memory: a judge that sees only its own criterion has nothing to compare it against. The single call sees all four at once, and the distance between its best and worst criterion, 0.92 against 0.58 for the sectioned one, is the evidence that it uses that view.

Repeatability passed and that does not rescue the result. A judge that drew its scores at random would also lower saturation and collapse, which is why that row was on the ruler. The sectioned judge kept its stability and got more generous, which is the portrait of a less demanding judge.

Alternative B weighed less because what it protected was one row of the ruler, and the other three already answered the question. Paying for an extra call to save one measurement, against the principle of ADR-003, does not justify itself.

### Accepted trade-offs

**I carry code that does not run.** Four rubric files, a branch in the judge and a column in the database serve a disabled path. From here on every change to the judge has to hold for both patterns.

**One row of the ruler gave no answer, and the alternative I rejected was the one that would have saved it.** The `overall` row came back at 0.000 on both sides and left the comparison. Alternative B, the fifth call, existed to keep that number comparable. I compared at criterion level instead, which works, and its merit is on the record.

**The floors were written against an older measurement, and the new one came back better.** The 40% and the 25% were set looking at the 2026-06-29 run, which gave 58% and 50%. Measured today, the single call gave 47.8% and 26.7%, far closer to the floors. So the floor was easier to pass than it looked when it was written. It did not change the result, because the sectioned side failed anyway, and it would have mattered had it landed at 39%.

**The judge that stays has no ruler either.** The single call saturates: 47.8% of its scores are 5, and in 26.7% of its judgments all four criteria are identical. Choosing it means choosing the less bad of the two, not one that passes.

### What was NOT considered, and why

**Turning Sectioning on despite the failure.** Never on the table. The ruler exists to be obeyed, and its first negative result is not the moment to make an exception.

**Deleting the code after measuring.** Also not weighed. The intent from the start was to build it and keep it, the way ADR-008 did with Voting, because the result may belong to the implementation rather than to the idea.

**Anchored examples per score in the rubric**, which is the fix ADR-014 points at. It was left out of scope on purpose in plan 016, so the pattern's effect would be readable. It is the next candidate, and it was not weighed as an alternative to this decision.

**Changing the judge's model.** Not considered. The repository already records that moving from sonnet-4-6 to sonnet-5 turns extended thinking on silently and triples the output tokens. Changing the model would have changed two things at once.

---

## Block 4: Consequences

### Expected positive consequences

- The scoring judge stays as it was, and nothing changes in the path the agent runs today.
- Switching patterns is one environment variable, with no build and no deploy.
- The history can tell the two patterns apart, through the new column on the evaluation. Any future attempt is comparable to these 90 judgments.
- Repeating the measurement is one invocation. The new command judges a whole golden run, skips what was already judged and resumes where it stopped.
- The hypothesis that the single call caused the saturation died with a number. ADR-014's diagnosis survived a test that could have knocked it down, which is different from never having been tested.
- The cost of sectioning is now measured: 3.23 times, not 4. Each sectioned call sends the same diff and returns a quarter of the output. That holds for any future pattern that fans out on this judge.

### Known negative consequences

- Four rubric files, a branch in the judge and a column in the database serve a path that does not run.
- The judge still cannot separate reviews, and the fix ADR-014 points at, an anchor per score, was not done.
- Rubric v1 now lives in five files: the whole one and the four slices. Changing one criterion means changing two files, and nothing in the code guarantees they keep saying the same thing. The plan proposed a test for this and it was not written.
- The conclusion holds for one model, sonnet-4-6, and one run, number 14. If another model inverts the result, this ADR needs a successor.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The four rubric slices drift from the whole file, and a future measurement compares criteria that are no longer the same | Medium | High | Pending. The plan proposed a test asserting that each criterion's paragraph appears in both files, and it was not written |
| The disabled path rots, because a change to the judge breaks the sectioned branch and nobody notices | Medium | Medium | The Phase 1 tests exercise the sectioned path on every build |
| A run measures the wrong configuration, because the launch profile overrides the shell variable | **Confirmed** | Medium | Fixed before this measurement. The runbook and the command use `--no-launch-profile` |
| Spending more than agreed, because the command judged all 75 assessments of the run instead of the 15 the plan budgets | **Confirmed** | High | Fixed before any paid call. The command gained an option for one assessment per case, and it prints how many it selected before spending |
| The conclusion does not hold for another judge model | Low | High | Repeat both sides on a second model, around US$ 1.10 |

### When to revisit this decision

- [ ] The rubric gains an anchor per score and the single call still saturates above 40%
- [ ] Both sides are measured on a second judge model and the sectioned one passes all four floors
- [ ] The single call's saturation passes 60% in any run, which reopens the search for another design
- [ ] Four judge calls come to cost the same as one, which removes the only floor the sectioned pattern passed comfortably

---

## Block 5: Post-decision validation

_Pending. The decision was taken on 2026-11-06, the same day as the measurement, and nothing has run since. The measurement that produced the decision does not validate it._

### Did the decision prove correct?

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, I will write a later ADR to reverse it

### What I learned from this decision

_Pending._

### What I would do differently in hindsight

_Pending._

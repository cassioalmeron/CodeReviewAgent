# ADR-008: Build the Voting pattern and leave it off by default, keeping the single call as the agent's path

**Date:** 2026-11-01

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

The agent reviews a diff with one call to the model. A single prompt asks it to look for bugs, security flaws, performance problems and convention breaches at the same time, and the answer comes back as a list of findings.

The method document that guides this project gives Voting as the canonical parallelization example, and the use case it names is code review looking for vulnerabilities, with several prompts flagging problems. The pattern has been on the list since the project started and was never tried.

The problem was not that the single call works badly. It was that nothing answered whether the single call is the right choice or merely the first one made. With no comparison, it stayed in place for lack of a measured alternative.

### 3.2 Why this decision matters now

Two things changed.

The golden set now measures. With 15 cases, 5 rounds and the 5 gates from ADR-015, comparing two patterns returns a number instead of an opinion. Before this work there was no way to decide it with data.

And the agent now comments on pull requests by itself, with nobody filtering first. What the pattern produces stopped being my problem and became what other people read.

Beyond that, the ADR was promised months ago and postponed. Number 008 was left empty, and the index of this repository stated for months that it had been skipped and never used.

### 3.3 Constraints

- **Time:** the sprint closes on 2026-11-01 and the review meeting is the next day.
- **Cost:** a monthly API cap of US$ 20. Voting sends the whole diff in all four calls, so input multiplies by 4.
- **Architecture:** the ruler is the one from ADR-015, five gates, none averaged with another. The pattern has to be judged by it, not by detection alone.
- **Performance:** this is a proof of concept, nothing critical. What weighs is the agent commenting on a pull request with no human filter, because noise there costs credibility.
- **Other:** the comparison only holds with both sides on the same configuration, and the run history could not tell one pattern from the other before this work.

---

## Block 2: Alternatives considered

### Alternative A: The single call, without building Voting

**Description:** leave the agent alone. Keep one call per review, record that Voting was left out, and close the work with Sectioning only.

**Pros:**
- Zero cost, both in implementation and in API. No paid run to measure anything.
- The single call is the configuration the run history knows. The 17 earlier runs were all measured against it, so nothing already paid for loses value.
- Frees the whole fortnight for Sectioning, which touches the judge instead.

**Cons:**
- Leaves the question unanswered. The single call would stay in place for lack of a measured alternative, not for having beaten one.
- Voting is the example the method document uses for code review. Not testing it means not testing precisely what the method points at.
- ADR-008 would stay empty for a second milestone in a row, with the public index claiming the number was skipped.

**Estimated cost:** zero.

---

### Alternative B: Voting on by default

**Description:** the agent would review every diff with four parallel reviews, one per subject, and the single call would leave the path. This is what I expected to happen when I started, because I believed four focused reviews would do better than one generic review.

**Pros:**
- It finds more. On `gpt-4o-mini` detection rose from 82% to 92%, and a case the single call never caught, `primary-ctor-capture`, started coming out in all five rounds.
- Wall time does not get worse on a remote model. Four calls took 54 seconds against 55 for one, because they spend most of their time waiting on the network and waiting in parallel does not add up.
- It is the pattern the method document names as canonical, with code review as the use case.

**Cons:**
- It costs 3.5 times more. US$ 0.1056 against US$ 0.0298 on the same run.
- It says far more about the same code. 179 counted findings against 79, and precision falls from 65% to 34%.
- It loses the only trap the single call resisted. It starts flagging correct code in all three traps, in every round, and noise goes from 1.9 to 6.2 per round.
- It fails 4 of the 5 gates, against 2 for the single call. On `qwen2.5-coder` the gap is wider: the single call passes 3 gates and Voting passes none.

**Estimated cost:** implementation already done. What remains is recurring, 3.5 times the bill of every review.

---

### Alternative C: Voting built and left off, with a written trigger (chosen)

**Description:** the pattern sits ready in the code behind the `REVIEW_PATTERN` variable. The agent keeps the single call in CI and on pull requests. The ADR writes down when turning it on is worth it, and the run history gains a column saying which pattern produced each run.

**Pros:**
- Keeps as the agent's path the configuration that passes more gates, without throwing away work already done.
- Turning it on is one environment variable. No build, no deploy, no code change.
- The pattern stays available for refinement. Anyone trying to improve prompt or code starts from a pattern that runs and has a measurement behind it.
- The comparison stays repeatable. With the pattern in the resume key and in the run history, any new pair of runs is comparable to these.

**Cons:**
- Carries code that does not run. Four prompts, a branch in the reviewer and a column in the database serving a disabled path.
- Every change to the reviewer now has to hold for both patterns, so testing and maintenance double at that point.
- The baseline comparator does not know about the pattern yet. A Voting run is compared against the single call's baseline as though nothing had changed.

**Estimated cost:** already spent. US$ 0.1354 across the six runs of the measurement, plus US$ 0.0349 on one run lost to the wrong configuration. Recurring cost is zero while it stays off.

---

## Block 3: Decision

### Chosen alternative

**Chose:** Alternative C, Voting built and left off, with a written trigger.

### Rationale

I expected Alternative B. I started believing that four focused reviews would review better than one generic review. The measurement said otherwise: I spent more and the result got worse. That frustration is why this ADR exists with a number behind it instead of an opinion.

What decided it was the noise. Cost would have been negotiable, because 3.5 times three cents still fits the cap. What does not fit is the agent commenting on a pull request by itself and flagging correct code in all three traps, in every round. High noise teaches people to ignore the comments, and that is hard to undo later.

Better detection does not buy approval, because the ruler from ADR-015 never averages one gate with another. Voting rises on one and falls on four.

The gain is also smaller than detection suggests. Of the 6 points it rose on `gpt-4o-mini`, nearly all of it comes from one case. That is one case more for 3.5 times the price, while losing the only trap the single call resisted.

Alternative A fell before any measurement, because not measuring was exactly the state this work existed to change.

The pattern stays in the code because the result may belong to the implementation rather than to the idea. Prompt and code have room for refinement, and turning it on later is one environment variable.

### Accepted trade-offs

**The configuration that stays also fails.** The single call passes 3 of the 5 gates on `gpt-4o-mini` and fails precision and noise. Choosing it means choosing the less bad of the two, not one that passes. The reviewer's quality problem stays open; Voting is simply not what solves it.

**I give up a bug that Voting caught.** `primary-ctor-capture` came out in all five rounds with Voting and never comes out with the single call. It is a real bug, a primary constructor capture, and the agent will keep walking past it.

**I carry code that does not run.** Four prompts, a branch in the reviewer and a column in the database serving a disabled path. Every change to the reviewer now has to hold for both sides.

**I decided without the approved model.** All three models measured fail the ruler on their own. The pair on DeepSeek V4 Flash, the only approved model in the series, is missing, and it would cost between US$ 0.50 and US$ 0.65. The decision was taken without that data, assuming a better model does not invert a result that showed up the same way on two opposite profiles.

### What was NOT considered, and why

**Deleting Voting after measuring it.** Never on the table. The intent from the start was to build it and keep it, and the measurement did not change that.

**An agreement threshold between the reviews.** Discarded before any code was written. With one review per subject, each one is the only reviewer looking at its subject, so requiring two to agree would throw away nearly every security finding.

**A hybrid, with the bug review split out and everything else in a single call.** Not evaluated at the time of deciding. The idea came up the following day, on noticing that every detection gain came from a Bug-category case. It is in Block 4 as a refinement path, with the condition that reopens this ADR.

**Turning on only some of the reviews**, for example security alone on diffs that touch authentication. Not evaluated. It stays open if refinement happens.

**Changing the number of reviews.** Four was decided before writing code, because with three the performance case would have nobody looking at it. I did not reopen that after the measurement.

---

## Block 4: Consequences

### Expected positive consequences

- The agent keeps the configuration that passes more gates, and nothing changes in CI or on pull requests.
- Switching patterns becomes one environment variable. No build, no deploy.
- The run history now says which pattern produced each run, so any new pair is comparable to these six.
- Voting is ready for refinement. Anyone trying to improve prompt or code starts from a pattern that runs and has a measurement behind it.
- A question open since the start of the project has an answer with a number instead of an opinion.
- Number 008 stops being a hole in the ADR sequence.

### Known negative consequences

- Four prompts, a branch in the reviewer and a column in the database serve a path that does not run.
- The reviewer has two paths. Every change to it now has to be thought through and tested on both.
- The baseline comparator does not know about the pattern. Until it does, a Voting run is compared against the single call's baseline as though nothing had changed, and the result comes out wrong while looking right.
- The primary constructor capture bug keeps getting past the agent.
- The conclusion holds for three models, none of which passes the ruler. If DeepSeek inverts the result, this ADR needs a successor.
- Later measurement, one day after deciding: the bug review alone produced 27 of the 93 findings on correct code, nearly matching the single call's 28 for the whole review. This showed up after the decision, and it weakens any refinement that keeps the bug review split out.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The disabled path rots. A change to the reviewer breaks Voting and nobody notices, because it does not run in CI | Medium | Medium | There are tests for Voting in the test project, and the golden set runs both sides on request |
| A wrong comparison against the baseline, because the comparator does not know which pattern produced the run | High | High | Pending. The same blindness already wrote a false row into the history through the resume store, so the precedent exists |
| The resume store reuses rounds from another pattern and records a wrong result that looks clean | **Confirmed** | High | Fixed. The pattern is now part of the key that identifies a stored review |
| A run measures the wrong configuration, because a `launchSettings.json` profile overrides the shell variable | **Confirmed** | Medium | Fixed. Warning in the runbook, and `--no-launch-profile` in the command |
| The conclusion does not hold for a model that already passes all five gates | Low | High | Measure the pair on DeepSeek V4 Flash, between US$ 0.50 and US$ 0.65 |
| Someone turns Voting on without knowing the result, since it is only an environment variable | Low | Medium | This ADR and the runbook |

### When to revisit this decision

- [ ] The pair on DeepSeek V4 Flash is measured and Voting passes more gates than the single call
- [ ] The hybrid is measured, with the bug review split out and everything else in one call, and trap noise lands at 1 or below per round
- [ ] The single call passes all 5 gates, and noise stops being the deciding factor
- [ ] The agent gains a human filter before commenting on a pull request, for example a draft someone approves
- [ ] Four calls come to cost US$ 0.03 or less per golden set run, which is what one call costs today

---

## Block 5: Post-decision validation

### Did the decision prove correct?

_Initial reading, 2026-11-03. Two days after deciding. The agent never ran any other way, so there is no new usage to validate. What has already shown up is below._

- [ ] Yes, without question
- [ ] Yes, with adjustments
- [ ] No, I will write a later ADR to reverse it

### What I learned from this decision

I bet wrong, and finding that out cost 17 cents. Before the golden set existed, that bet would have become the agent's default configuration, and I would only have found out through the annoyance of reading bad comments on pull requests. The ruler paid for itself on this single decision.

Writing the prediction down before running changed what the result is worth. The two predictions I wrote held, and because of that the conclusion reads as neither luck nor an explanation invented afterwards.

Detection is the metric that misleads. It was the only one that rose, and it was exactly the one that gave me the impression Voting was better. It took two days and a second model before I stopped reading the result as "better at one thing, worse at others" and started reading it as "worse".

### What I would do differently in hindsight

I would have run the local pair first. It cost nothing and reached the same conclusion, so starting there would have given me the answer before spending anything.

I would have put the pattern in the resume key before the first run, instead of after a false row showed up in the database.

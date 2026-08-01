# ADR-003: Compute line numbers in the application instead of having the LLM infer them

**Date:** 2026-06-20

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

In the first version of the prompt, the LLM was responsible for inferring the line numbers of code review findings. The results were imprecise: the LLM hallucinated line numbers, producing findings that pointed at the wrong place in the code. On top of that, asking the LLM to infer line numbers consumed more output tokens, raising the cost per run without adding any quality to the result.

### 3.2 Why this decision matters now

Findings with wrong line numbers undermine the usefulness of the agent. A comment pointing at the wrong line in a PR is worse than no comment at all. Fixing this before moving on to evaluation (week 3) guarantees the golden set is built on trustworthy results.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, 4 weeks until Milestone 1 (2026-07-10)
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Keep LLM inference and improve the prompt

**Description:** Keep asking the LLM to infer finding line numbers, investing in prompt iterations to try to raise accuracy.

**Pros:**
- No change to the application architecture

**Cons:**
- Line inference is inherently non-deterministic. Improving the prompt reduces hallucinations but does not eliminate them
- Keeps output token cost high for something that can be computed with absolute precision

**Estimated cost:** zero in infrastructure, but a continuous cost in tokens and in the reliability of the findings

---

### Alternative B: Compute line numbers in the application

**Description:** Extract line numbers directly from diff parsing, which is deterministic, and pass them to the LLM already computed in the prompt. The LLM focuses only on identifying the problem and suggesting the improvement.

**Pros:**
- Absolute precision in line numbers, with no hallucination possible
- Reduces output tokens, since the LLM no longer has to return the line
- Lower cost per run

**Cons:**
- Requires additional application logic to extract lines from the diff

**Estimated cost:** a few hours of implementation, zero additional API cost

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, compute line numbers in the application

### Rationale

What is deterministic should be computed by the application. A line number is information extractable directly from the diff with absolute precision, so delegating it to the LLM is asking it to guess something the application already knows. Beyond eliminating hallucinations, the change reduced output token consumption and the cost per run.

### Accepted trade-offs

None identified for this context.

### What was NOT considered, and why

External diff parsing tools, unnecessary because parsing was already implemented in the application as part of an earlier story.

---

## Block 4: Consequences

### Expected positive consequences

- Precise, trustworthy line numbers on every finding
- Lower cost per run, from fewer output tokens
- The LLM focused exclusively on its actual job: identifying the problem and suggesting the improvement
- A solid base for the week 3 golden set, since findings become deterministically comparable

### Known negative consequences

- Additional application logic to extract lines from the diff, accepted as a natural responsibility of the parsing layer

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Edge cases in the diff format breaking the computation | Low | Medium | Cover known diff scenarios with unit tests |

### When to revisit this decision

- [ ] If the diff format changes significantly, for example support for version control systems other than Git

---

## Block 5: Post-decision validation

_Filled in on 2026-07-10, after Milestone 1 was delivered._

### Did the decision prove correct?

- [x] Yes, without reservation
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

Since line computation moved into the application, the problem never came back. The LLM stayed focused on what is actually its job, and the findings became reliable enough to build the golden set on top of. The principle generalized: anything deterministic that the application can compute precisely should not be delegated to the LLM.

### What I would do differently in hindsight

I would have built it this way from the first version of the prompt. It would have saved the time spent debugging line hallucinations.

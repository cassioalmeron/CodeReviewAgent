# ADR-005: Separate report generation from the core code review logic

**Date:** 2026-06-25

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

Report generation was coupled to the core review logic. It was not possible to run a review without generating a report, nor to test the report without running a review, which meant paying unnecessary API cost just to validate output formatting. Separating the responsibilities made the two operations independent: the review can run on its own, and the report can be unit tested without calling any LLM. The more flexible design also allows new report types to be generated from data the review core already produced, without re-running the agent.

### 3.2 Why this decision matters now

With the week 3 evaluation pipeline increasing how often the agent runs, the cost of calling the LLM just to test report output would become significant. Separating now guarantees report tests are fast, free and independent, with no risk of burning the monthly budget on unnecessary runs.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, 4 weeks until Milestone 1 (2026-07-10)
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Keep it coupled

**Description:** Review and report generation as a single operation, with no separation of responsibilities.

**Pros:**
- Less code up front

**Cons:**
- Impossible to test the report without running a review, paying API cost on every test
- Impossible to run a review without generating a report
- Adding new output formats requires changing the agent core

**Estimated cost:** zero to implement, continuous token cost to test output

---

### Alternative B: Separate the review core from the report generator

**Description:** The review core is responsible only for calling the LLM and returning structured findings. The report generator is a separate layer that consumes that data and formats the output: a PR comment, a file, the console, or any future format.

**Pros:**
- Review core testable in isolation
- Report unit testable with no API cost
- New report formats without touching the core
- A more flexible design for future evolution

**Cons:**
- More code up front than Alternative A

**Estimated cost:** a few hours of refactoring, zero additional API cost

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, separate the review core from the report generator

### Rationale

Separation of responsibilities is a fundamental design principle: each component does one thing and does it well. The core produces findings, the report consumes them. That separation removes the needless token cost of testing formatting and opens the design to new report types with no risk of regressing agent behavior.

### Accepted trade-offs

None identified for this context.

### What was NOT considered, and why

No other design alternative was evaluated. Separation of responsibilities was the obvious direction as soon as the coupling was identified as a problem.

---

## Block 4: Consequences

### Expected positive consequences

- Unit tests for the report with no API cost
- Review core runnable in isolation
- New output formats without changing the core
- Higher test coverage at lower cost

### Known negative consequences

- More files and classes than the coupled solution, accepted as the cost of separating responsibilities

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The interface between core and report needing changes as findings evolve | Low | Low | The findings schema is already a typed C# record, so changes are caught at compile time |

### When to revisit this decision

- [ ] If the findings schema changes in a way incompatible with the existing report generators

---

## Block 5: Post-decision validation

_Filled in on 2026-07-10, after Milestone 1 was delivered._

### Did the decision prove correct?

- [x] Yes, without reservation
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

The separation made tests faster and cheaper immediately, with the report unit tested at no API cost. The flexibility to generate new output formats without touching the core has already been used in practice. The typed record schema was the key: interface changes are caught at compile time.

### What I would do differently in hindsight

I would have separated the responsibilities from the start, instead of going through a mid-project refactor.

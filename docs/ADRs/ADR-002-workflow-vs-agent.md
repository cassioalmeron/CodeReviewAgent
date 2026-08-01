# ADR-002: Use a workflow, not an agent, for the code review agent as the first applied AI architectural pattern

**Date:** 2026-06-12

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

Define the architectural pattern for the first applied AI system: an example simple enough to start internalizing applied AI concepts in practice, and at the same time useful enough to run as a real code review agent at work.

### 3.2 Why this decision matters now

This choice sets the direction for the coming weeks of the mentorship. Picking the wrong pattern now means rework in the middle of execution. It is also a potentially real use case, which reinforces the need for a deliberate, well-founded decision.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, 4 weeks until Milestone 1 (2026-07-10)
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: RAG, going straight to a chatbot with a vector database

**Description:** Build a chatbot as the first system, using RAG with a vector database instead of a code review workflow.

**Pros:**
- Would cover a chatbot project using vector databases, a relevant technology in the AI ecosystem

**Cons:**
- Would exceed the time available in the mentorship
- Would not cover the workflow pattern, which is the learning objective right now

**Estimated cost:** high in time, not viable within the current window

---

### Alternative B: Code review workflow

**Description:** Build a code review agent as a deterministic workflow: a diff goes in, the LLM processes it, structured findings come out.

**Pros:**
- Covers a real use case that can actually be put to work

**Cons:**
- None identified

**Estimated cost:** 4 weeks (Milestone 1 on 2026-07-10)

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, the code review workflow

### Rationale

It is feasible within the estimated four weeks and has real potential to become a use case in daily work. RAG, while relevant, would overrun the available window and divert from the goal of learning the workflow pattern first.

### Accepted trade-offs

Postponing the start of a RAG chatbot, another project with real potential, left for a later stage after Milestone 1.

### What was NOT considered, and why

Continuing or expanding existing personal projects, such as the grocery list with AI voice recognition. Out of the question because the goal of the mentorship is to build something professionally applicable, with a real use case, documentable as portfolio.

---

## Block 4: Consequences

### Expected positive consequences

- Ship a real applied AI use case, usable in production
- Understand and internalize workflows and agents in practice

### Known negative consequences

- Real token spend during development and testing

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Cost exceeding the monthly cap | High | Medium | Use Ollama for local testing, reserving the Anthropic API for real validations |
| Scope creep, trying to go beyond the MVP | Medium | High | Return to the plan and the scope defined in 3.1 |
| Scarce C# documentation for AI | Low | Low | Fall back on Claude Code for examples and support |
| Getting technically stuck on an implementation | Medium | High | Fall back on Claude Code to get unblocked |
| Unexpected work demands eating into the weekly hours | Low | Medium | Reduce the scope of that week's sprint |

### When to revisit this decision

- [ ] If the project finishes ahead of schedule, evaluate expanding the scope or starting a new project
- [ ] Once workflows and applied AI are internalized, the project may stop making sense as a learning exercise and can be dropped in favor of a more advanced challenge

---

## Block 5: Post-decision validation

_Filled in on 2026-07-10, after Milestone 1 was delivered._

### Did the decision prove correct?

- [x] Yes, without reservation
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

The deterministic workflow was the right choice for a first system: full control over every step, precise instrumentation, and no overengineering. The diff to LLM to structured findings flow proved simple to understand, test and evolve. An agent here would have added complexity with no benefit. The project is being carried on independently beyond the mentorship, a sign that the use case is real and relevant.

### What I would do differently in hindsight

Nothing. The workflow was the right choice and the project proved applicable in practice.

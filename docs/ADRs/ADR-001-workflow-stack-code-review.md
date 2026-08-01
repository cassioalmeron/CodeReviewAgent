# ADR-001: Use a workflow built on C#/.NET + Claude for the code review agent, as the first applied AI system in production

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

This ADR is part of an EPC method exercise for developing Applied AI Engineer skills. The need is to put the concepts into practice and break through the first learning barriers. The concrete goal is to build a code review agent that can be used on real pull requests at work.

### 3.2 Why this decision matters now

The focus of the learning should be applied AI, not adapting to a new language. Using Python would add the overhead of a second learning curve, diverting energy from what actually needs to be learned. Deciding the stack now guarantees that all the friction goes where it belongs: the applied AI domain.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, 4 weeks until Milestone 1 (2026-07-10)
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Python

**Description:** Use Python as the primary language for the code review agent.

**Pros:**
- The language where AI is most mature, with a richer ecosystem, more examples and a larger community

**Cons:**
- Adds a second thing to adapt to, pulling focus away from learning applied AI

**Estimated cost:** zero

---

### Alternative B: C#/.NET

**Description:** Use C#/.NET as the primary language for the code review agent.

**Pros:**
- Years of experience and deep familiarity with the language, so zero friction

**Cons:**
- Less mature AI ecosystem compared to Python

**Estimated cost:** zero

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, C#/.NET

### Rationale

Familiarity with C#/.NET removes language friction entirely and lets the focus go where it is actually needed: applied AI. Python would add a second, parallel learning curve, splitting attention between the language and the domain.

### Accepted trade-offs

None identified for this context.

### What was NOT considered, and why

Every other language (Java, Go, Ruby and so on) was out of the question. The choice narrowed to Python vs C#/.NET based on relevance in the AI ecosystem and on existing familiarity.

---

## Block 4: Consequences

### Expected positive consequences

- Understand the first concepts of applied AI in a hands-on way
- Internalize the difference between agents and workflows more deeply
- Build real awareness of LLM cost, latency and observability
- Develop judgment about which LLMs suit which kinds of task
- Ship a working code review agent that can be used in production

### Known negative consequences

- Testing against the Anthropic API has real monetary cost, accepted as part of the learning process.

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Cost exceeding the monthly cap | High | Medium | Use Ollama for local testing, reserving the Anthropic API for real validations |
| Scope creep, trying to go beyond the MVP | Medium | High | Return to the plan and the scope defined in 3.1 |
| Scarce C# documentation for AI | Low | Low | Fall back on Claude Code for examples and support |
| Getting technically stuck on an implementation | Medium | High | Fall back on Claude Code to get unblocked |
| Unexpected work demands eating into the weekly hours | Low | Medium | Reduce the scope of that week's sprint |

### When to revisit this decision

- [ ] Once the basics of applied AI are internalized, evaluate whether exploring Python makes sense to widen reach in the AI ecosystem.

---

## Block 5: Post-decision validation

_Filled in on 2026-07-10, after Milestone 1 was delivered._

### Did the decision prove correct?

- [x] Yes, without reservation
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

Familiarity with C# removed language friction completely, and all the focus went to applied AI, as planned. In the final weeks, with the project moving beyond its original scope, fluency in C# made it possible to explore Semantic Kernel, OpenTelemetry and .NET Aspire without additional friction. The decision paid dividends beyond what was expected.

### What I would do differently in hindsight

Nothing. The decision was right and was confirmed over the four weeks.

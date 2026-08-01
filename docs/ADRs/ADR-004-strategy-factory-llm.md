# ADR-004: Use the Strategy and Factory patterns to isolate LLM call logic

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

The application needed to call different LLM engines (Claude through the Anthropic API, and Ollama locally) without swapping one for the other requiring changes to the review core. Without an abstraction, the LLM call code would be coupled to the core, making it hard to test, to switch models, or to add new providers.

### 3.2 Why this decision matters now

During week 2 the agent needed to alternate between Ollama (structural tests, no cost) and Claude (real validations). Without this separation, every switch would be a code change in the core, risking a break in the review logic on each iteration. Deciding the architecture now avoids technical debt ahead of week 3, when the evaluation pipeline increases how often the agent runs.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, 4 weeks until Milestone 1 (2026-07-10)
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Direct calls in the core, no abstraction

**Description:** Anthropic API and Ollama calls embedded directly in the review logic, with no abstraction layer.

**Pros:**
- Simpler and faster to implement in the short term

**Cons:**
- Core coupled to the LLM provider, so switching engines requires changing the core
- Makes unit testing harder, since the LLM call cannot easily be mocked
- Adding a new provider requires modifying existing code

**Estimated cost:** zero to implement, high to maintain

---

### Alternative B: Strategy and Factory patterns

**Description:** A common interface (`ILlmEngine`) for any LLM engine. Each provider (Claude, Ollama) is a concrete implementation of the interface. The Factory decides which implementation to instantiate based on configuration. The core only knows the interface.

**Pros:**
- Core fully decoupled from the LLM provider
- Swapping or adding an engine (ChatGPT, for instance) without touching the core
- Simple unit tests by mocking the interface
- A familiar pattern for any senior C# developer

**Cons:**
- More code up front than Alternative A

**Estimated cost:** a few hours of implementation, zero additional API cost

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, Strategy and Factory patterns

### Rationale

Strategy plus Factory is the right pattern for this problem: it isolates what changes (the LLM provider) from what does not (the review logic). The Factory centralizes the decision of which engine to use, and the core never needs to know. It is a familiar pattern in C#, which helps future adoption: any developer working on the code will understand the separation of responsibilities without needing to know the formal pattern names.

### Accepted trade-offs

More code up front than a solution without abstraction, accepted as an investment in maintainability and testability.

### What was NOT considered, and why

Abstract methods with inheritance. Composition was preferred over inheritance, being more flexible and testable in modern C#.

---

## Block 4: Consequences

### Expected positive consequences

- Swapping or adding an LLM provider (ChatGPT, for instance) without changing the core
- Simple unit tests by mocking the `ILlmEngine` interface
- Code that is easier to understand and maintain, with a clear separation of responsibilities

### Known negative consequences

- More files and classes than a direct solution, accepted as the cost of flexibility

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Overengineering, if the project never needs a third provider | Low | Low | The pattern already paid for itself by allowing Ollama and Claude to alternate without touching the core |

### When to revisit this decision

- [ ] If the `ILlmEngine` interface needs changes incompatible with the existing implementations

---

## Block 5: Post-decision validation

_Filled in on 2026-07-10, after Milestone 1 was delivered._

### Did the decision prove correct?

- [x] Yes, without reservation
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

The architecture paid dividends quickly and increasingly. Adding Ollama, Claude via CLI and OpenAI, each new provider was simple and direct, without touching the core. Strategy and Factory also created fertile ground for the resilience Decorator (ADR-006), which slotted naturally into the existing architecture.

### What I would do differently in hindsight

Nothing. The decision was right and proved more valuable than expected over the weeks.

# ADR-006: Use the Decorator pattern for HTTP resilience on LLM API calls

**Date:** 2026-07-10

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

HTTP calls to the LLM APIs (Anthropic and Ollama) need resilience: retry on transient failure, a timeout so nothing blocks indefinitely, and a graceful skip on error so the PR never gets stuck. Without that layer, any network instability or API rate limit would break the pipeline silently or hang the run.

### 3.2 Why this decision matters now

Milestone 1 requires the agent to run on real pull requests, a production environment with real instability. Without resilience, the first network failure takes the whole pipeline down. Implementing it before closing Milestone 1 guarantees the "never blocks the PR" criterion is met.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, Milestone 1 deadline 2026-07-10
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Inline resilience, implemented first

**Description:** Retry, timeout and skip logic embedded directly inside each `ILlmService` implementation, such as `AnthropicLlmService` and `OllamaLlmService`.

**Pros:**
- Simpler and more direct to implement initially

**Cons:**
- Duplicated resilience logic in every `ILlmService` implementation
- Adding a new provider means reimplementing resilience from scratch
- Makes resilience hard to test in isolation

**Estimated cost:** zero, but high maintenance cost as new providers are added

---

### Alternative B: Decorator pattern, chosen after evolution

**Description:** A decorator class (`ResilientLlmService`) that wraps any `ILlmService` and transparently adds retry, timeout and graceful skip, without changing the existing implementations. Applied only to HTTP calls (Anthropic and Ollama), not needed for CLI and SDK.

**Pros:**
- Resilience implemented once, reusable by any HTTP provider
- The `ILlmService` implementations stay clean, focused only on the LLM call
- Easy to test resilience in isolation with a mocked `ILlmService`
- New HTTP providers get resilience automatically by passing through the decorator

**Cons:**
- One more class in the project compared to Alternative A

**Estimated cost:** a few hours of refactoring from Alternative A

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, the Decorator pattern

### Rationale

Alternative A was implemented first, being faster to a working result. But when the second provider (Ollama) was added, it became clear the resilience logic would be copied and pasted for every new provider. The problem confirmed itself in practice: when OpenAI support was added, the Decorator made resilience work automatically, with no code duplication. Write it once, apply it to any `ILlmService` through composition, the same logic as the Strategy and Factory in ADR-004: isolate what changes from what does not.

### Accepted trade-offs

One more class in the project, accepted as the cost of reuse and testability.

### What was NOT considered, and why

External resilience libraries such as Polly, unnecessary for the scope of the POC. The retry and timeout logic is simple enough to implement directly.

---

## Block 4: Consequences

### Expected positive consequences

- New HTTP providers get resilience automatically by passing through the decorator
- Resilience logic testable in isolation
- `ILlmService` implementations focused exclusively on the LLM call
- Already validated in practice: OpenAI was added with resilience working at no extra effort

### Known negative consequences

- One more class in the project, accepted as the cost of separating responsibilities

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Retry behavior inappropriate for some errors, for example a 400 that should not be retried | Low | Medium | Filter by exception type and status code inside the decorator |

### When to revisit this decision

- [ ] If the resilience logic grows in complexity, evaluate adopting Polly or similar

---

## Block 5: Post-decision validation

_Filled in on 2026-07-10, after Milestone 1 was delivered._

### Did the decision prove correct?

- [x] Yes, without reservation, validated in practice when OpenAI support was added
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

The evolution from A to B happened naturally, by feeling the code duplication. The Decorator was the obvious consequence of already having Strategy and Factory in place: the architecture asked for the extension.

### What I would do differently in hindsight

Implement the Decorator from the start, without going through Alternative A.

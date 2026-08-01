# ADR-007: Add Claude access through the CLI via a process runner, as an alternative to the metered API

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

The application already used the Anthropic API, with prepaid credits, for LLM calls. The goal was to explore whether a Claude subscription could work as an alternative, reducing or eliminating token cost during development. The community .NET SDK (ClaudeAgentSDK) was not stable, so the alternative path was to reach the Claude CLI directly through a process runner.

### 3.2 Why this decision matters now

With the project moving into continuous use after Milestone 1, token cost starts to grow. Having the subscription as a viable option gives the flexibility to alternate between the API (full control, cost per token) and the CLI (fixed subscription cost) depending on the context.

### 3.3 Constraints

- Time: roughly 10 effective hours per week, Milestone 1 deadline 2026-07-10
- Cost: a tight monthly cap on LLM API spend
- The Anthropic API remains available as the primary option. The CLI is an alternative, not a replacement
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: ClaudeAgentSDK, the community .NET SDK

**Description:** Use `ClaudeAgentSDK`, a third-party (unofficial) library built by the community to integrate Claude into .NET applications.

**Pros:**
- A more fluent interface, better integrated with the .NET ecosystem than shelling out to an external process

**Cons:**
- Unstable library, which hit concrete problems during testing
- No guarantee of maintenance or future compatibility, being unofficial
- Little control over what happens underneath

**Estimated cost:** zero, but high in debugging time and uncertainty

---

### Alternative B: CLI through a process runner, chosen

**Description:** Call the Claude CLI executable directly from the .NET application through `Process.Start`, passing input and capturing output. Implemented as a separate `ILlmService`, preserving the existing architecture.

**Pros:**
- Works around the instability of the community SDK
- Uses the Claude subscription instead of API credits
- Fits the existing Strategy and Factory architecture (ADR-004)

**Cons:**
- Less control over loaded context, tokens consumed and latency
- Higher latency than the direct API, even after context optimization
- Depends on the CLI being installed in the execution environment

**Estimated cost:** a few hours of implementation and optimization

---

### Alternative C: Keep only the Anthropic API

**Description:** Do not explore subscription access. Continue using the prepaid-credit API exclusively.

**Pros:**
- Full control: tokens, latency and context completely visible
- Simpler, with no dependency on an external process

**Cons:**
- Cost per token on every development test and run

**Estimated cost:** continuous API cost on every run

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, the CLI through a process runner

### Rationale

The community SDK (Alternative A) proved unstable in practice, so it is set aside for now but still in the plans once the bug found is fixed. There is an open-source contribution PR planned for the library. The API (Alternative C) remains the primary option for real validations, where control and visibility matter. The CLI through a process runner was added as a viable alternative for subscription use, especially in development runs where token cost would be unnecessary. All three options coexist thanks to the Strategy and Factory architecture from ADR-004.

### Accepted trade-offs

Less control over context and latency through the CLI compared to the direct API, accepted in exchange for no per-token cost on development runs.

### What was NOT considered, and why

No other alternative was evaluated.

---

## Block 4: Consequences

### Expected positive consequences

- Flexibility to alternate between the API (full control) and the CLI (fixed subscription cost) depending on context
- An open path to integrating the community SDK once the bug is resolved
- Architecture ready to absorb any new provider without changing the core (ADR-004)

### Known negative consequences

- Through the CLI: less visibility into tokens, and higher latency than the API
- Dependency on the CLI being installed in the execution environment

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The CLI changing its interface in future versions | Medium | Medium | Isolate the process call inside the `ILlmService` implementation, so the change stays contained |
| The SDK bug not being fixed in the short term | Medium | Low | The CLI already covers the need for subscription use |

### When to revisit this decision

- [ ] Once the ClaudeAgentSDK bug is fixed, evaluate migrating from the CLI to the SDK
- [ ] If Anthropic releases an official .NET SDK

---

## Block 5: Post-decision validation

_Pending. To be filled in once the community SDK is stable, or once an official .NET SDK exists._

### Did the decision prove correct?

- [ ] Yes, without reservation
- [ ] Yes, with adjustments
- [ ] No, I will write a follow-up ADR to reverse it

### What I learned from this decision

_Pending._

### What I would do differently in hindsight

_Pending._

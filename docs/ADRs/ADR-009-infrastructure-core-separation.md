# ADR-009: Move infrastructure classes into their own project, outside the Core

**Date:** 2026-07-22

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

The infrastructure classes (database access, LLM providers, HTTP clients) lived in the same project as the Core, the business logic of the code reviewer. This was known technical debt from the start, but deprioritized behind the Milestone 1 deliverables.

The logical separation between Core and infrastructure was already correct: the classes lived in distinct folders and the coupling did not block tests. The risk was long term. Without a physical boundary between projects, nothing prevented the logical separation from being violated at some future point. On top of that, infrastructure libraries such as EF Core should not be referenced from the Core project at all. Moving to separate projects makes that boundary explicit and structural.

### 3.2 Why this decision matters now

Milestone 1 was delivered and the week had no formal sprint. With room for organic work, it was the right moment to pay off this technical debt with no delivery pressure.

### 3.3 Constraints

- Time: organic week after Milestone 1, no formal deadline
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Keep everything in the same project

**Description:** Logical separation through folders only, without creating a separate project for infrastructure.

**Pros:**
- No overhead from multiple projects in the solution
- No refactoring needed

**Cons:**
- Nothing prevents the logical boundary from being violated in the future
- Infrastructure libraries (EF Core, HttpClient) stay referenced in the same project as the Core

**Estimated cost:** zero now, but growing risk as the project evolves

---

### Alternative B: Move infrastructure into its own project, chosen

**Description:** Move infrastructure classes into a separate project in the solution. The Core then depends only on interfaces, with no direct reference to infrastructure libraries.

**Pros:**
- The compiler becomes the guardian of the separation, instead of it depending on code discipline
- Infrastructure libraries such as EF Core stay structurally outside the Core project
- Makes the code and the architecture more intuitive: it is immediately clear where each responsibility lives

**Cons:**
- Small maintenance overhead from multiple projects in the solution
- One-off refactoring cost to move the classes

**Estimated cost:** a few hours of refactoring

---

### What was NOT considered, and why

Adding another abstraction layer with explicit ports and adapters in a separate project. Unnecessary overhead, given the interfaces already existed in the Core and the logical separation was already correct.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, move infrastructure into its own project

### Rationale

The logical separation was already correct, so the change was low risk and low effort. The compiler now guarantees what previously depended on code discipline. Infrastructure libraries such as EF Core should not be referenced from the Core project, and the physical separation removes that possibility structurally. Technical debt paid at the right moment: after Milestone 1, in an organic week with no delivery pressure.

### Accepted trade-offs

Small overhead from multiple projects in the solution, accepted as the cost of a structural boundary.

---

## Block 4: Consequences

### Expected positive consequences

- An explicit physical boundary: the compiler prevents business logic from referencing infrastructure libraries directly
- Makes the code and the architecture more intuitive: it is immediately clear where each responsibility lives
- Removes the risk of the logical separation being violated later

### Known negative consequences

- Small maintenance overhead from multiple projects in the solution

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The refactor introducing an accidental regression | Low | Medium | Existing unit tests catch contract breaks |

### When to revisit this decision

- [ ] If project granularity grows too far, evaluate consolidating or modularizing differently

---

## Block 5: Post-decision validation

_Pending. To be filled in after production use._

### Did the decision prove correct?

- [ ] Yes, without reservation
- [ ] Yes, with reservations
- [ ] No

### What I learned from this decision

_Pending._

### What I would do differently in hindsight

_Pending._

# ADR-012: Redesign the data schema, introducing Project as the root entity and Finding as its own table

**Date:** 2026-07-25

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

The original schema had three tables: `Diff → Analysis → JudgeEvaluation`. Findings were serialized into a JSON column inside `Analysis`, and there was no notion of a project at all: every diff from every repository landed in a single, undifferentiated store. The golden set was identified by a string prefix in the `Source` field (`"golden:<case>"`), not by an entity of its own.

### 3.2 Why this decision matters now

With no notion of a project, it was impossible to separate statistics per repository or to filter results by origin. Findings stored as JSON were not individually queryable: any analysis meant deserializing the whole blob. Identifying the golden set by a string prefix was fragile and coupled business logic to a persistence detail.

### 3.3 Constraints

- Building the per-project dashboard turned the absence of `Project` into a real blocker. There was no way to filter reviews, assessments or findings by repository. The front end needed a `Project` entity to exist. Without it, the dashboard would have nothing to show.
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Keep the flat schema with prefix filtering

**Description:** Derive the project from the diff content or the `Source` field, without creating a `Project` entity. The golden set would stay identified by a string prefix.

**Pros:**
- No refactoring, zero immediate effort
- No data migration needed

**Cons:**
- Perpetuates the coupling between business logic and a persistence detail
- Findings remain individually unqueryable
- Per-repository statistics require manual parsing on every query

**Estimated cost:** zero now, but growing technical debt as the project evolves

---

### Alternative B: Redesign the schema with Project as root and Finding as its own table, chosen

**Description:** Five tables: `Project → Review → Assessment → Finding`, plus `Evaluation`. `Finding` leaves the JSON blob and becomes a queryable row. The golden set becomes a project of its own, identified by `Folder = "golden"`.

**Pros:**
- Per-repository statistics and filters with no manual parsing
- Findings individually queryable by severity, category and file
- The golden set treated as a first-class project, with no special prefix logic
- Command vocabulary aligned with the schema (`diff→review`, `review→assess`)

**Cons:**
- Large-surface refactor, 23 files affected
- Existing data needed a manual migration
- `Review` as an entity creates ambiguity with the verb "review", mitigated by renaming the commands

**Estimated cost:** several days of refactoring plus a one-off migration script

---

### What was NOT considered, and why

`OwnsMany` in EF for `Finding`. It would generate the table without touching the record, but findings would only be reachable by navigating from `Assessment`, which would defeat the reason for promoting the table in the first place: querying findings on their own.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, a full schema redesign

### Rationale

`Project` solves per-repository traceability. `Finding` in its own table allows filtering by severity, category and file without deserializing JSON. The golden set becomes a project like any other, removing the coupling between business logic and a string prefix. Historical data was migrated through a one-off Python script, with no EF migration and no permanent command in the console.

### Accepted trade-offs

A large-surface refactor and a manual migration of historical data, accepted as the cost of a correct foundation for the dashboard and for structured queries.

---

## Block 4: Consequences

### Expected positive consequences

- Per-repository statistics and filters with no manual parsing
- Findings individually queryable by severity, category and file
- The golden set treated as a first-class project, with no special prefix logic
- Renamed commands (`diff→review`, `review→assess`) make the vocabulary coherent with the schema

### Known negative consequences

- Large-surface refactor, 23 files affected
- Existing data needed a manual migration
- `Review` as an entity creates ambiguity with the verb "review", mitigated by renaming the commands

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The `Review` ambiguity (entity vs verb) confusing future reading of the code | Low | Low | Commands renamed (`assess`) and the vocabulary documented in CLAUDE.md |
| The migration introducing inconsistency in historical data | Low | Medium | Idempotent script with count validation before and after |

### When to revisit this decision

- [ ] If schema granularity needs to grow, for example sub-projects or branches as entities

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

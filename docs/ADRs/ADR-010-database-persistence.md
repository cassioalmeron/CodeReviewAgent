# ADR-010: Persist logs in a database instead of files on the filesystem

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

The code reviewer's execution logs (findings, cost, latency, tokens) were saved as files on the filesystem. That worked for the first weeks, but started to limit how the results could be analyzed and visualized. The implemented solution put the repository behind an interface, with two available implementations, EF Core (database) and filesystem, selectable through an environment variable. The filesystem was not discarded. The database was added as a better alternative for analysis.

### 3.2 Why this decision matters now

Files on the filesystem make queries, filters and historical visualization awkward. To feed the React front end, a structured way to access the data was needed, and the filesystem does not offer that without manual parsing on every query.

### 3.3 Constraints

- The decision to build a React front end for report visualization accelerated the need. Queries and filters over files would be possible, but inefficient. With a database, the backend API serves data in a structured, efficient way with no manual parsing.
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Filesystem, the existing implementation

**Description:** Logs saved as files. Simple, with no extra dependencies.

**Pros:**
- No additional dependencies
- Already working, so zero maintenance effort

**Cons:**
- Queries and filters are possible but inefficient, requiring manual parsing
- Does not scale well for historical visualization through an API

**Estimated cost:** zero, but a growing efficiency cost as log volume increases

---

### Alternative B: Database through EF Core, chosen

**Description:** Repository behind an interface with two implementations, EF Core (database) and filesystem, selectable through an environment variable. For now the database in use is SQLite.

**Pros:**
- Structured queries and filters with no manual parsing
- The backend API serves data efficiently to the front end
- Filesystem kept as an alternative for environments without a configured database, or for local debugging
- The Strategy pattern makes it easy to swap SQLite for another database later

**Cons:**
- A dependency on EF Core and SQLite in the infrastructure layer
- Migrations have to be maintained as the schema evolves

**Estimated cost:** a few hours to implement the interface, the EF Core implementation and the migrations

---

### What was NOT considered, and why

External storage solutions such as cloud storage or S3. Unnecessary overhead for the scope of the project.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, a database through EF Core with the Strategy pattern

### Rationale

Add an EF Core implementation behind the same repository interface that already existed. The Strategy pattern, already adopted in ADR-004 for the LLM providers, applied naturally to the persistence layer: one interface, multiple implementations, selected through an environment variable. The filesystem remains available, useful for environments without a configured database or for local debugging. For now the database in use is SQLite.

### Accepted trade-offs

A dependency on EF Core and SQLite, plus migration overhead, accepted as the cost of query efficiency and future flexibility.

---

## Block 4: Consequences

### Expected positive consequences

- Structured queries and filters with no manual parsing
- The backend API can serve data efficiently to the front end
- The Strategy pattern makes swapping SQLite for another database trivial: just add a new implementation of the interface

### Known negative consequences

- A dependency on EF Core and SQLite in the infrastructure layer
- Migrations have to be maintained as the schema evolves

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Adding a new database whose provider is incompatible with the existing migrations | Low | Medium | Refactor the migrations for the new provider |

### When to revisit this decision

- [ ] If data volume grows beyond what SQLite handles well, evaluate migrating to PostgreSQL

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

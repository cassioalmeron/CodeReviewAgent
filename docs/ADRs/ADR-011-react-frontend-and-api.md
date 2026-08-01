# ADR-011: Build a React front end and a backend API to visualize code review reports

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

Code review reports were generated as files, markdown or JSON, and consumed directly through the filesystem or the CLI. As the number of runs grew, locating a specific run from an earlier period meant navigating files by hand.

### 3.2 Why this decision matters now

With no visualization interface, every look at past results meant opening files manually or writing search scripts. There was no practical way to filter by date, repository or finding severity, nor to compare runs against each other or extract statistics and totals.

### 3.3 Constraints

- Adding the database (ADR-010) made it viable to build an API that serves the data efficiently. With structured persistence solved, a React front end was the natural next step to make the data accessible and navigable.
- Cost: a tight monthly cap on LLM API spend
- Performance: this is a POC, nothing critical
- Other: the MVP must be entirely independent of any third-party approval

---

## Block 2: Alternatives considered

### Alternative A: Keep consuming through the filesystem and CLI

**Description:** No dedicated interface. Reports consumed directly through the filesystem or the CLI.

**Pros:**
- No additional projects to maintain
- Zero implementation effort

**Cons:**
- Files have to be located by hand, with no search or filter mechanism
- Extracting statistics and totals requires scripts or manual analysis
- Finding runs from earlier periods is tedious and error prone

**Estimated cost:** zero now, but a growing time cost as the number of runs increases

---

### Alternative B: React front end plus backend API, chosen

**Description:** A dedicated interface for browsing, filtering and visualizing reports. The backend API serves data from the database (ADR-010), and the React front end consumes the API.

**Pros:**
- Browsing and filtering historical runs with no manual effort
- Statistics and totals extracted directly in the interface
- A more pleasant visual way to consume the results
- Makes it possible to generate reports in an updated format from older reviews

**Cons:**
- Two more projects to maintain in the solution, the API and the front end
- A dependency on the React stack in the front end

**Estimated cost:** a few hours to structure the API and the initial front end

---

### What was NOT considered, and why

External BI solutions such as Metabase or Grafana. Unnecessary configuration overhead for the current volume and scope of the project.

---

## Block 3: Decision

### Chosen alternative

**Chosen:** Alternative B, React front end plus backend API

### Rationale

With the database already solved by ADR-010, the API was the natural step to expose the data in a structured way, and the React front end to make it navigable. The three decisions (ADR-009, ADR-010, ADR-011) form a coherent progression: physical separation of the architecture, then structured persistence, then accessible visualization.

### Accepted trade-offs

Two more projects in the solution and a dependency on the React stack, accepted as the cost of usability and of the ability to analyze history.

---

## Block 4: Consequences

### Expected positive consequences

- Browsing and filtering historical runs with no manual effort
- Statistics and totals extracted directly in the interface
- A more pleasant visual way to consume the results
- Because the data is structured in the database, reports can be generated in an updated format from older reviews, without re-running the review
- The project stops being just a CLI and gains a product layer

### Known negative consequences

- Two more projects to maintain in the solution, the API and the front end
- A dependency on the React stack in the front end

### Identified risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| The front end growing in complexity and pulling focus from the project core | Medium | Medium | Keep the front end as a visualization tool. It is not the main product |

### When to revisit this decision

- [ ] If the front end starts consuming more maintenance time than the value it adds

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

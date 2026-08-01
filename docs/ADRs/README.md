# Architecture Decision Records

Every non-trivial decision in this project is written down before it is implemented, and revisited afterwards against what actually happened. Each record states the problem, the alternatives that were weighed, the one chosen, the trade-offs accepted, and the conditions that would justify reversing it.

The last section of each record is the part that matters most: it is filled in after the decision has been in use, and says whether it held up. Some are still marked pending, because the decision has not run long enough to judge.

The records are in chronological order. Read together, they trace the project from a first choice of language to a redesigned data schema.

| ADR | Decision | Date |
|---|---|---|
| [001](ADR-001-workflow-stack-code-review.md) | Build on C#/.NET plus Claude, as the first applied AI system | 2026-06-12 |
| [002](ADR-002-workflow-vs-agent.md) | Use a workflow, not an agent, as the first architectural pattern | 2026-06-12 |
| [003](ADR-003-line-numbers-in-application.md) | Compute line numbers in the application instead of having the LLM infer them | 2026-06-20 |
| [004](ADR-004-strategy-factory-llm.md) | Isolate LLM call logic behind Strategy and Factory | 2026-06-20 |
| [005](ADR-005-report-generation-separation.md) | Separate report generation from the review core | 2026-06-25 |
| [006](ADR-006-decorator-http-resilience.md) | Add HTTP resilience through a Decorator | 2026-07-10 |
| [007](ADR-007-cli-via-process-runner.md) | Reach Claude through the CLI via a process runner, alongside the API | 2026-07-10 |
| [009](ADR-009-infrastructure-core-separation.md) | Move infrastructure out of the Core, into its own project | 2026-07-22 |
| [010](ADR-010-database-persistence.md) | Persist logs in a database instead of files | 2026-07-22 |
| [011](ADR-011-react-frontend-and-api.md) | Add a React front end and a backend API for visualization | 2026-07-22 |
| [012](ADR-012-project-finding-schema-redesign.md) | Redesign the schema around Project and Finding | 2026-07-25 |

There is no ADR-008. The number was skipped and never used.

## Threads worth following

Three of these records build on each other rather than standing alone.

**Isolating what changes.** ADR-004 put the LLM providers behind an interface. ADR-006 then added resilience as a decorator over that same interface, so a new provider inherits retry and timeout for free. ADR-010 applied the identical shape to persistence: one interface, a database implementation and a filesystem implementation, chosen by environment variable. The same idea, three layers.

**Not asking the model to do arithmetic.** ADR-003 moved line-number computation out of the prompt and into the parser, because line numbers are derivable from the diff with certainty and the model was guessing them wrong. The principle generalized: anything the application can compute exactly should not be delegated to an LLM.

**Earning the right to a dashboard.** ADR-009 separated infrastructure from the Core, ADR-010 made persistence structured, ADR-011 built the API and front end on top, and ADR-012 redesigned the schema once the dashboard revealed that a `Project` entity was missing. Each step was only possible because of the one before it.

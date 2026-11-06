# Architecture Decision Records

Every non-trivial decision in this project is written down before it is implemented, and revisited afterwards against what actually happened. Each record states the problem, the alternatives that were weighed, the one chosen, the trade-offs accepted, and the conditions that would justify reversing it.

The last section of each record is the part that matters most: it is filled in after the decision has been in use, and says whether it held up. Some are still marked pending, because the decision has not run long enough to judge.

The records are in chronological order. Read together, they trace the project from a first choice of language to the criterion by which a model is allowed to pass.

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
| [013](ADR-013-skills-harness-model-activation.md) | Rebuild the skills harness on the Agent Skills specification, with activation decided by the model | 2026-08-05 |
| [014](ADR-014-pairwise-judge-per-criterion.md) | Judge pairwise with a verdict per criterion and a legitimate tie | 2026-08-10 |
| [015](ADR-015-composite-metric-approval-criterion.md) | Replace the golden set's boolean verdict with a composite metric and an approval criterion | 2026-09-04 |
| [008](ADR-008-voting-pattern-off-by-default.md) | Build the Voting pattern and leave it off by default, keeping the single call as the agent's path | 2026-11-01 |
| [016](ADR-016-sectioning-scoring-judge-measured-off.md) | Measure Sectioning on the scoring judge and leave it off, keeping the single call as the project's judge | 2026-11-06 |

ADR-008 comes last despite its number. It was reserved early for the parallelization decision, stayed empty while that decision was postponed, and was written once the golden set could measure the two patterns against each other.

## Threads worth following

Four of these records build on each other rather than standing alone.

**Isolating what changes.** ADR-004 put the LLM providers behind an interface. ADR-006 then added resilience as a decorator over that same interface, so a new provider inherits retry and timeout for free. ADR-010 applied the identical shape to persistence: one interface, a database implementation and a filesystem implementation, chosen by environment variable. The same idea, three layers.

**Not asking the model to do arithmetic.** ADR-003 moved line-number computation out of the prompt and into the parser, because line numbers are derivable from the diff with certainty and the model was guessing them wrong. The principle generalized: anything the application can compute exactly should not be delegated to an LLM.

**Earning the right to a dashboard.** ADR-009 separated infrastructure from the Core, ADR-010 made persistence structured, ADR-011 built the API and front end on top, and ADR-012 redesigned the schema once the dashboard revealed that a `Project` entity was missing. Each step was only possible because of the one before it.

**Learning to measure.** Each of the last three records replaced an instrument that could not tell things apart. ADR-013 rebuilt the skills harness so that running with no skill at all became possible, which is the baseline every later comparison rests on. ADR-014 replaced an absolute score that had returned twenty values within two tenths of each other with a pairwise verdict on each criterion, where a tie is a legitimate answer rather than a forced preference. ADR-015 then replaced the golden set's own yes-or-no with five gates that are never averaged together, written down before the model matrix ran so the cut-off could not be chosen to fit the conclusion. The thread is one lesson learned three times: a measurement that cannot come back negative is not a measurement.

ADR-016 closes that thread by turning it on ADR-014 itself. ADR-014 had blamed an absolute scale for scores that could not tell reviews apart, without separating the scale from the single call that produced them. ADR-016 split the call into one per criterion, wrote four floors first, and the scores came back more saturated instead of less, so the scale keeps the blame. A diagnosis that survives a test that could have knocked it down is worth more than one nobody tried.

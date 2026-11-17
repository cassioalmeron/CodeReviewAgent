# Review approaches compared: single call, Voting, routing and advisor

**Date:** 16/11/2026
**Verdict:** no approach passes the ruler. Production stays on the single call with `gpt-4o-mini`,
the cheapest and fastest option, because nothing measured earns its extra cost.

## How it was measured

Every approach ran on the same 25 cases with the same settings, so the pattern is the only thing
that changes between rows:

- 15 golden cases (12 detection, 3 traps) and 10 large cases (9 detection, 1 trap), 5 rounds each,
  125 reviews per approach.
- Skills off, prompt v3.
- Cheap model `gpt-4o-mini`, strong model `deepseek/deepseek-v4-flash-0731`.
- Quality is the 5 gates of ADR-015, never summed into one score.

Latency is the summed time of the 125 reviews. Routing composes its cost and latency per case from
the cheap and strong passes, picking the pass its rule selects. The advisor has two readings of the
same calls: *replace* keeps only the second review, *merge* also keeps the first review's findings
the second did not repeat.

## Results

| Approach | Detection ≥ 60% | Trap resistance ≥ 50% | Precision ≥ 85% | Exact calibration ≥ 75% | Trap noise ≤ 1 | Gates passed | Cost | Latency |
|---|---|---|---|---|---|---|---|---|
| Single call, cheap | 50.5% | 40% | 76.0% | 69.8% | 3.65 | 0 of 5 | US$ 0.0870 | 12.4 min |
| Single call, strong | 52.4% | **70%** | **96.6%** | 72.7% | **0.90** | 3 of 5 | US$ 0.2427 | 83.2 min |
| Voting, cheap | 51.4% | 30% | 41.3% | 55.6% | 9.05 | 0 of 5 | US$ 0.3439 | 23.9 min |
| Routing (rule v1) | 57.1% | 35% | 79.0% | 73.3% | 2.45 | 0 of 5 | US$ 0.1764 | 54.6 min |
| Advisor, merge | **62.9%** | 35% | 75.3% | 71.2% | 3.90 | 1 of 5 | US$ 0.2634 | 54.3 min |
| Advisor, replace | 57.1% | 40% | 84.2% | **76.7%** | 2.90 | 1 of 5 | US$ 0.2634 | 54.3 min |

Bold marks a gate passed. Per review, the cheap single call costs US$ 0.0007 and takes 6 seconds;
the strong single call costs US$ 0.0019 and takes 40 seconds.

## Reading each approach

**Single call, cheap.** The production path. It fails all 5 gates on this set, mostly on the large
diffs, where it finds 7 of 45 bugs. Every other row is measured against it.

**Single call, strong.** The least bad row: it passes trap resistance, precision and noise, so when
it reports something, it is almost always right. It fails detection by the same margin as the cheap
model and costs 2.8 times as much. It is also the slowest, at 83 minutes for the set.

**Voting.** The worst row. It costs 4 times the single call and loses on 4 of the 5 gates, while
detection barely moves (51.4% against 50.5%). Its 4 prompts report almost twice as many findings
(143 against 75), and most of the extra ones are wrong: precision falls to 41% and trap noise reaches
9 findings per round. It finds 5 more bugs on the small cases and 4 fewer on the large ones. This
repeats the result of ADR-008, which measured the 15 small cases only, now on the full set.

**Routing.** Sends diffs above 50 lines or 3 files to the strong model. It is the cheapest
alternative, at 2 times the single call, and improves 4 of the 5 gates a little, but passes none.
Trap resistance drops from 40% to 35%: the large trap goes to the strong model, which resisted it in
4 of 5 rounds, while the cheap model resisted it in all 5.
On the small cases the strong model found more on only 1 of 15, so the cheap route rarely loses.
ADR-017 records it and leaves it off.

**Advisor.** Escalates to the strong model when the cheap review has no Bug and no Security finding,
and the cheap model reviews again with its hints. The merge reading is the only row in the table
that passes detection, and the replace reading is the only one that passes calibration. Neither
passes trap resistance, and both cost 3 times the single call. ADR-018 records it and leaves it off.

## Recommendation

Keep the single call on `gpt-4o-mini` in production.

No approach is approved, so the choice is between rows that fail. The cheap single call is the
cheapest and the fastest, and no pattern on top of it reaches more than 1 gate. Paying 2 to 4 times
more for 0 or 1 gate is not justified by the data.

The table also says where the quality problem sits. The strong model alone passes 3 gates without
any pattern, and no pattern lifted the cheap model above 1. The gap is the model, not the
orchestration. The next measurement that can change this recommendation is a model that passes
trap resistance and detection on its own, tested as a single call first.

Two situations reopen it:

- Detection becomes the only gate that matters, for example a security-only review. Then the merge
  advisor is the one approach that passes it, and its trap results need a fix before it ships.
- A strong model gets cheap enough to cost about the same as `gpt-4o-mini` per review. Then the
  strong single call wins outright, since it already passes 3 gates.

## Not in the table: Sectioning

Sectioning was measured on the scoring judge, not on the reviewer, so it has no row here. It split
the judge's 4 criteria into 4 calls and failed 2 of its 4 floors, scoring more generously instead of
telling reviews apart.
[ADR-016](../ADRs/ADR-016-sectioning-scoring-judge-measured-off.md) records it and leaves it off.

## Decisions behind each row

| Approach | Decision |
|---|---|
| The 5 gates | [ADR-015](../ADRs/ADR-015-composite-metric-approval-criterion.md) |
| Voting | [ADR-008](../ADRs/ADR-008-voting-pattern-off-by-default.md) |
| Routing | [ADR-017](../ADRs/ADR-017-cost-based-model-routing-measured-off.md) |
| Advisor | [ADR-018](../ADRs/ADR-018-advisor-category-alarm.md) |

The Voting run cost US$ 0.3439: US$ 0.0633 on the 15 golden cases and US$ 0.2806 on the 10 large
ones.

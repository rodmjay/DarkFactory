# Screen: Usage and cost
Route: /usage
Reads (local SQLite): model_calls rolled up by stage, member and day
Dispatches: none
Renders:
  headline figure        → MetricTile (states: with delta, without delta, missing data)
  by stage               → Table with a drawn share column
  by member              → Table (states: within budget, no cap, over budget)
  daily trend            → CostBar per day, cached against uncached input
States covered: normal, a provider that has stopped reporting, a member over its cap
Proposed additions: none

## Notes

One row per model call (ADR-0032), so every figure here is a query rather
than an estimate. That is what lets the screen say "no data" for thinking
tokens instead of quietly showing a zero: a missing number and a zero are
different facts, and only one of them is about the provider.

Cache hit rate moved by nine percentage *points*, which is not nine
percent. Deltas are stored as fractions or not at all, and anything that is
not a fraction is written as the words it actually is — otherwise the
formatter does that arithmetic for you and is wrong.

The by-member table exists for one comparison: Wren cost twice as much per
amendment as Hollis on the same model family, because it retried more. A
price list cannot show that.

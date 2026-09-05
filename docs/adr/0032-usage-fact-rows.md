# ADR-0032: Every model call writes a usage fact row; optimization is a query, not a guess

## Status
Accepted — **effective immediately**. Supersedes the `stage_usages` ledger
added during step 3d's model-free slice, and changes where
[ADR-0028](0028-project-agent-teams.md)'s budgets read from.

## Context
The factory will shortly be making several model calls per run across five
roles, with skills, context packs, cache strategies, thinking budgets and
personas all varying underneath. Every one of those is a lever someone will
eventually want to pull, and every one of them changes cost, latency and
outcome quality at the same time.

Without a record per call, questions like *"did raising the implementer's
context size actually reduce retries?"* or *"is the deliberate persona worth
four times the tokens?"* are answerable only by opinion. Aggregate counters
cannot answer them at all: by the time usage is summed into a per-run
total, every dimension that would explain it has been thrown away.

The ledger built for budgets (`stage_usages`) had exactly this shape — a
running total per stage attempt, adequate for enforcing a cap and useless
for anything else. Keeping both it and a fact table would mean two sources
of truth for the same tokens, which is worse than either.

## Decision
**`model_calls` is an append-only fact table with one row per gateway
call.** It carries:

- **Identity** — `org_id`, `project_id`, `run_id`, `batch_id?`, `stage_id`,
  `task_id?`, `attempt`, `team_member_id`, `persona_id?`, `deployment`,
  `provider`, `model_family`
- **Inputs** — `input_tokens_uncached`, `input_tokens_cached`,
  `cache_write_tokens`, `context_pack_ref`, `skill_revisions[]`,
  `prompt_template_version`, `thinking_preset`
- **Outputs** — `output_tokens`, `thinking_tokens`, `latency_ms`, `cost`
- **Outcome** — `artifact_valid_first_try`, `retried`, `steered`,
  `stage_result`

**Written by the gateway, never by an agent.** An agent that is trusted to
record its own usage is an agent that can forget to, and the one call most
worth recording is the one that went wrong. In practice this means a
recording decorator wrapping the provider gateway: the providers stay
ignorant of the database, and no caller can make a model call that escapes
the ledger.

**Budget enforcement reads from this table** rather than keeping a separate
ledger ([ADR-0028](0028-project-agent-teams.md)). One source of truth for
tokens spent.

**Roll-ups** per project and per org feed the dashboard and billing;
per-dimension views answer which skill revisions, context sizes, personas
and cache strategies improve outcome per token.

Effective immediately: the three step-3d agents write these rows on every
call.

### Cached and thinking counts are breakdowns, not additions

`input_tokens_cached` is a portion of the input the provider served from
cache, and `thinking_tokens` a portion of the output — not extra tokens on
top. They are recorded separately because they are *billed* differently: a
cache read costs a fraction of a fresh input token, so a cost model built
on totals alone is wrong in the direction that flatters us.

### Outcome columns are the point

`artifact_valid_first_try`, `retried` and `steered` are what make this a
fact table rather than a meter. Tokens alone rank a cheap model best at
everything. Tokens next to "did it produce something valid on the first
attempt" is what makes
[ADR-0022](0022-model-assignment-is-policy.md)'s verifiability criterion
measurable instead of merely asserted — and what would let us discover that
the expensive model is cheaper per *successful* stage.

## Consequences
- `stage_usages` is dropped. It was added hours before this decision and
  never carried production data, so it goes rather than lingering as a
  second answer to "what did this run cost".
- `ModelRequest` grows a call context: the gateway cannot record
  `run_id`, `stage_id` or `team_member_id` it was never told. Callers that
  have no run — the conversation
  ([ADR-0017](0017-conversation-is-the-product.md)) — supply what they
  have, and the run-scoped columns are nullable for exactly that reason.
- `cost` is computed at write time from a price table, not derived later.
  Prices change; what a call cost when it was made does not.
- The table will be the largest in the schema and is the obvious first
  candidate for partitioning or roll-up-and-truncate. That is a scaling
  decision for later; the row shape is chosen now so it does not have to
  be re-derived then.
- Both providers must report the same usage breakdown, or a per-dimension
  query silently means different things depending on who served the call.
  [ADR-0027](0027-foundry-model-gateway.md)'s two implementations both
  populate the full breakdown.

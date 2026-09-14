# ADR-0039: Spec generation is a swappable strategy; the gate is not

## Status
Proposed, 2026-09-13. Extends [ADR-0017](0017-conversation-is-the-product.md)
and [ADR-0037](0037-corpus-intake.md); leaves the approval gate and
[ADR-0035](0035-hierarchical-approval-and-blocking.md) exactly as they are.

## Context
The factory produces specifications in one way: `ArchitectPrompt`, one call,
one retry on invalid output. [ADR-0037](0037-corpus-intake.md) then added a
second way — extract, find holes, ask, fill, propose — as a parallel service
with its own tables and surface. A third (specs recovered from existing
code) would be a third bespoke service. The pattern is visible: what varies
is *how a candidate amendment is produced*; what must not vary is *what it
takes for one to be accepted*.

Projects need different ways. A founder talking through a greenfield idea,
a corpus of prose already written, a brownfield codebase with no specs at
all, a regulated domain where the missed edge case is the expensive one,
a team that thinks in examples rather than rules. One prompt serves none of
them well. Rod's instruction: strategies swappable on demand, by the user
or by the agent, per project, and the extension point built to be extended.

## Decision

**A strategy is a named, versioned procedure that produces a candidate,
never an amendment.** A candidate is a reply, zero or more spec diffs,
questions (holes, in [ADR-0037](0037-corpus-intake.md)'s closed kinds), and
per node the condition that would show it holds
([ADR-0040](0040-every-spec-is-addressable-and-proven.md)). A strategy
cannot write the graph.

**The gate is the factory's and is the same for every strategy:** schema,
references, open questions block proposal (ADR-0037's rule, generalised from
intake to every strategy), every created or revised node carries its
verification, and the result is stamped with the strategy that produced it.
A strategy may call the gate while it works, to validate and refine, but only
the factory turns a candidate into an amendment. A strategy that cheats the
gate fails conformance, not review.

**Strategies are compositions over a closed step vocabulary:** `gather`
(assemble context from a named source), `draft` (produce one or N candidate
diffs), `critique` (find holes and contradictions against the graph and
standards), `refine` (redraft given critique and answers), `judge` (rank
candidates against a rubric), `ask` (hand open questions to a person and
stop). Closed for the reason node kinds are closed: strategies stay
comparable, and a new step is a convention change, not a string a strategy
invents. Each step binds to a team assignment point (`strategy.critic`,
`strategy.judge`), so [ADR-0022](0022-model-assignment-is-policy.md)'s
verifiability rule applies per step: a critic can sit on a different, stronger
model than the drafter.

**Every strategy has a descriptor:** `id`, `version`, `title`, `summary`,
`needs` (conversation, corpus, workspace_code, standards, examples, graph),
`steps`, `cost` (model calls per turn: one, bounded N), and `suits`
(signals for automatic selection). Availability comes from project facts, and
an unavailable strategy is listed with the reason, e.g. `reverse` needs a
reachable workspace server.

**Selection precedence: turn override → conversation pin → project default
→ `auto`.** A person chooses in the UI or by argument. The agent chooses
through `auto`, where the router deployment picks from the available
descriptors per turn using cheap signals (graph size, imports present,
workspace connected, the shape of the message), and the architect may
recommend a switch in its reply. **The agent may choose; it may not choose
invisibly.** Every turn records who chose (`turn`, `conversation`,
`project`, `auto`) and why, and an `auto` choice renders as a line with its
reason and a control to switch. Pinning a conversation turns `auto` off for
it. The project default ships as `conversational`, so landing this
changes nothing. It flips to `auto` once
[ADR-0032](0032-usage-fact-rows.md)'s rows show auto's choices hold up.

**Three extension tiers, mirroring [ADR-0019](0019-three-plugin-tiers.md):**

1. **Built-in** strategies are C# `ISpecStrategy` implementations
   registered in DI, in-process, not forced through MCP.
2. **Declarative** strategies are a JSON pipeline over the step vocabulary,
   giving a role and a skill per step, held centrally with a stable id and
   immutable revisions exactly as skills are (ADR-0028, amended). A customer
   writes one without code or a server; standards servers may ship them as
   they ship team templates.
3. **Strategy servers** are MCP servers with `domain: spec_strategy`:
   `df.strategy.describe()` returns the descriptor and
   `df.strategy.run(context_ref)` returns a candidate. They obtain every model
   call through MCP **sampling** (ADR-0028's rule for agent servers), so the
   factory keeps model choice, budget and metering. Conformance runs one
   fixed conversation against a scratch project and requires a gate-valid
   candidate.

**The first built-in set:**

| id | What it does | Suits |
|---|---|---|
| `conversational` | Today's architect, unchanged: talk until settled, propose. | The default; small changes to a known graph. |
| `interview` | Keeps an explicit coverage checklist per feature (actors, states, failure paths, limits, permissions, data lifecycle, non-functional) and asks the single highest-value open question each turn. It proposes only when coverage is met. | Vague greenfield; "I want X" with nothing behind it. |
| `intake` | ADR-0037 unchanged, re-expressed as `gather(corpus) → draft → critique → ask → refine`. Its tables and surface stay. | A corpus already written elsewhere. |
| `example_first` | Specification by example. It induces rules from concrete scenarios the person gives, then generates boundary cases ("delinquent by zero days: blocked?") for them to classify. The classified cases become the nodes' verification. It asks the fewest questions that pin the rule down. | People who think in cases, and business rules with thresholds. |
| `adversarial` | Drafts, then a critic on a different model hunts contradictions with approved nodes, untestable statements, undefined terms, missing failure paths, and runs a premortem ("six months on this failed in production — why?"). Surviving critiques become questions; then it refines. | Regulated or high-blast-radius layers. |
| `tournament` | Produces N independent drafts, each with a different decomposition (by actor, by state machine, by data). A judge ranks them against a rubric: grain, testability, fit with the graph, fewest new layers. The top two render side by side and the person picks; the rest are kept on the turn. | Genuinely open design choices. |
| `reverse` | Reads code through the workspace server (`df.files.*`), drafts `observed` nodes for what the code does, each citing its file and symbol, and asks per node whether it is intended or an accident. This is [ADR-0024](0024-stable-spec-ids-drift-detected-not-prevented.md)'s reconcile turned into generation. | Brownfield code with no specs. |
| `decompose` | Turns one large-grain statement into its small-grain tree, with `constrains`/`depends_on` edges. This is how the brief's grain recommendation is carried out. | Feature-sized requests. |
| `standards_projection` | Derives project constraint nodes from the indexed standards that bear on the layers in play ([ADR-0023](0023-index-standards-not-live-reads.md)), so a standard becomes something this project can check. | New projects on an org with a standards corpus. |

**Everything is attributable.** Each call a strategy makes records
`prompt_template_version = <strategy>@<version>/<step>` on `model_calls` and
persists its own context pack. The turn records `strategy_ref`, and so does the
amendment's provenance. "Which strategy yields amendments approved without
revision, per token, per kind of project" becomes a query, not an opinion.

### Surface

```
df.strategies.list(project_id)                     descriptors, with availability and reasons
df.strategies.get(strategy_id, version?)
df.strategies.set_default(project_id, strategy_id | "auto")
df.strategies.define(scope, definition)            declarative tier → revision
df.strategies.replay(conversation_id, strategy_id) candidates only; never proposed
df.conversations.start(project_id, title?, strategy?)
df.conversations.turn(conversation_id, message, strategy?)
df.conversations.pin_strategy(conversation_id, strategy_id | null)
```

### Data model

```
spec_strategies            id, org_id, project_id?, kind ∈ {builtin, declarative, server}, server_id?, created_at
spec_strategy_revisions    hash, strategy_id, version, definition(jsonb), created_at, provenance_id   append-only
projects                   + spec_strategy_default (default 'conversational')
conversations              + strategy_pin?
turns                      + strategy_ref, strategy_selected_by, strategy_reason
amendments                 + strategy_ref
intake_questions           + conversation scope; becomes the question store for every strategy
```

## Consequences
- `ConversationService` splits along the line this ADR draws. Context
  assembly, the gate and persistence stay; the attempt-and-retry block
  becomes `conversational`. The existing conversation tests must pass
  unmodified, which is the proof the split changed nothing.
- A turn can now be blocked by open questions, not just by invalid output.
  The architect asks in prose today; under the gate those questions become
  rows. `conversational` adopts that only once the question store is shared,
  so the change is staged, not silent.
- Multi-call strategies cost more per turn. The descriptor's `cost` is shown
  before choosing, and member token budgets (ADR-0028) bound every step.
- `tournament` needs candidates side by side. That is a new
  [ADR-0021](0021-rendering-vocabulary.md) component (`spec_candidates`)
  through the convention process; until then it renders as N `spec_diff`
  payloads and a `form`.
- Replay is cheap because every context pack is already stored: a real
  conversation can be rerun under another strategy offline and compared,
  without touching the graph. This is how strategies get evaluated on
  customer work rather than on demos.
- Build order: the seam with `conversational` only (no behaviour change,
  selection recorded) → `adversarial` (the smallest strategy that behaves
  observably differently) → `tournament` → `example_first` → `intake` behind
  the seam → declarative tier → strategy servers → replay.

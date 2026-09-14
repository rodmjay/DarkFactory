# Dark Factory — Architecture Brief v2

## The main flow

1. **Converse.** You talk to an agent that knows your specifications and is
   maintaining them from your answers. It coordinates across the connected MCP
   servers to pull the right standards, and it works out how the work will be
   handed to lower agents based on those standards and specs.
2. **Approve specs.** Settled conversation becomes a proposed amendment to the
   spec graph. It is reviewed and approved.
3. **Execute to test.** Approved specs are queued in ordered batches. Each
   batch runs — plan, implement, verify — until everything in it passes
   testing.
4. **Deploy.** A batch that has fully passed is deployed as a unit.

Everything else in this document exists to make those four steps work.

## North star

Dark Factory builds Dark Factory. The repository carries its own `df`-shaped
project server and a `.dark-factory/workspace.json`, and a second factory
instance — never the one being edited — registers it as a customer project.
The factory maintains its own specifications (the brief and the ADRs are the
first spec graph), then does additive work (test harnesses, docs,
design-system states), then the engine itself under the rule that a
candidate build must pass the committed evidence suite before it replaces
the stable one. Anything that feels awkward doing to ourselves is a product
bug, found for free. The repo's standards become the first first-party
standards server.

This supersedes the original setup prompt. ADR-0001 through ADR-0014 remain
in force except where an ADR below explicitly amends one. Read this whole
document before starting step 3.

---

## The product, in one paragraph

Dark Factory is a hosted, web-based service a company converses with to
maintain the specifications for its entire product stack, and which then
builds against those specifications with architectural standards applied at
generation time. The buyer is a CTO or lead already shipping agent-written
code who can feel it turning shapeless. The line: **ship at agent speed
without losing the architecture.** What we sell is judgment — the
conversation, the specification graph, the orchestration, and the standards.
The hands (repos, deploys, infrastructure) are connectors and MCP servers.
The factory never becomes the only way to touch a project; a developer can
always bypass it, and drift is something we detect and reconcile rather than
prevent.

## What changed since v1

- The **chat is the product**, not a job-submission surface. Intake and spec
  are no longer the first two stages of a build run; they are an ongoing
  conversation with an architect that knows the spec graph and queries the
  project live.
- **Specifications are durable project state**, not run artifacts. They live
  in the factory's database as a versioned graph. A run is what happens when
  the code needs to catch up to the graph.
- **`df` is the namespace** and `df.describe` is the handshake. Every
  compliant server answers it.
- **Three plugin tiers:** built-in first-party connectors the factory holds
  credentials for (GitHub, Azure, Vercel), premium first-party servers
  (per-layer standards, QA), and a free community layer. Our own internal
  components are not forced through MCP.
- **The web UI is the primary interface.** No IDE required. The MCP front
  surface and IDE extension are secondary.
- Runs are **observable and steerable** by any authorized client.

## Amendments to earlier ADRs

- **ADR-0003 (pipeline)** — amended. New shape: `conversation → spec amendment
  (gate) → plan → implement → verify → ship`. The `intake` and `spec` stages
  are removed from the run; they are the conversation. A run is created from
  an approved amendment (or set of amendments), never from raw text.
- **ADR-0004 (typed artifacts)** — amended. `Spec` is no longer a run
  artifact. `Plan`, `ChangeSet`, `TestReport` remain. A run records the **spec
  snapshot** it was built against.
- **ADR-0009 (factory owns model access)** — extended by ADR-0022 (model
  hierarchy as policy).
- **ADR-0014 (local mode is the dev loop)** — extended by ADR-0025: hosted is
  the default product; a self-hosted image exists for customers whose project
  servers are unreachable from our cloud.

## New ADRs (write each as its own file, ADR-0015 onward)

**ADR-0015 — Runs are observable and steerable by any authorized client.**
`df.work.attach(run_id)` streams the run's activity: stage, current agent
turn, every outbound tool call, artifacts as produced.
`df.work.steer(run_id, message)` injects guidance into the next agent turn
of the current stage without cancelling, recorded as an event. Review is a
first-class role: an `after:stage` hook may be a human, a plugin, or a
model, and produces a typed `Review` artifact that approves or steers. The
dashboard and any external client consume the same stream.

**ADR-0016 — The spec graph lives in the factory database, content-addressed
and append-only.**
Nodes have a stable identity (`spec_id`, a ULID) and immutable revisions
keyed by SHA-256 of canonical content. Edges are typed (`depends_on`,
`conflicts_with`, `supersedes`, `implements`, `constrains`, plus
plugin-declared kinds) and point at node identities, not revisions. A
**snapshot** is a named, immutable set of `(spec_id, revision_hash)` pairs;
runs record the snapshot they built against; snapshots are diffable. Every
amendment carries provenance: conversation id, turn id, proposer (human or
agent), approver, timestamp. Nothing is ever updated or deleted in place. A
**markdown export** materializes the graph into the repo on demand as a
projection; the database remains the source of truth. This is the "git model
without git" — expect two or three tables plus provenance.

**ADR-0017 — The conversation is the product; the spec phase is a diff
against the graph.**
A conversation belongs to a project. Each user turn is answered by an
architect agent that has retrieved the relevant spec neighborhood and
standards. When the conversation settles, the spec phase produces a
**proposed amendment**: a set of node creations, revisions, and edge
changes, rendered as a diff ("creates 1, amends 2, conflicts with X decided
in March"). Amendments are gated; approvers are configurable per project
(none, one, several). Only approved amendments can seed a run. Conversations
and turns are persisted, queryable, and part of the audit trail.

**ADR-0018 — `df` namespace and `df.describe` handshake.**
All convention tools live under the dotted root `df.` (`df.files.read_many`,
`df.vcs.open_pr`, `df.specs.query`). Anything outside `df.` is the server's
own business, so a server can be compliant and useful standalone.
`df.describe()` is mandatory and returns: `convention_version`,
`capabilities[]`, `domain` (vcs, deploy, standards, qa, knowledge,
observability, ticketing…), `effective_config` (what this instance is
pointed at — repo, branch, environment, subscription), and `requires[]`
(capabilities it needs from elsewhere). The factory calls it first and
refuses to register a server that cannot answer. The registry holds a
**static manifest** per listed server; the factory compares it against the
**live describe** at registration and flags disagreement. Registration runs
a **conformance check**: each declared capability is exercised once against
a scratch project.

**ADR-0019 — Three plugin tiers; internal components are not forced through
MCP.**
*Built-in connectors* (GitHub, Azure, Vercel) ship with the product, are
authorized via OAuth in the web UI, hold customer credentials in the
factory's secret store, and implement `df.vcs.*` / `df.deploy.*` internally
so a customer may substitute their own server for any of them. They are the
quality floor: if a built-in connector needs to cheat around the convention,
the convention is not good enough. *Premium first-party servers* (per-layer
standards, QA) are subscription line items. *Community servers* are free,
publish with no review queue beyond conformance, and are listed by manifest.
Our own orchestrator, deployer, and QA agent are in-process components that
expose `df.*` surfaces where third parties might replace them but are not
required to call themselves over MCP.

**ADR-0020 — Convention versioning and migration.**
The convention is a published, versioned contract. A new version ships with
a stated overlap window during which the previous version remains supported.
The factory supports at least two versions concurrently and adapts per
server using the version from `df.describe`. Where a change is a rename or
an added field, the factory shims older servers automatically. A
**conformance CLI** (`df-conform`) lets an author exercise their server
against a target version locally and reports exactly what is missing. The
factory knows every connected server's version and can report adoption to
authors.

**ADR-0021 — Rich responses bind to a factory-owned rendering vocabulary.**
Conversational and run responses are typed payloads, not prose alone. The
factory owns a versioned vocabulary of renderable component types —
`spec_diff`, `dependency_graph`, `stage_timeline`, `approval_card`, `table`,
`code_diff`, `form`, `metric`, `markdown` — each with a JSON schema. Plugins
emit payloads that bind to these types; they never ship UI. A plugin
introducing a new object kind maps it onto existing components or proposes a
new component type through the convention process. The web UI renders the
vocabulary; the MCP front surface returns the same payloads as structured
content.

**ADR-0022 — Model assignment is a policy, not code.**
Which model handles which stage or task is configuration the factory reads
per project and per plan (`agent_policies`). The governing criterion is
**verifiability of outcome**, not seniority: work a test or schema can check
may go to a cheap model; work whose subtle failure would go unnoticed (spec
amendments, architectural decisions, completion judgment) goes to the
strongest model regardless of size. Default policy: orchestration and spec
amendment on the strongest available model, planning on Opus-class,
implementation on Sonnet-class, mechanical tasks and routing on Haiku-class.
Policies are a product lever (cheaper tier, enterprise tier).

**ADR-0023 — Index standards; do not read servers live per turn.**
At registration and on change notification, the factory pulls each
standards/knowledge server's content once and builds its own index
(embeddings plus structure). A conversational turn performs one retrieval
across the spec graph and all indexed standards, scoped by **layer routing**
(which layers the turn touches) and **spec neighborhood** (graph traversal
from the nodes in play). Servers are called live for actions and for current
code state, not to re-answer "what are your standards." A small-model triage
pass selects layers and nodes before the expensive model sees assembled
context. Token usage per turn is metered and visible.

**ADR-0024 — Stable spec identifiers are referenced from code; drift is
detected, not prevented.**
Every spec node's `spec_id` is a stable identifier that generated code
references however its layer can: a C# attribute (`[Spec("01J…")]`), a
structured comment, a manifest entry for Terraform or SQL. Each standards
server declares how its layer carries the reference. Because the factory
generates the code, it applies references itself. This gives bidirectional
traceability (spec → implementing symbols, symbol → justifying spec),
computable drift (`df.specs.reconcile` reports specs with no implementation
and code with no spec), and generated diagrams from real structure. A
developer editing by hand outside the factory is tolerated; reconcile
surfaces the divergence and offers to update whichever side is wrong.

**ADR-0025 — Identity, tenancy, and deployment modes.**
A token (PAT or OAuth) carries **user and org**, never the workspace; the
workspace is selected per connection or via `df.projects.select`. Orgs own
subscriptions and projects; roles govern who may approve gates and install
servers. **Hosted is the default product.** A **self-hosted image** (same
image as Compose) exists for customers whose project servers cannot be
reached from our cloud; it validates entitlement against our licensing
endpoint, caches it for a generous window (≈7 days), runs normally offline
within the window, and degrades after. Self-hosted customers keep their spec
graph on their own Postgres; we accept the loss of telemetry.

**ADR-0026 — Hosting.**
Factory, worker, and Postgres run on Azure Container Apps (scale-to-zero,
wake-on-request suits bursty factory load). The Next.js web UI runs on
Vercel. Local development is Docker Compose with the same image.

**Note (not an ADR yet) — Branded standards as marketing objects.**
Premium standards servers may be authored by third parties under their own
brand, with accompanying content. This implies revenue share, payouts,
ratings or curation, and a public-facing marketplace. Deferred; recorded so
the registry schema leaves room for `author`, `brand`, `pricing`, and
`rating`.

## Open decision — flagged for Rod, not for Claude Code to resolve

**Spec node grain.** Recommendation: small-grained — a node is a single
behavior, rule, or constraint ("renewal is blocked if the account is
delinquent"), not a whole feature. Features are groupings via
`constrains`/`depends_on` edges. Small grain is what makes "this contradicts
the rule you set in March" possible. Cost: a feature request produces many
nodes and the UI must make that legible. Build the schema so either grain
works; default the conversation agent to small grain.

## Data model additions (before any new front-surface code)

```
conversations      id, project_id, org_id, title, created_by, created_at, status
turns              id, conversation_id, seq, role, content, payloads(jsonb), retrieval_ref, token_usage, created_at
spec_nodes         spec_id(ulid), project_id, org_id, kind, layer, created_at, retired_at?
spec_revisions     hash(sha256), spec_id, content(jsonb), canonical_text, created_at, provenance_id
spec_edges         id, project_id, from_spec_id, to_spec_id, kind, created_at, provenance_id, retired_at?
spec_snapshots     id, project_id, name, created_at, provenance_id
snapshot_members   snapshot_id, spec_id, revision_hash
amendments         id, project_id, conversation_id, turn_id, proposed_by, status, diff(jsonb), created_at
approvals          (extend existing) target_type ∈ {amendment, gate}
provenance         id, conversation_id?, turn_id?, actor_type, actor_id, approved_by?, at
servers            id, org_id, project_id?, name, tier, domain, convention_version, manifest(jsonb), live_describe(jsonb), status, last_conformance_at
standards_index    server_id, project_id?, chunk_ref, layer, embedding(vector), text, source_ref
agent_policies     id, project_id, stage_or_task, model, max_cost, fallback
runs               (extend) snapshot_id, amendment_ids[]
```

All tables carry `org_id` and `project_id` where scoped. Append-only tables
get no `UPDATE` grants for the application role.

## Front MCP surface v2

Keep `df.projects.*` and `df.work.*` from v1 (drop `work.submit(input:
string)`; a run is created from approved amendments). Add:

```
df.describe()                                   the factory answers its own handshake
df.projects.select(project_id)
df.conversations.start(project_id, title?)
df.conversations.turn(conversation_id, message)     → payloads[] per ADR-0021
df.conversations.list(project_id) / get(conversation_id)   the thread as stored, with amendment state
df.specs.query(project_id, q, layer?, kinds?, limit)
df.specs.get(spec_id, revision?)
df.specs.neighborhood(spec_id, depth)
df.specs.propose(conversation_id, diff)              → amendment
df.specs.approve(amendment_id) / reject(amendment_id, reason)
df.specs.snapshot(project_id, name)
df.specs.diff(snapshot_a, snapshot_b)
df.specs.export(project_id, snapshot_id?)            → markdown projection as an artifact
df.specs.reconcile(project_id)                       stub in v2
df.work.create(project_id, amendment_ids[])          → run
df.work.attach(run_id)                               stream; stub returns current event tail
df.work.steer(run_id, message)                       stub records the event
df.servers.register(url) / list / remove             register runs describe + conformance
df.intake.start(project_id, name, sources[])         import an existing corpus (ADR-0037)
df.intake.extract(source_id)                         draft + holes as questions; re-run to fill
df.intake.status / questions / answer / defer
df.intake.propose(source_id)                         → amendment; refused while holes are open
df.intake.pull(project_id, server_id, area?)         import from a corpus server (ADR-0038)
df.intake.drift(intake_id)                           what changed at the source since the pull
df.servers.check(server_id)                          health check now; heals if it was down (ADR-0038)
df.standards.ingest(server_id)                       copy a standards server into the index (ADR-0023); also automatic
```

Resources: `factory://projects`, `factory://projects/{id}/specs`,
`factory://runs/{id}`, `factory://conversations/{id}`.

## Step 3, redefined

Stop for review after each part.

**3a — Spec graph.** Migrations for the tables above. `df.specs.*`
implemented with content addressing, append-only enforcement, snapshots, and
diff. Tests: identical content yields identical hash; a revision never
mutates; snapshot diff reports created/revised/retired nodes and edge
changes; provenance is required and immutable; the application role cannot
`UPDATE` revision rows.

**3b — Handshake and registry.** `df.describe` on the reference workspace
server and on the factory itself. `df.servers.register` performs describe,
stores manifest vs live, and runs a minimal conformance check (exercise
`df.files.list` and `df.exec.run` against a scratch directory). Registration
of a server that cannot answer `df.describe` fails with a clear error.

**3c — Conversation, minimal.** `df.conversations.turn` retrieves the spec
neighborhood plus a hardcoded default standards resource (index ADR-0023 is
stubbed to "read the one resource"), calls the strongest configured model
with a single well-structured prompt, and returns payloads: a `markdown`
reply and, when the agent judges the conversation settled, a `spec_diff`
proposal via `df.specs.propose`. Approval via `df.specs.approve` creates the
amendment; `df.work.create` seeds a run at `plan` against a fresh snapshot.

**3d — End to end.** Register this repo's reference workspace server → start
a conversation → propose and approve an amendment → create a run → run
reaches `ship` and opens a PR. The PR body must include the spec ids
implemented and the snapshot id.

**Step 4 (unchanged in spirit):** the web UI renders conversations, spec
diffs, the graph neighborhood, and run timelines from the ADR-0021
vocabulary, live via SignalR. It is the primary interface; treat the
dashboard shells from step 1 as scaffolding to be replaced.

## Still out of scope

OAuth and org roles, billing and metering beyond token counts, built-in
GitHub/Azure/Vercel connectors, the standards index (embeddings),
`df.specs.reconcile`, the C# attribute generator, marketplace and branded
standards, self-hosted licensing, Temporal, the IDE extension, deploy hooks.

---

## Addendum — ADR-0027 and ADR-0028

**ADR-0027 — Microsoft Foundry powers the intelligence layer (amends
ADR-0009).**
All model inference, routing, and evaluation runs through Microsoft Foundry
on Azure. The factory never holds a model vendor's API key in production; it
authenticates to Foundry with Entra managed identity. Deployments are named
by role (`architect`, `planner`, `implementer`, `reviewer`, `router`) and
referenced by name from team definitions, so swapping the model behind a
role is a Foundry change, not a code change. Foundry's model router handles
the small-model triage pass from ADR-0023. Foundry usage metering is the
source of truth for token cost per project, run, and agent. Foundry Control
Plane evaluations and tracing score agent outputs; the factory records
evaluation results as run metadata. **Bring-your-own Foundry:** an org may
point its projects at its own Foundry resource, so tokens bill to its Azure
account and prompts remain in its tenant (with zero-data-retention where
required); hosted customers use ours. Foundry Agent Service is *not* used
for the core pipeline — the factory's durable engine remains the
orchestrator — but may host external agent servers later. Implementation:
`IModelGateway` in `DarkFactory.Core` with a Foundry implementation; local
development uses the same gateway with a key; nothing above the gateway
knows the provider.

**ADR-0028 — Each project has a standing team of agents; members are native
or MCP servers.**
A project has one active **team**, versioned and snapshotted like the spec
graph; a run records the team snapshot it was built with. A team member has
a role name, a Foundry deployment, a set of **skills** (versioned
instruction bundles, the same shape as Claude Code skills), the `df.*`
capabilities it may call, a token budget, and a fallback deployment. The
team's **assignment map** binds stages and hook points to members; this is
where ADR-0022's verifiability rule is applied in practice. Two member kinds
share a seat: **native agents** are definitions executed in-process by the
engine (default); **agent servers** are external MCP servers with `domain:
agent` in `df.describe`, exposing `df.agent.run(task_ref, context_ref)`, and
are required to obtain model calls through MCP **sampling** so the factory
retains model choice, policy, budget, and metering for every member. Premium
and community agents ship as agent servers. Standards servers may ship
**team templates**; skills are a fourth free plugin surface. Open: whether a
project may run multiple teams (e.g. per layer); the schema should allow it,
v1 UI exposes one.

**Data model additions:** `teams`, `team_revisions`, `team_members`,
`skills`, `skill_revisions`, `assignments`, plus `runs.team_snapshot_id`.
`agent_policies` is subsumed by `team_members` and `assignments`.

**Step 3 amendment:** 3c uses the `IModelGateway` against Foundry from the
start; the project created in 3d gets a default team of native agents
generated from a built-in template.

**ADR-0029 — Approved amendments are executed in ordered batches; deploy is
a batch-level action.**
Approved amendments enter a project backlog. A **batch** is an ordered set
of amendments; the factory proposes the order from spec-graph edges
(`depends_on` first) and the user may reorder. A batch executes as a
sequence of runs, each against the snapshot produced by the previous run, so
later items build on earlier ones. Batch gates: `verify` must pass for every
run before the batch is deployable; deploy is invoked once per batch, not
per run, and is the unit for release notes and rollback. Data model:
`batches`, `batch_items(batch_id, amendment_id, seq, run_id?)`,
`batches.status`, `batches.deployed_at`. Front surface:
`df.batches.create/order/execute/status/deploy`.

**ADR-0030 — Claude Code hooks are mapped mechanically to factory commands
under the `df:` namespace.**
A developer's Claude Code session is part of the factory whether or not the
web UI is open. A `df-hook` CLI is installed with the project; every Claude
Code hook entry invokes `df-hook <event>`, and `.dark-factory/hooks.json`
maps lifecycle events to factory commands as data, per project. Default
mapping: `SessionStart → df.context.pull` (inject spec neighborhood and
standards summary into session context), `UserPromptSubmit → df.specs.query`
(attach related spec nodes), `PreToolUse[Write|Edit] → df.specs.check`
(block or warn on edits to code whose spec reference has no approved
amendment), `PostToolUse[Bash git commit] → df.specs.reconcile` (record
drift), `Stop`/`SessionEnd → df.sessions.record` (audit), `WorktreeCreate →
df.projects.select`, `TaskCompleted → df.work.report`. Rules: hooks read a
local cache and post to the factory asynchronously so they stay fast; only
`df.specs.check` on `PreToolUse` may block, and only when the project's gate
config enables it; every call carries the Claude session id as its
idempotency key; hooks fire deterministically and never depend on the model
choosing to call a tool. The hook pack, the factory MCP server entry, and
the `df` skills ship together as a single installable Claude Code plugin.
Data model: `sessions(id, project_id, claude_session_id, started_at,
ended_at)`, `session_events(session_id, event, command, payload_ref, at)`.
Out of scope for step 3; scheduled after step 4.

**ADR-0028 — Amendment: centralized skills.**
Skills are held centrally in the factory: one library per org with
project-level overrides, each skill a stable id with immutable revisions.
Native agents read skills from the factory at run time; Claude Code sessions
receive the project's resolved skill set mechanically via `SessionStart →
df.skills.sync`, which pins them into `.claude/skills/` by revision (the
factory may also expose itself as a Claude Code plugin marketplace).
Standards servers may ship skills; community skills are free. A run's team
snapshot records every skill revision in use, so behavior is always
traceable to a skill version. Data model: `skills`, `skill_revisions`,
`skill_assignments(scope ∈ {org, project, team_member}, skill_id,
revision_hash)`.

**ADR-0031 — Local-first sync with PowerSync: reads sync down to
OPFS/SQLite, writes are `df.*` commands.**
The web UI and the `df-hook` CLI each hold a local SQLite mirror kept
current by PowerSync (web: OPFS VFS; CLI: Node SDK). Sync rules bucket by
`org_id` and `project_id` from the JWT, enforcing tenancy in the sync layer.
Synced: projects, spec_nodes, spec_revisions, spec_edges, spec_snapshots,
snapshot_members, snapshot_edges, amendments, approvals, conversations,
turns, runs, stages, events, batches, batch_items, teams, team_members,
skills, skill_revisions, servers (non-secret columns). Never synced:
artifact bodies, standards_index, audit_log, provenance secrets,
effective_config secrets. All client writes go through the PowerSync backend
connector to a `df.*` command; there are no raw table writes from clients.
Append-only tables mean no conflict resolution; write checkpoints reconcile
optimistic local rows. Consequences: `migrate` owns `wal_level=logical`, the
publication, and the PowerSync sync-rules file; the PowerSync Open Edition
service joins Compose and the self-hosted image; live dashboard updates come
from the synced `events` table, leaving SignalR only for streaming agent
turns during `df.work.attach` (to be removed if turn streaming moves to a
table); the `df-hook` local cache is the synced SQLite. Client schema is
declared per table in the web app; adding a synced table is a two-schema
change. Scheduled with step 4 (web UI).

**Note (deferred, not an ADR) — Personas.** Team members may be sold as
named, pictured personas: a persona bundles a role, a model deployment,
skills, and a monthly price under a name, portrait, and description, so the
team page becomes a roster to hire from. Branded standards authors may
publish personas that embody their standards. The card must state the
underlying model family. No schema yet; `team_members` should leave room for
a nullable `persona_id`.
A persona also declares a **speed** preset (quick / balanced / deliberate)
that maps to the gateway's per-call thinking budget; speed is independent of
model family, so two personas on one model may differ in price and depth.
Thinking tokens count against member budgets.

**ADR-0032 — Every model call writes a usage fact row; optimization is a
query, not a guess.**
`model_calls` is an append-only fact table with one row per gateway call:
`org_id, project_id, run_id, batch_id?, stage_id, task_id?, attempt,
team_member_id, persona_id?, deployment, provider, model_family`; inputs
`input_tokens_uncached, input_tokens_cached, cache_write_tokens,
context_pack_ref, skill_revisions[], prompt_template_version,
thinking_preset`; outputs `output_tokens, thinking_tokens, latency_ms,
cost`; outcome `artifact_valid_first_try, retried, steered, stage_result`.
Written by the gateway, never by agents. Budget enforcement (3d) reads from
it rather than keeping a separate ledger. Roll-ups per project and org feed
the dashboard and billing; per-dimension views answer which skill revisions,
context sizes, personas, and cache strategies improve outcome per token.
Effective immediately: the three 3d agents must write these rows on every
call.

**ADR-0034 — The factory is its own first customer (north star).**
The repo ships a specialized `df` project server (reference server
configured for this stack: `dotnet test`, `pnpm test`,
`scripts/check-stack.sh` as verify; `df.describe` reports C#, Next.js,
Postgres, Compose) and `.dark-factory/workspace.json`. A `staging` Compose
profile runs a second factory with its own database pointed at the real
checkout. Sequence: (1) the staging factory maintains the repo's own specs —
import the brief and ADRs as the first graph; (2) additive, mechanically
verifiable work (test harnesses, docs, showcase states); (3) engine changes,
gated on the candidate passing `docs/evidence` suites before promotion. The
running factory never modifies itself. Owner: backend session, after the
first push.

**ADR-0035 — Approval is hierarchical along `depends_on`; rejection blocks
dependents rather than rejecting them.**
A spec node cannot be approved while any node it transitively depends on is
unapproved or rejected; approval proceeds in dependency order. Rejecting a
node sets every transitive dependent to `blocked_by` (still proposed, not
rejected); a later approval of a revised parent unblocks them. Before reject
or retire, the approval surface reports blast radius (count of blocked nodes
and affected amendments). Blocked nodes cannot enter a batch; the batch
order proposer already respects `depends_on`. Retiring an approved node is
subject to the same dependent check. Approval state is a property of the
node in the graph, not of the amendment; an amendment is approved when all
its nodes are. Data model: `spec_nodes.approval_state ∈ {proposed, approved,
rejected, blocked_by, retired}`, `blocked_by_spec_id`. Front surface:
`df.specs.approve/reject` gain `cascade_preview: true` returning the blast
radius without acting. UI: `ApprovalCard` shows dependency order and blast
radius; `SpecDiff` marks blocked elements.

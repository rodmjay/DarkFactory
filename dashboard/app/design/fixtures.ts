import type {
  Amendment,
  Approval,
  Batch,
  Payload,
  Persona,
  Provenance,
  Server,
  SpecConflict,
  SpecDiffDocument,
  SpecNode,
  StageTimelineEntry,
  TeamMember,
} from "@dark-factory/ui";

/**
 * Fixtures for the showcase.
 *
 * Drawn from the real 3d acceptance run in `docs/evidence/3d/` wherever
 * possible — the spec ids, the snapshot, the token counts and the stage
 * sequence are that run's, not invented. A specimen sheet built on plausible
 * numbers hides the cases real data produces: a 26-character ULID that has
 * to fit in a table cell, a cost of $0.0004 that must not round to zero, an
 * implement stage that emitted 15,208 tokens.
 */

export const RUN_ID = "01M1SCBRA1ZC2TFTMCY1DGXV7V";
export const SNAPSHOT_ID = "01M1SCBR8ZCK5346WGW2C2VE26";
export const SPEC_ID = "01M1SCBR6RPXC3A1VK8K810ZE6";
const SPEC_ID_B = "01JAV3K8QW7Z9RXNB4MDPT6FE2";
const SPEC_ID_C = "01BX5ZZKBKACTAV9WEVGEMMVRZ";

export const timelineStages: Record<string, StageTimelineEntry[]> = {
  running: [
    {
      stage: "plan",
      status: "passed",
      owner: { team_member_id: "m1", role: "planner", deployment: "planner" },
      artifact: { ref: "factory://artifacts/01M1", type: "Plan" },
      cost: { input_tokens: 510, output_tokens: 622, usd: 0.0118 },
      attempt: 1,
    },
    {
      stage: "implement",
      status: "running",
      owner: { team_member_id: "m2", role: "implementer", deployment: "implementer" },
      cost: { input_tokens: 671, output_tokens: 15208, usd: 0.1534 },
      attempt: 1,
    },
    { stage: "verify", status: "pending" },
    { stage: "ship", status: "pending" },
  ],

  parked: [
    {
      stage: "plan",
      status: "passed",
      owner: { team_member_id: "m1", role: "planner", deployment: "planner" },
      artifact: { ref: "factory://artifacts/01M1", type: "Plan" },
      cost: { input_tokens: 510, output_tokens: 622, usd: 0.0118 },
    },
    {
      stage: "implement",
      status: "parked",
      owner: { team_member_id: "m2", role: "implementer", deployment: "implementer" },
      cost: { input_tokens: 671, output_tokens: 4617, usd: 0.0472 },
      question:
        "Does GET /health/detailed return 200 with a body saying the database is down, or 503 when it is unreachable? Load balancers and uptime probes care which, and I would be guessing.",
    },
    { stage: "verify", status: "pending" },
    { stage: "ship", status: "pending" },
  ],

  failed: [
    {
      stage: "plan",
      status: "passed",
      owner: { team_member_id: "m1", role: "planner", deployment: "planner" },
      cost: { input_tokens: 510, output_tokens: 622, usd: 0.0118 },
    },
    {
      stage: "implement",
      status: "passed",
      owner: { team_member_id: "m2", role: "implementer", deployment: "implementer" },
      artifact: { ref: "factory://artifacts/01M2", type: "ChangeSet" },
      cost: { input_tokens: 671, output_tokens: 15208, usd: 0.1534 },
    },
    {
      stage: "verify",
      status: "failed",
      owner: { team_member_id: "m3", role: "reviewer", deployment: "reviewer" },
      artifact: { ref: "factory://artifacts/01M3", type: "TestReport" },
      attempt: 1,
    },
    { stage: "ship", status: "pending" },
  ],

  overBudget: [
    {
      stage: "plan",
      status: "passed",
      owner: { team_member_id: "m1", role: "planner", deployment: "planner" },
      cost: { input_tokens: 510, output_tokens: 622, usd: 0.0118 },
    },
    {
      stage: "implement",
      status: "over-budget",
      owner: { team_member_id: "m2", role: "implementer", deployment: "implementer" },
      cost: { input_tokens: 128_400, output_tokens: 96_210, usd: 4.82 },
      question:
        "This member has used 224,610 tokens on this run, over its budget of 200,000. Raise the cap or split the work — retrying will not help.",
    },
    { stage: "verify", status: "pending" },
    { stage: "ship", status: "pending" },
  ],

  shipped: [
    { stage: "plan", status: "passed", cost: { input_tokens: 510, output_tokens: 622, usd: 0.0118 } },
    {
      stage: "implement",
      status: "passed",
      artifact: { ref: "factory://artifacts/01M2", type: "ChangeSet" },
      cost: { input_tokens: 671, output_tokens: 15208, usd: 0.1534 },
    },
    {
      stage: "verify",
      status: "passed",
      artifact: { ref: "factory://artifacts/01M3", type: "TestReport" },
      cost: { input_tokens: 0, output_tokens: 0, usd: 0 },
    },
    {
      stage: "ship",
      status: "passed",
      artifact: { ref: "factory://artifacts/01M4", type: "PrBody" },
      // Verify makes no model call: the answer is an exit code.
      cost: { input_tokens: 0, output_tokens: 0, usd: 0 },
    },
  ],
};

export const specNodes: Record<string, SpecNode> = {
  default: {
    spec_id: SPEC_ID,
    kind: "interface",
    layer: "api",
    text: "GET /health/detailed responds with the current reachability of the database connection.",
    revision_hash: "9f1c4a2e7b03d8156ea94c0b7f2d31e8ab5c609374fd2e18",
    edges: { outgoing: 2, incoming: 1 },
  },
  selected: {
    spec_id: SPEC_ID_B,
    kind: "rule",
    layer: "product",
    text: "Renewal is blocked if the account is delinquent.",
    revision_hash: "3a71bd94ee20c5f81b7a44d0c9e6f2183bb0d7c4e51928af",
    edges: { outgoing: 4, incoming: 2 },
  },
  // df.specs.get returns { node, revisions }, oldest first. All three
  // rationale states appear, because all three arrive on the wire.
  withHistory: {
    spec_id: SPEC_ID_B,
    kind: "rule",
    layer: "product",
    text: "A subscription renewal is blocked while the owning account is delinquent.",
    revision_hash: "e28b7eb5c1904f3a77d0b2ee615c8a4419fd0c73b2e5a681",
    edges: { outgoing: 4, incoming: 2 },
    revisions: [
      {
        hash: "1f04c9a7b83e5d2160fa47cc90b1e8d3a5027e4f61bc9d08",
        text: "Renewal is blocked if the account is delinquent.",
        rationale:
          "Finance asked for this after renewals were charged to accounts already in dunning. The charge succeeds and the refund lands in a different month, which is what makes it expensive.",
        created_at: "2026-03-14T10:22:00Z",
        actor_id: "architect",
        approved_by: "rod",
        conversation_id: "01M1ST3R9AK5N8KR6MGFFWCSD4",
        turn_id: "01M1ST3RCMYAGP8A8YB78BJ97B",
      },
      {
        hash: "7bd90ea1cc3f28b5104d6ea72f9c0b31d84e5a6072fb1c93",
        text: "Renewal is blocked if the account is delinquent at the time renewal is attempted.",
        // Absent: the wire sends no key at all when nobody gave a reason.
        created_at: "2026-06-02T09:05:00Z",
        actor_id: "architect",
        approved_by: "rod",
      },
      {
        hash: "e28b7eb5c1904f3a77d0b2ee615c8a4419fd0c73b2e5a681",
        text: "A subscription renewal is blocked while the owning account is delinquent.",
        rationale:
          "30 days after due date is the assumed grace window; confirm before this is built on. Renamed to say 'owning account' because a subscription and its payer are not always the same record.",
        created_at: "2026-09-05T22:13:28Z",
        actor_id: "org_local",
        approved_by: "org_local",
        conversation_id: "01M1ST3R9AK5N8KR6MGFFWCSD4",
        turn_id: "01M1ST3RCMYAGP8A8YB78BJ97B",
      },
    ],
  },
  retired: {
    spec_id: SPEC_ID_C,
    kind: "rule",
    layer: "product",
    text: "Renewal proceeds regardless of account standing.",
    retired_at: "2026-03-14T10:22:00Z",
    edges: { outgoing: 0, incoming: 3 },
  },
  drifted: {
    spec_id: "01CQZ0PGCC4KVFPNAJ5R6T9HTM",
    kind: "constraint",
    layer: "data",
    text: "Delinquency is evaluated against the billing ledger, never against a cached flag.",
    revision_hash: "c4e51928af3a71bd94ee20c5f81b7a44d0c9e6f2183bb0d7",
    edges: { outgoing: 1, incoming: 5 },
    drifted: true,
  },
};

export const specDiff: SpecDiffDocument = {
  creates: [
    {
      kind: "interface",
      layer: "api",
      text: "GET /health/detailed responds with the current reachability of the database connection.",
      rationale:
        "Operators need a signal that distinguishes process liveness from database connectivity. A load balancer that only knows the process is up will keep routing to an instance that cannot serve a request.",
    },
  ],
  revises: [
    {
      spec_id: SPEC_ID_B,
      text: "Renewal is blocked if the account is delinquent at the time renewal is attempted.",
      rationale:
        "30 days after due date is the assumed grace window; confirm before this is built on. The original left evaluation time open and billing and renewal disagreed about it.",
    },
  ],
  // No rationale at all — the absent-key case, which is what the wire sends
  // when nobody gave a reason.
  retires: [{ spec_id: SPEC_ID_C }],
  edge_adds: [
    {
      from_spec_id: "new:0",
      to_spec_id: SPEC_ID_B,
      kind: "depends_on",
      rationale:
        "The health endpoint reports on the same billing ledger the renewal rule reads; if that rule moves, this reports on the wrong thing.",
    },
  ],
  // An empty reason: somebody was asked and left it blank, which is not the
  // same fact as nobody being asked.
  edge_retires: [{ edge_id: "e-0194ab", rationale: "" }],
};

export const emptyDiff: SpecDiffDocument = {
  creates: [],
  revises: [],
  retires: [],
  edge_adds: [],
  edge_retires: [],
};

export const conflicts: SpecConflict[] = [
  {
    spec_id: SPEC_ID_C,
    text: "Renewal proceeds regardless of account standing.",
    detail:
      "This amendment blocks renewal for delinquent accounts, which directly contradicts this rule. One of the two has to go.",
    decided_at: "2026-03-14T10:22:00Z",
  },
];

export const approvals: Record<string, Approval> = {
  awaiting: {
    id: "01M1AMD1",
    target_type: "amendment",
    target_id: SPEC_ID,
    summary: "Creates 1 node, revises 2, retires 1 — and conflicts with a decision made on 14 March.",
    status: "awaiting",
    required_approvers: [
      { id: "u1", name: "Rod Johnson" },
      { id: "u2", name: "Priya Raman" },
    ],
  },
  approved: {
    id: "01M1AMD2",
    target_type: "amendment",
    target_id: SPEC_ID,
    summary: "Adds the detailed health endpoint to the api layer.",
    status: "approved",
    required_approvers: [
      { id: "u1", name: "Rod Johnson", decision: "approved", decided_at: "2026-09-05T11:02:00Z" },
    ],
  },
  rejected: {
    id: "01M1AMD3",
    target_type: "amendment",
    target_id: SPEC_ID,
    summary: "Removes the delinquency check from renewal.",
    status: "rejected",
    reason: "This is the rule finance asked for in March. If it needs to change, that is a conversation, not an amendment.",
    required_approvers: [
      { id: "u1", name: "Rod Johnson", decision: "rejected", decided_at: "2026-09-05T11:40:00Z" },
    ],
  },
  waitingOnOthers: {
    id: "01M1AMD4",
    target_type: "gate",
    target_id: RUN_ID,
    summary: "Deploy batch 14 to production.",
    status: "awaiting",
    required_approvers: [
      { id: "u2", name: "Priya Raman" },
      { id: "u3", name: "Sam Okafor", decision: "approved", decided_at: "2026-09-05T12:00:00Z" },
    ],
  },
};

export const amendments: Record<string, Amendment> = {
  default: {
    id: "01M1SCBR4C1DV27DC1R8AHEBA0",
    summary: "Add a detailed health endpoint that reports database reachability.",
    status: "approved",
    change_count: 2,
    layers: ["api"],
  },
  inBatch: {
    id: "01M1SCBR4C1DV27DC1R8AHEBA1",
    summary: "Block renewal while the account is delinquent.",
    status: "approved",
    change_count: 4,
    layers: ["product", "data"],
  },
  blocked: {
    id: "01M1SCBR4C1DV27DC1R8AHEBA2",
    summary: "Evaluate delinquency against the billing ledger rather than a cached flag.",
    status: "approved",
    change_count: 3,
    layers: ["data", "infra"],
    depends_on: ["01M1SCBR4C1DV27DC1R8AHEBA1"],
    blocked_by: "01M1…AHEBA1",
  },
};

export const batches: Record<string, Batch> = {
  composing: {
    id: "01M1BATCH01",
    name: "Batch 14 — billing rules",
    status: "composing",
    items: [
      { seq: 1, amendment_id: "a1", summary: "Block renewal while delinquent", status: "pending" },
      { seq: 2, amendment_id: "a2", summary: "Evaluate against the ledger", status: "pending" },
    ],
  },
  running: {
    id: "01M1BATCH02",
    name: "Batch 14 — billing rules",
    status: "running",
    items: [
      { seq: 1, amendment_id: "a1", summary: "Block renewal while delinquent", run_id: RUN_ID, status: "passed" },
      { seq: 2, amendment_id: "a2", summary: "Evaluate against the ledger", run_id: "01M1RUN02", status: "running" },
      { seq: 3, amendment_id: "a3", summary: "Add the health endpoint", status: "pending" },
    ],
  },
  blocked: {
    id: "01M1BATCH03",
    name: "Batch 15 — dunning",
    status: "blocked_by_verify",
    items: [
      { seq: 1, amendment_id: "a1", summary: "Retire the legacy dunning rule", run_id: "01M1RUN03", status: "passed" },
      { seq: 2, amendment_id: "a2", summary: "Add the grace period constraint", run_id: "01M1RUN04", status: "failed" },
      { seq: 3, amendment_id: "a3", summary: "Notify on entry to dunning", status: "pending" },
    ],
  },
  deployable: {
    id: "01M1BATCH04",
    name: "Batch 16 — health",
    status: "deployable",
    items: [
      { seq: 1, amendment_id: "a1", summary: "Add the health endpoint", run_id: RUN_ID, status: "passed" },
    ],
  },
  deployed: {
    id: "01M1BATCH05",
    name: "Batch 13 — invoicing",
    status: "deployed",
    deployed_at: "2026-09-04T16:30:00Z",
    items: [
      { seq: 1, amendment_id: "a1", summary: "Round invoice totals half-up", run_id: "01M1RUN05", status: "passed" },
      { seq: 2, amendment_id: "a2", summary: "Emit an invoice event on issue", run_id: "01M1RUN06", status: "passed" },
    ],
  },
};

export const teamMembers: Record<string, TeamMember> = {
  native: {
    id: "m1",
    role: "planner",
    kind: "native",
    deployment: "planner",
    model_family: "claude-opus-5",
    skills: [
      { name: "df-planning", revision: "0f31c8a4b2" },
      { name: "dotnet-standards", revision: "7bd90ea1cc" },
    ],
    token_budget: 200_000,
    spend: { input_tokens: 510, output_tokens: 622, usd: 0.0118 },
  },
  agentServer: {
    id: "m2",
    role: "reviewer",
    kind: "agent_server",
    deployment: "reviewer",
    model_family: "claude-opus-5",
    skills: [{ name: "qa-premium", revision: "aa41f0b7de" }],
    token_budget: null,
    spend: { input_tokens: 31_092, output_tokens: 4_120, usd: 0.312 },
  },
  persona: {
    id: "m3",
    role: "implementer",
    kind: "persona",
    deployment: "implementer",
    model_family: "claude-sonnet-5",
    speed: "deliberate",
    skills: [{ name: "df-implement", revision: "12c9e40aab" }],
    token_budget: 500_000,
    spend: { input_tokens: 671, output_tokens: 15_208, usd: 0.1534 },
    persona: {
      id: "p1",
      name: "Hollis",
      author: "Ovrline Standards",
      price_usd: 40,
      installed: true,
    },
  },
  overBudget: {
    id: "m4",
    role: "implementer",
    kind: "native",
    deployment: "implementer",
    model_family: "claude-sonnet-5",
    speed: "balanced",
    token_budget: 200_000,
    spend: { input_tokens: 128_400, output_tokens: 96_210, usd: 4.82 },
  },
};

export const personas: Record<string, Persona> = {
  free: {
    id: "p2",
    name: "Wren",
    author: "Dark Factory",
    description: "The default implementer. Returns whole files, references every spec it implements.",
    price_usd: null,
  },
  priced: {
    id: "p1",
    name: "Hollis",
    author: "Ovrline Standards",
    description:
      "A deliberate implementer that reads the standards server before it writes, and refuses work that contradicts a layer's conventions.",
    price_usd: 40,
  },
  installed: {
    id: "p3",
    name: "Marlow",
    author: "Cambrian QA",
    description: "A reviewer that writes the failing test first and only then reads the diff.",
    price_usd: 25,
    installed: true,
  },
  // Owned *and* assigned to this project's team — the state that answers
  // "is this persona actually doing any of my work".
  onTeam: {
    id: "p4",
    name: "Hollis",
    author: "Ovrline Standards",
    description:
      "A deliberate implementer that reads the standards server before it writes, and refuses work that contradicts a layer's conventions.",
    price_usd: 40,
    installed: true,
  },
};

export const servers: Record<string, Server> = {
  conformant: {
    id: "s1",
    name: "workspace-demo",
    domain: "workspace",
    tier: "community",
    convention_version: "1.0.0",
    health: "conformant",
    last_conformance_at: "2026-09-05T11:47:00Z",
    capabilities: [
      { name: "df.files.list", status: "passed" },
      { name: "df.files.read_many", status: "passed" },
      { name: "df.exec.run", status: "passed" },
      { name: "df.vcs.open_pr", status: "passed" },
    ],
    effective_config: { repo: "dark-factory", branch: "main" },
  },
  degraded: {
    id: "s2",
    name: "acme-standards",
    domain: "standards",
    tier: "premium",
    convention_version: "1.0.0",
    health: "degraded",
    last_conformance_at: "2026-09-05T09:12:00Z",
    capabilities: [
      { name: "df.standards.query", status: "passed" },
      {
        name: "df.standards.index",
        status: "failed",
        detail: "timed out after 30s",
      },
    ],
    effective_config: { subscription: "acme-enterprise" },
  },
  unreachable: {
    id: "s3",
    name: "internal-ticketing",
    domain: "ticketing",
    tier: "community",
    convention_version: "0.9.0",
    health: "unreachable",
    last_conformance_at: "2026-09-02T14:00:00Z",
    capabilities: [{ name: "df.tickets.create" }, { name: "df.tickets.link" }],
    effective_config: { host: "tickets.internal:8443" },
  },
  builtIn: {
    id: "s4",
    name: "GitHub",
    domain: "vcs",
    tier: "built_in",
    convention_version: "1.0.0",
    health: "unreachable",
    built_in_connector: true,
    authorized: false,
    capabilities: [
      { name: "df.vcs.branch" },
      { name: "df.vcs.commit" },
      { name: "df.vcs.open_pr" },
    ],
  },
};

export const provenance: Provenance = {
  conversation_id: "01M1SCBCSJSVEVB9CJ9RBX8XRS",
  conversation_title: "Delinquent renewals",
  turn_id: "01M1SCBR4YM7CJ5J0YM9ZSN58N",
  proposer: { name: "architect", type: "agent" },
  approver: { name: "Rod Johnson", at: "2026-09-05T11:02:00Z" },
  skill_revisions: [
    { name: "df-architect", revision: "0f31c8a4b2" },
    { name: "dotnet-standards", revision: "7bd90ea1cc" },
  ],
  model_family: "claude-opus-5",
  deployment: "architect",
  at: "2026-09-05T11:01:00Z",
};

export const patch = `diff --git a/src/DarkFactory.Mcp/Endpoints/HealthEndpoints.cs b/src/DarkFactory.Mcp/Endpoints/HealthEndpoints.cs
--- a/src/DarkFactory.Mcp/Endpoints/HealthEndpoints.cs
+++ b/src/DarkFactory.Mcp/Endpoints/HealthEndpoints.cs
@@ -12,6 +12,14 @@ public static class HealthEndpoints
     public static void MapHealthEndpoints(this WebApplication app)
     {
         app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
+
+        [Spec("${SPEC_ID}")]
+        app.MapGet("/health/detailed", async (DarkFactoryDbContext db) =>
+        {
+            var reachable = await db.Database.CanConnectAsync();
+            return Results.Ok(new { status = reachable ? "ok" : "degraded", database = reachable });
+        });
     }
 }`;

export const conflictPatch = `--- a/src/Billing/RenewalPolicy.cs
+++ b/src/Billing/RenewalPolicy.cs
@@ -8,7 +8,11 @@ public sealed class RenewalPolicy
     public bool MayRenew(Account account)
     {
<<<<<<< ours
         return account.IsActive;
=======
         return account.IsActive && !account.IsDelinquent;
>>>>>>> theirs
     }
 }`;

export const graph = {
  nodes: [
    { spec_id: SPEC_ID_B, layer: "product", label: "Renewal blocked" },
    { spec_id: SPEC_ID, layer: "api", label: "Health endpoint" },
    { spec_id: "01CQZ0PGCC4KVFPNAJ5R6T9HTM", layer: "data", label: "Ledger evaluation" },
    { spec_id: "01DEF0PGCC4KVFPNAJ5R6T9HTM", layer: "infra", label: "Billing job" },
    { spec_id: SPEC_ID_C, layer: "product", label: "Legacy renewal", retired: true },
  ],
  edges: [
    { from: SPEC_ID_B, to: "01CQZ0PGCC4KVFPNAJ5R6T9HTM", kind: "depends_on" },
    { from: SPEC_ID_B, to: SPEC_ID_C, kind: "conflicts_with" },
    { from: "01DEF0PGCC4KVFPNAJ5R6T9HTM", to: SPEC_ID_B, kind: "implements" },
    { from: SPEC_ID, to: "01CQZ0PGCC4KVFPNAJ5R6T9HTM", kind: "depends_on" },
  ],
};

/** One reply carrying three payloads, as a conversational turn actually does. */
export const multiPayloadReply: Payload[] = [
  {
    type: "markdown",
    text: "That is concrete enough for one node. Proposing it as a single `interface` node in the `api` layer:\n\n> **GET /health/detailed responds with the current reachability of the database connection.**\n\nI have left the status-code semantics out — 200-with-a-body and 503 are different observable behaviours, and callers care which.",
  },
  {
    type: "spec_diff",
    amendment_id: "01M1SCBR4C1DV27DC1R8AHEBA0",
    summary: "Creates 1 node in the api layer.",
    diff: {
      creates: [
        {
          kind: "interface",
          layer: "api",
          text: "GET /health/detailed responds with the current reachability of the database connection.",
        },
      ],
      revises: [],
      retires: [],
      edge_adds: [],
      edge_retires: [],
    },
  },
  {
    type: "metric",
    label: "Tokens this turn",
    value: 786,
    delta: -0.12,
    delta_is_good: true,
    series: [1200, 1040, 980, 900, 786],
    period: "vs last turn",
  },
];

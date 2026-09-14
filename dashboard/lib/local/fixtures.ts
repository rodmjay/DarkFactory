/**
 * The mirror's contents, as the prototype specifies them.
 *
 * These are rows, not markup. Every screen renders from here through
 * `useQuery`, so when PowerSync lands the screens keep working and this
 * file is what goes away.
 *
 * The conversation is the interesting part: every turn in it is a list of
 * ADR-0021 payloads, and the whole thread — prose, a retrieved
 * neighbourhood, a table, a clarifying form, a spec diff, an approval, a
 * parked run's timeline, a patch — is expressible in the nine component
 * types without adding a tenth. That was worth checking, and it held.
 */

import type { LocalDb, ScreenName } from "./schema";

/**
 * The states the screens must hold, as one flat union.
 *
 * They are demonstrable rather than hypothetical: each is a URL, so a
 * reviewer can open the offline state instead of being told it exists.
 */
export const SCENARIOS = [
  "settled",
  "thinking",
  "parked",
  "syncing",
  "offline",
  "empty",
  "projects-empty",
  "servers-empty",
  "amendments-empty",
  "graph-diff",
] as const;
export type Scenario = (typeof SCENARIOS)[number];

export const SCENARIO_LABEL: Record<Scenario, string> = {
  settled: "amendment settled, awaiting approval",
  thinking: "architect mid-turn",
  parked: "a run parked on needs-human, posted here",
  syncing: "optimistic write in flight",
  offline: "offline, reads from the local mirror",
  empty: "new conversation",
  "projects-empty": "an org with nothing connected",
  "servers-empty": "a project connected to nothing",
  "amendments-empty": "nothing awaiting approval",
  "graph-diff": "two snapshots compared",
};

/** Which scenarios each screen offers, for the state strip. */
export const SCENARIOS_FOR: Record<ScreenName, Scenario[]> = {
  conversation: ["settled", "thinking", "parked", "syncing", "offline", "empty"],
  projects: ["settled", "projects-empty", "offline"],
  specs: ["settled", "graph-diff"],
  amendments: ["settled", "amendments-empty"],
  batches: ["settled"],
  team: ["settled"],
  servers: ["settled", "servers-empty"],
  usage: ["settled"],
};

const SNAPSHOT = "01M1SCBCSJSVEVB9CJ9RC2VE26";
const PREV_SNAPSHOT = "01M0SCBCSJSVEVB9CJ9RJ4KQ21";
const RENEWAL = "01JAV3K8QW7Z9RXNB4MDPT6FE2";
const LEDGER = "01CQZ0PGCC4KVFPNAJ5R6T9HTM";
const LEGACY = "01BX5ZZKBKACTAV9WEVGEMMVRZ";
const AMENDMENT = "01M1SCBCSJSVEVB9CJ9RAHEBA1";
const RUN = "01M1SCBCSJSVEVB9CJ9RDGXV7V";
const CONVERSATION = "01M1SCBCSJSVEVB9CJ9RBX8XRS";

export function buildDb(scenario: Scenario): LocalDb {
  const offline = scenario === "offline";
  const syncing = scenario === "syncing";
  /**
   * An org with no projects has no current project, and therefore no runs,
   * no amendments and no batches either. Emptying only the roster would
   * leave the header naming a project that does not exist and the ticker
   * reporting runs against it — which is worse than either state on its own.
   */
  const emptyOrg = scenario === "projects-empty";

  return {
    sync: offline
      ? { state: "offline", pending: 1 }
      : syncing
        ? { state: "syncing", pending: 2 }
        : { state: "synced", pending: 0 },

    org: "ovrline",
    actor: { id: "u-rod", name: "Rod Johnson", initials: "RJ" },
    decision: { state: "undecided" },
    thinking: scenario === "thinking",

    project: emptyOrg ? null : projects[0],
    projects: emptyOrg ? [] : projects,

    ticker: emptyOrg ? [] : ticker,
    conversations,
    turns: turnsFor(scenario),
    retrieval: [retrieval],
    // "Nothing awaiting you" means nothing *proposed* — the approved and
    // rejected ones are still facts about the project, and the backlog count
    // the empty state quotes is computed from them.
    amendments: emptyOrg
      ? []
      : scenario === "amendments-empty"
        ? amendments.filter((a) => a.status !== "proposed")
        : amendments,
    batches: emptyOrg ? [] : batches,
    runs: [run],
    events,
    spec_nodes: specNodes,
    graph,
    snapshot_diff: snapshotDiff,
    team,
    server_groups: scenario === "servers-empty" ? [] : serverGroups,
    usage,
    connectors,
    // The prototype predates connections and imports; the product reads them
    // from the factory, and the showcase has no state that needs them.
    connections: [],
    intakes: [],
    imported_specs: [],
    graph_nodes: [],
  };
}

// ---------------------------------------------------------------- projects

const projects: LocalDb["projects"] = [
  {
    id: "billing-platform",
    name: "billing-platform",
    org: "ovrline",
    node_count: 412,
    snapshot_id: SNAPSHOT,
    layers: ["product", "api", "data", "infra"],
    active_runs: 2,
    parked_runs: 0,
    awaiting_amendments: 3,
    deployable_batches: 1,
    spend_usd: 18.4,
  },
  {
    id: "identity-service",
    name: "identity-service",
    org: "ovrline",
    node_count: 96,
    snapshot_id: PREV_SNAPSHOT,
    layers: ["api", "data"],
    active_runs: 0,
    parked_runs: 0,
    awaiting_amendments: 0,
    deployable_batches: 0,
    spend_usd: 4.02,
  },
  {
    id: "web-storefront",
    name: "web-storefront",
    org: "ovrline",
    node_count: 238,
    snapshot_id: "01M1SCBCSJSVEVB9CJ9R7RPQ08",
    layers: ["ui", "product", "api", "analytics"],
    declared_layers: ["analytics"],
    active_runs: 0,
    parked_runs: 1,
    awaiting_amendments: 0,
    deployable_batches: 0,
    spend_usd: 31.75,
  },
  {
    id: "dunning-worker",
    name: "dunning-worker",
    org: "ovrline",
    node_count: 0,
    snapshot_id: "",
    layers: [],
    active_runs: 0,
    parked_runs: 0,
    awaiting_amendments: 0,
    deployable_batches: 0,
    spend_usd: 0,
    unconnected: true,
  },
];

const connectors: LocalDb["connectors"] = [
  { id: "gh", name: "GitHub", domain: "df.vcs.*", note: "built in · convention 1.0.0" },
  { id: "az", name: "Azure", domain: "df.deploy.*", note: "built in · convention 1.0.0" },
  { id: "vc", name: "Vercel", domain: "df.deploy.*", note: "built in · convention 1.0.0" },
];

// ------------------------------------------------------------------ ticker

const ticker: LocalDb["ticker"] = [
  {
    id: "t1",
    project_id: "billing-platform",
    status: "running",
    label: "Batch 16 · run 2 of 3",
    detail: "implement",
    screen: "batches",
  },
  {
    id: "t2",
    project_id: "billing-platform",
    status: "parked",
    label: "Batch 15 · run 2",
    detail: "waiting on you",
    screen: "batches",
  },
  {
    id: "t3",
    project_id: "billing-platform",
    status: "passed",
    label: "Batch 14",
    detail: "deployable",
    screen: "batches",
  },
];

// ----------------------------------------------------------- conversations

const conversations: LocalDb["conversations"] = [
  {
    id: CONVERSATION,
    project_id: "billing-platform",
    title: "Delinquent renewals",
    subtitle: "1 amendment awaiting · 12:41",
    updated_at: "2026-09-05T12:41:00Z",
    turn_count: 6,
    deployment: "architect · claude-opus-5",
    snapshot_id: SNAPSHOT,
  },
  {
    id: "c-health",
    project_id: "billing-platform",
    title: "Health endpoint semantics",
    subtitle: "shipped in batch 16 · Fri",
    updated_at: "2026-09-04T09:00:00Z",
    turn_count: 4,
    deployment: "architect · claude-opus-5",
    snapshot_id: SNAPSHOT,
  },
  {
    id: "c-dunning",
    project_id: "billing-platform",
    title: "Dunning notification window",
    subtitle: "2 nodes revised · Thu",
    updated_at: "2026-09-03T16:02:00Z",
    turn_count: 7,
    deployment: "architect · claude-opus-5",
    snapshot_id: SNAPSHOT,
  },
  {
    id: "c-rounding",
    project_id: "billing-platform",
    title: "Invoice rounding",
    subtitle: "deployed · 4 Sep",
    updated_at: "2026-09-04T16:30:00Z",
    turn_count: 3,
    deployment: "architect · claude-opus-5",
    snapshot_id: SNAPSHOT,
  },
  {
    id: "c-terraform",
    project_id: "billing-platform",
    title: "Terraform state layout",
    subtitle: "no amendment · 2 Sep",
    updated_at: "2026-09-02T11:00:00Z",
    turn_count: 5,
    deployment: "architect · claude-opus-5",
    snapshot_id: SNAPSHOT,
  },
];

// ------------------------------------------------------------------- turns

const humanTurn = (
  seq: number,
  at: string,
  text: string,
  pending = false,
): LocalDb["turns"][number] => ({
  id: `turn-${seq}`,
  conversation_id: CONVERSATION,
  seq,
  author: "human",
  author_name: "Rod",
  at,
  pending,
  payloads: [{ type: "markdown", text }],
});

/** Turn 2: the architect answers with the neighbourhood it retrieved. */
const retrievalTurn: LocalDb["turns"][number] = {
  id: "turn-2",
  conversation_id: CONVERSATION,
  seq: 2,
  author: "agent",
  author_name: "architect",
  deployment: "claude-opus-5",
  at: "2026-09-05T11:42:00Z",
  payloads: [
    {
      type: "markdown",
      text:
        "Three nodes govern renewal and they do not agree with each other. The **product** layer still carries a rule from before dunning existed, and the **data** layer constrains how delinquency may be evaluated at all.\n\nHere is the neighbourhood I retrieved.",
    },
    {
      type: "dependency_graph",
      focus: RENEWAL,
      nodes: [
        { spec_id: RENEWAL, layer: "product", label: "Renewal blocked" },
        { spec_id: "01M1SCBCSJSVEVB9CJ9R810ZE6", layer: "api", label: "Health endpoint" },
        { spec_id: LEDGER, layer: "data", label: "Ledger evaluation" },
        { spec_id: "01DEZ0PGCC4KVFPNAJ5R6T9HTM", layer: "infra", label: "Billing job" },
        { spec_id: LEGACY, layer: "product", label: "Legacy renewal", retired: true },
      ],
      edges: [
        { from: RENEWAL, to: LEDGER, kind: "depends_on" },
        { from: RENEWAL, to: LEGACY, kind: "conflicts_with" },
        { from: "01DEZ0PGCC4KVFPNAJ5R6T9HTM", to: RENEWAL, kind: "implements" },
        { from: "01M1SCBCSJSVEVB9CJ9R810ZE6", to: LEDGER, kind: "depends_on" },
      ],
    },
    {
      type: "table",
      columns: [
        { key: "node", label: "Node" },
        { key: "layer", label: "Layer" },
        { key: "revised", label: "Last revised" },
        { key: "implemented", label: "Implemented by" },
      ],
      rows: [
        {
          node: "Renewal is blocked if the account is delinquent.",
          layer: "product",
          revised: "14 Mar 10:22 UTC",
          implemented: "RenewalPolicy.cs",
        },
        {
          node: "Delinquency is evaluated against the billing ledger, never a cached flag.",
          layer: "data",
          revised: "02 Jun 09:05 UTC",
          implemented: "drifted",
        },
        {
          node: "Renewal proceeds regardless of account standing.",
          layer: "product",
          revised: "11 Jan 08:31 UTC",
          implemented: "nothing",
        },
      ],
    },
  ],
};

/** Turn 4: the two things it will not guess at. */
const clarifyingTurn: LocalDb["turns"][number] = {
  id: "turn-4",
  conversation_id: CONVERSATION,
  seq: 4,
  author: "agent",
  author_name: "architect",
  deployment: "claude-opus-5",
  at: "2026-09-05T12:03:00Z",
  payloads: [
    {
      type: "markdown",
      text: "Two things I would be guessing at, and both are observable behaviour rather than wording.",
    },
    {
      type: "form",
      title: "Before I propose this",
      submit_label: "Answer and continue",
      fields: [
        {
          name: "grace",
          label: "Grace window after due date",
          kind: "select",
          value: "30",
          options: [
            { value: "30", label: "30 days" },
            { value: "14", label: "14 days" },
            { value: "0", label: "None — delinquent on the due date" },
          ],
          help: "This becomes a constraint node, not a config value.",
        },
        {
          name: "unreachable",
          label: "If the ledger is unreachable at renewal time",
          kind: "text",
          value: "Fail closed — do not renew, raise for review",
        },
      ],
    },
  ],
};

/** Turn 6: the amendment, its conflict, and the approval it is waiting on. */
const proposalTurn: LocalDb["turns"][number] = {
  id: "turn-6",
  conversation_id: CONVERSATION,
  seq: 6,
  author: "agent",
  author_name: "architect",
  deployment: "claude-opus-5",
  at: "2026-09-05T12:41:00Z",
  cost: {
    input_tokens: 12480,
    cached_input_tokens: 38200,
    output_tokens: 786,
    thinking_tokens: 1240,
    usd: 0.0412,
  },
  cost_delta: -0.12,
  cost_series: [64000, 60200, 57800, 55100, 52706],
  payloads: [
    {
      type: "markdown",
      text:
        "This settles into one amendment: it **creates 1** node, **amends 2**, and **conflicts with a rule you set in March**. The conflict is not a wording clash — the two rules cannot both hold.\n\n> You decided on 14 March that renewal proceeds regardless of account standing. Approving this retires that decision.",
    },
    specDiffPayload(),
    { type: "approval_card", approval: approval() },
  ],
};

/** The run that parked on the same question, posted into this conversation. */
const parkedTurn: LocalDb["turns"][number] = {
  id: "turn-run",
  conversation_id: CONVERSATION,
  seq: 7,
  author: "run",
  author_name: "run",
  at: "2026-09-05T12:52:00Z",
  run_id: RUN,
  batch_label: "batch 15 · posted to this conversation",
  payloads: [
    {
      type: "markdown",
      text: "The implementer stopped on the same question you and I settled above, in a run started before we did.",
    },
    {
      type: "stage_timeline",
      run_id: RUN,
      snapshot_id: SNAPSHOT,
      stages: [
        {
          stage: "plan",
          status: "passed",
          owner: { team_member_id: "m-planner", role: "planner", deployment: "planner" },
          artifact: { ref: "factory://artifacts/plan-1", type: "Plan", label: "Plan" },
          cost: { input_tokens: 1000, output_tokens: 132, usd: 0.0118 },
        },
        {
          stage: "implement",
          status: "parked",
          owner: { team_member_id: "m-hollis", role: "implementer", deployment: "implementer", persona_name: "Hollis" },
          cost: { input_tokens: 5000, output_tokens: 288, usd: 0.0472 },
          question:
            "Does the renewal check read the ledger inside the renewal transaction, or from the nightly snapshot? The nightly one is 14 hours stale at worst, and I would be guessing which the rule means.",
        },
        { stage: "verify", status: "pending" },
        { stage: "ship", status: "pending" },
      ],
    },
    {
      type: "code_diff",
      files_changed: ["src/Billing/RenewalPolicy.cs"],
      spec_annotations: { 10: RENEWAL },
      patch: [
        "@@ -8,7 +8,11 @@ public sealed class RenewalPolicy",
        "     public bool MayRenew(Account account)",
        "     {",
        "-        return account.IsActive;",
        "+        if (_ledger.IsDelinquent(account.Id)) return false;",
        "+        return account.IsActive;",
        "     }",
      ].join("\n"),
    },
  ],
};

function turnsFor(scenario: Scenario): LocalDb["turns"] {
  if (scenario === "empty") return [];

  const base: LocalDb["turns"] = [
    humanTurn(
      1,
      "2026-09-05T11:42:00Z",
      "Finance flagged three renewals last month that were charged to accounts already in dunning. What does the graph say about renewal today?",
    ),
    retrievalTurn,
    humanTurn(
      3,
      "2026-09-05T12:03:00Z",
      "Block renewal while the account is delinquent, evaluated at the moment renewal is attempted.",
    ),
    clarifyingTurn,
    humanTurn(5, "2026-09-05T12:41:00Z", "30 days, and fail closed. Propose it."),
  ];

  // Mid-turn: the thread stops at the question, and the thinking indicator
  // is what comes next. There is no reply to show yet.
  if (scenario === "thinking") return base;

  const withReply = [...base, proposalTurn];

  if (scenario === "parked") return [...withReply, parkedTurn];

  if (scenario === "syncing") {
    return [
      ...withReply,
      humanTurn(8, "2026-09-05T12:53:00Z", "Also check whether the nightly job reads the same definition.", true),
    ];
  }

  return withReply;
}

// --------------------------------------------------------------- retrieval

const retrieval: LocalDb["retrieval"][number] = {
  turn_id: "turn-6",
  node_total: 412,
  neighbourhood: [
    { spec_id: RENEWAL, layer: "product", text: "Renewal is blocked if the account is delinquent." },
    {
      spec_id: LEDGER,
      layer: "data",
      text: "Delinquency is evaluated against the billing ledger.",
      drifted: true,
    },
    { spec_id: LEGACY, layer: "product", text: "Renewal proceeds regardless of account standing." },
    {
      spec_id: "01DEZ0PGCC4KVFPNAJ5R6T9HTM",
      layer: "infra",
      text: "The nightly billing job re-evaluates standing.",
    },
    {
      spec_id: "01M1SCBCSJSVEVB9CJ9R810ZE6",
      layer: "api",
      text: "GET /health/detailed reports database reachability.",
    },
  ],
  standards: [
    { name: "dotnet-standards", health: "conformant", detail: "14 chunks" },
    { name: "acme-standards", health: "degraded", detail: "index stale" },
  ],
  standards_note:
    "acme-standards failed df.standards.index at 09:12; this turn used the last good index.",
  cost: {
    input_tokens: 12480,
    cached_input_tokens: 38200,
    output_tokens: 786,
    thinking_tokens: 1240,
    usd: 0.0412,
  },
  cache_hit_rate: 0.75,
  proposer: "architect",
  deployment: "claude-opus-5",
  at: "2026-09-05T12:41:00Z",
  skill_revisions: [
    { name: "df-architect", revision: "0f31c8a" },
    { name: "dotnet-standards", revision: "7bd90ea" },
  ],
};

// -------------------------------------------------------------- amendments

/**
 * The amendment as a `spec_diff` payload.
 *
 * Every element carries its `rationale` — which is the point of ADR-0016's
 * amendment: a reader can see *why* each change was made, not only who made
 * it. Two of these are deliberately not "given": the retire has no reason at
 * all, and one edge retire has an empty one. They render differently,
 * because "nobody said why" and "somebody was asked and left it blank" are
 * different facts with different fixes.
 */
function specDiffPayload(): LocalDb["turns"][number]["payloads"][number] {
  return {
    type: "spec_diff",
    amendment_id: AMENDMENT,
    summary: "Creates 1 node, revises 2, retires 1. One conflict detected against the current snapshot.",
    conflicts: [
      {
        spec_id: LEGACY,
        text: "Renewal proceeds regardless of account standing.",
        detail:
          "This amendment blocks renewal for delinquent accounts, which directly contradicts this rule. One of the two has to go.",
        decided_at: "2026-03-14T10:22:00Z",
      },
    ],
    diff: {
      creates: [
        {
          kind: "constraint",
          layer: "data",
          text: "An account is delinquent 30 days after an invoice passes its due date unpaid.",
          rationale:
            "The grace window was assumed in three places and written down in none. Naming it as its own node is what lets the renewal rule depend on it instead of restating it.",
        },
      ],
      revises: [
        {
          spec_id: RENEWAL,
          text: "Renewal is blocked if the account is delinquent at the time renewal is attempted.",
          rationale:
            "The original left evaluation time open, and billing and renewal disagreed about it. Pinning it to the attempt is what makes the three flagged charges impossible.",
        },
        {
          spec_id: LEDGER,
          text: "Delinquency is evaluated against the billing ledger, and fails closed when the ledger is unreachable.",
          rationale:
            "Fail-closed was your answer above. Recorded here rather than in the renewal rule, because every reader of delinquency needs it.",
        },
      ],
      // No rationale key at all: nobody gave a reason.
      retires: [{ spec_id: LEGACY }],
      edge_adds: [
        {
          from_spec_id: "new:0",
          to_spec_id: RENEWAL,
          kind: "constrains",
          rationale:
            "The renewal rule now reads delinquency from one definition instead of assuming a window.",
        },
      ],
      // An empty string, not an absent key: someone was asked and left it blank.
      edge_retires: [{ edge_id: "e-0194ab", rationale: "" }],
    },
  };
}

function approval(): NonNullable<LocalDb["amendments"][number]["approval"]> {
  return {
    id: "ap-1",
    target_type: "amendment",
    target_id: AMENDMENT,
    summary:
      "Creates 1 node, revises 2, retires 1 — and conflicts with a decision made on 14 March.",
    status: "awaiting",
    required_approvers: [
      { id: "u-rod", name: "Rod Johnson" },
      { id: "u-priya", name: "Priya Raman", decision: "approved", decided_at: "2026-09-05T12:44:00Z" },
    ],
  };
}

const amendments: LocalDb["amendments"] = [
  {
    id: AMENDMENT,
    project_id: "billing-platform",
    summary: "Block renewal while the account is delinquent, evaluated at the attempt.",
    status: "proposed",
    change_count: 4,
    layers: ["product", "data"],
    created_at: "2026-09-05T12:41:00Z",
    conversation_id: CONVERSATION,
    conversation_title: "Delinquent renewals",
    turn_seq: 6,
    snapshot_id: SNAPSHOT,
    proposer: "architect",
    deployment: "claude-opus-5",
    conflict_count: 1,
    payload: specDiffPayload(),
    approval: approval(),
  },
  {
    id: "01M1SCBCSJSVEVB9CJ9RLEDGER",
    project_id: "billing-platform",
    summary: "Evaluate delinquency against the billing ledger rather than a cached flag.",
    status: "proposed",
    change_count: 3,
    layers: ["data", "infra"],
    created_at: "2026-09-05T11:20:00Z",
    proposer: "architect",
  },
  {
    id: "01M1SCBCSJSVEVB9CJ9RDUNNIN",
    project_id: "billing-platform",
    summary:
      "Send a dunning notice on the day an account becomes delinquent, then not again for seven days.",
    status: "proposed",
    change_count: 2,
    layers: ["product"],
    created_at: "2026-09-03T16:02:00Z",
    proposer: "Priya Raman",
  },
  {
    id: "01M1SCBCSJSVEVB9CJ9RHEALTH",
    project_id: "billing-platform",
    summary: "Add a detailed health endpoint that reports database reachability.",
    status: "approved",
    change_count: 2,
    layers: ["api"],
  },
  {
    id: "01M1SCBCSJSVEVB9CJ9RREMOVE",
    project_id: "billing-platform",
    summary: "Remove the delinquency check from renewal.",
    status: "rejected",
    change_count: 1,
    layers: ["product"],
    created_at: "2026-09-05T11:40:00Z",
    rejected_reason:
      "This is the rule finance asked for in March. If it needs to change, that is a conversation, not an amendment.",
  },
];

// ----------------------------------------------------------------- batches

const batches: LocalDb["batches"] = [
  {
    id: "b16",
    project_id: "billing-platform",
    label: "Batch 16 — health",
    status: "running",
    spend_usd: 0.42,
    budget_usd: 5,
    blocked_reason: "Every run must pass verify first.",
    items: [
      { seq: 1, amendment_id: AMENDMENT, summary: "Block renewal while delinquent", status: "passed" },
      {
        seq: 2,
        amendment_id: "01M1SCBCSJSVEVB9CJ9RLEDGER",
        summary: "Evaluate against the ledger",
        run_id: RUN,
        status: "running",
      },
      {
        seq: 3,
        amendment_id: "01M1SCBCSJSVEVB9CJ9RHEALTH",
        summary: "Add the health endpoint",
        status: "pending",
      },
    ],
  },
  {
    id: "b15",
    project_id: "billing-platform",
    label: "Batch 15 — dunning",
    status: "blocked_by_verify",
    spend_usd: 1.86,
    budget_usd: 5,
    blocked_reason: "Run 2 has not passed verify.",
    note:
      "Run 2's test report has 3 failures in RenewalTests. Nothing after it starts until this is resolved — later runs build on its snapshot.",
    items: [
      { seq: 1, amendment_id: "a-legacy", summary: "Retire the legacy dunning rule", status: "passed" },
      { seq: 2, amendment_id: "a-grace", summary: "Add the grace period constraint", status: "parked" },
      { seq: 3, amendment_id: "a-notify", summary: "Notify on entry to dunning", status: "pending" },
    ],
  },
  {
    id: "b14",
    project_id: "billing-platform",
    label: "Batch 14 — billing rules",
    status: "deployable",
    spend_usd: 2.14,
    run_count: 2,
    note:
      "Every run passed verify. Deploy ships all 2 as one release — one set of notes, one rollback.",
    items: [
      { seq: 1, amendment_id: "a-round", summary: "Round invoice totals half-up", status: "passed" },
      { seq: 2, amendment_id: "a-event", summary: "Emit an invoice event on issue", status: "passed" },
    ],
  },
  {
    id: "b13",
    project_id: "billing-platform",
    label: "Batch 13 — invoicing",
    status: "deployed",
    spend_usd: 1.02,
    run_count: 2,
    deployed_at: "2026-09-04T16:30:00Z",
    deployed_note: "04 Sep 16:30 UTC · 2 runs · $1.02",
    items: [],
  },
];

const run: LocalDb["runs"][number] = {
  id: RUN,
  project_id: "billing-platform",
  batch_id: "b16",
  batch_label: "Run 2 of batch 16",
  snapshot_id: SNAPSHOT,
  team_revision: 4,
  attached: true,
  run_id: RUN,
  stages: [
    {
      stage: "plan",
      status: "passed",
      owner: { team_member_id: "m-planner", role: "planner", deployment: "planner" },
      artifact: { ref: "factory://artifacts/plan-2", type: "Plan", label: "Plan" },
      cost: { input_tokens: 1000, output_tokens: 132, usd: 0.0118 },
    },
    {
      stage: "implement",
      status: "over-budget",
      attempt: 1,
      owner: { team_member_id: "m-hollis", role: "implementer", deployment: "implementer", persona_name: "Hollis" },
      cost: { input_tokens: 220000, output_tokens: 4610, usd: 4.82 },
      question:
        "Hollis used 224,610 of its 200,000-token budget. You raised the cap to 400,000 and split the change set in two; attempt 2 is running on the first half.",
    },
    {
      stage: "implement",
      status: "running",
      attempt: 2,
      owner: { team_member_id: "m-hollis", role: "implementer", deployment: "implementer", persona_name: "Hollis" },
      cost: { input_tokens: 15000, output_tokens: 879, usd: 0.1534 },
    },
    { stage: "verify", status: "pending" },
    { stage: "ship", status: "pending" },
  ],
};

const events: LocalDb["events"] = [
  { id: "e1", run_id: RUN, at: "12:52:04", command: "df.files.read_many", detail: "3 files" },
  { id: "e2", run_id: RUN, at: "12:52:11", command: "df.standards.query", detail: "layer=data" },
  { id: "e3", run_id: RUN, at: "12:52:19", command: "df.files.write", detail: "Billing/LedgerReader.cs" },
  {
    id: "e4",
    run_id: RUN,
    at: "12:52:26",
    command: "df.exec.run",
    detail: "dotnet build",
    outcome: "exit 0",
  },
  { id: "e5", run_id: RUN, at: "12:52:41", command: "writing RenewalPolicy.cs", streaming: true },
];

// -------------------------------------------------------------- spec graph

const specNodes: LocalDb["spec_nodes"] = [
  {
    project_id: "billing-platform",
    spec_id: RENEWAL,
    kind: "rule",
    layer: "product",
    text: "A subscription renewal is blocked while the owning account is delinquent.",
    revision_hash: "e28b7eb1c4a9f30d5e6b7c8d9a0b1c2d3e4f5061728394a5b6c7d8e9f0a1a681",
    edges: { outgoing: 4, incoming: 2 },
    implemented_by: [
      { path: "src/Billing/RenewalPolicy.cs:24", status: "matches" },
      { path: "tests/Billing/RenewalTests.cs:61", status: "matches" },
    ],
    edge_summary: [
      { kind: "constrained_by", text: "An account is delinquent 30 days after…" },
      { kind: "depends_on", text: "Delinquency is evaluated against the ledger" },
      { kind: "implements", text: "The nightly billing job re-evaluates standing" },
      { kind: "supersedes", text: "Renewal proceeds regardless of standing" },
    ],
    revisions: [
      {
        hash: "1f04c9a",
        text: "Renewal is blocked if the account is delinquent.",
        created_at: "2026-03-14T10:22:00Z",
        rationale:
          "Finance asked for this after renewals were charged to accounts already in dunning. The charge succeeds and the refund lands in a different month, which is what makes it expensive.",
      },
      {
        hash: "7bd90ea",
        text: "Renewal is blocked if the account is delinquent at the time renewal is attempted.",
        created_at: "2026-06-02T09:05:00Z",
        // No rationale: this revision has none, and says so.
      },
      {
        hash: "e28b7eb",
        text: "A subscription renewal is blocked while the owning account is delinquent.",
        created_at: "2026-09-05T22:13:00Z",
        rationale:
          "30 days after due date is the assumed grace window; confirm before this is built on. Renamed to say “owning account” because a subscription and its payer are not always the same record.",
        actor_id: "architect",
        approved_by: "Rod Johnson",
        conversation_id: CONVERSATION,
      },
    ],
  },
  {
    project_id: "billing-platform",
    spec_id: LEGACY,
    kind: "rule",
    layer: "product",
    text: "Renewal proceeds regardless of account standing.",
    retired_at: "2026-03-14T10:22:00Z",
    retired_label: "retired 14 Mar",
    edges: { outgoing: 0, incoming: 3 },
  },
  {
    project_id: "billing-platform",
    spec_id: "01KPZ0PGCC4KVFPNAJ5RB22M4E",
    kind: "rule",
    layer: "product",
    text: "A dunning notice is sent on the day an account becomes delinquent, and not again for seven days.",
    edges: { outgoing: 2, incoming: 1 },
  },
  {
    project_id: "billing-platform",
    spec_id: "01HRZ0PGCC4KVFPNAJ5RM0X7QA",
    kind: "constraint",
    layer: "product",
    text: "Invoice totals are rounded half-up to the account's billing currency.",
    edges: { outgoing: 1, incoming: 6 },
  },
  {
    project_id: "billing-platform",
    spec_id: LEDGER,
    kind: "constraint",
    layer: "data",
    text: "Delinquency is evaluated against the billing ledger, never against a cached flag.",
    drifted: true,
    drift_detail:
      "RenewalPolicy.cs reads Account.IsDelinquent, a cached column. Reconcile last ran 05 Sep 06:00 UTC.",
    edges: { outgoing: 1, incoming: 5 },
  },
  {
    project_id: "billing-platform",
    spec_id: "01NMZ0PGCC4KVFPNAJ5R4TQ8WD",
    kind: "constraint",
    layer: "data",
    text: "Ledger entries are append-only; a correction is a new entry, never an update.",
    unimplemented: true,
    edges: { outgoing: 0, incoming: 2 },
  },
];

const graph: LocalDb["graph"] = {
  project_id: "billing-platform",
  node_count: 412,
  edge_count: 1084,
  snapshot_id: SNAPSHOT,
  layers: [
    { layer: "product", count: 96 },
    { layer: "api", count: 124 },
    { layer: "data", count: 88 },
    { layer: "infra", count: 61 },
    { layer: "ui", count: 39 },
    { layer: "analytics", count: 4 },
  ],
  edge_kinds: [
    { kind: "depends_on", count: 402 },
    { kind: "implements", count: 318 },
    { kind: "constrains", count: 241 },
    { kind: "supersedes", count: 116 },
    { kind: "conflicts_with", count: 7 },
  ],
  drifted: 6,
  unimplemented: 21,
};

const snapshotDiff: LocalDb["snapshot_diff"] = {
  project_id: "billing-platform",
  from: { id: PREV_SNAPSHOT, at: "04 Sep 16:30 UTC", note: "deployed with batch 13" },
  to: { id: SNAPSHOT, label: "current" },
  summary:
    "4 nodes created, 6 revised, 2 retired, 11 edges added, 1 edge retired between these two snapshots.",
  changes: [
    {
      kind: "added",
      spec_id: "01M1SCBCSJSVEVB9CJ9R8ZQ4R2",
      layer: "data",
      text: "An account is delinquent 30 days after an invoice passes its due date unpaid.",
    },
    {
      kind: "changed",
      spec_id: RENEWAL,
      layer: "product",
      text: "A subscription renewal is blocked while the owning account is delinquent.",
      was: "Renewal is blocked if the account is delinquent at the time renewal is attempted.",
    },
    {
      kind: "removed",
      spec_id: LEGACY,
      layer: "product",
      text: "Renewal proceeds regardless of account standing.",
    },
  ],
};

// -------------------------------------------------------------------- team

const team: LocalDb["team"] = {
  project_id: "billing-platform",
  revision: 4,
  members: [
    {
      id: "m-planner",
      role: "planner",
      kind: "native",
      deployment: "planner",
      model_family: "claude-opus-5",
      speed: "balanced",
      skills: [
        { name: "df-planning", revision: "0f31c8a" },
        { name: "dotnet-standards", revision: "7bd90ea" },
      ],
      token_budget: 200000,
      spend: { input_tokens: 1000, output_tokens: 132, usd: 0.0118 },
    },
    {
      id: "m-hollis",
      role: "implementer",
      kind: "persona",
      deployment: "implementer",
      model_family: "claude-sonnet-5",
      speed: "deliberate",
      skills: [{ name: "df-implement", revision: "12c9e40" }],
      token_budget: 400000,
      spend: { input_tokens: 15000, output_tokens: 879, usd: 6.31 },
      avatar_url: "/design/portrait-sample.svg",
      persona: {
        id: "p-hollis",
        name: "Hollis",
        author: "Ovrline Standards",
        price_usd: 40,
        installed: true,
      },
    },
    {
      id: "m-reviewer",
      role: "reviewer",
      kind: "agent_server",
      deployment: "reviewer",
      model_family: "claude-opus-5",
      speed: "quick",
      skills: [{ name: "qa-premium", revision: "aa41f0b" }],
      token_budget: null,
      spend: { input_tokens: 34000, output_tokens: 1212, usd: 0.312 },
    },
    {
      id: "m-router",
      role: "router",
      kind: "native",
      deployment: "router",
      model_family: "claude-haiku-5",
      token_budget: 200000,
      spend: { input_tokens: 220000, output_tokens: 4610, usd: 4.82 },
    },
  ],
  assignments: [
    {
      stage: "conversation",
      member: "architect",
      deployment: "architect → claude-opus-5",
      why: "A wrong spec amendment fails silently. Strongest model, always.",
    },
    {
      stage: "plan",
      member: "planner",
      deployment: "planner → claude-opus-5",
      why: "A bad plan is expensive downstream and cheap to catch here.",
    },
    {
      stage: "implement",
      member: "Hollis",
      deployment: "implementer → claude-sonnet-5",
      why: "Tests check the output, so a cheaper model is safe.",
    },
    {
      stage: "verify",
      member: "reviewer",
      deployment: "reviewer → claude-opus-5",
      why: "Judging completion is the failure nobody notices.",
    },
    {
      stage: "after:verify",
      member: "Rod Johnson",
      deployment: "—",
      why: "A human review hook produces the same typed Review artifact.",
      human: true,
    },
  ],
  hireable: [
    {
      name: "Wren",
      author: "Dark Factory",
      role: "implementer",
      price: "Free",
      blurb: "The default implementer. Returns whole files and references every spec it implements.",
      model_family: "claude-sonnet-5",
      speed: "balanced",
    },
    {
      name: "Marlow",
      author: "Cambrian QA",
      role: "reviewer",
      price: "$25/mo",
      blurb: "A reviewer that writes the failing test first and only then reads the diff.",
      model_family: "claude-opus-5",
      speed: "deliberate",
      state: "installed",
    },
    {
      name: "Hollis",
      author: "Ovrline Standards",
      role: "implementer",
      price: "$40/mo",
      blurb:
        "A deliberate implementer that reads the standards server before it writes, and refuses work that contradicts a layer's conventions.",
      model_family: "claude-sonnet-5",
      speed: "deliberate",
      state: "on_team",
    },
  ],
};

// ----------------------------------------------------------------- servers

const serverGroups: LocalDb["server_groups"] = [
  {
    project_id: "billing-platform",
    domain: "df.vcs.*",
    title: "Version control",
    servers: [
      {
        id: "s-github",
        name: "GitHub",
        domain: "df.vcs.*",
        tier: "built_in",
        convention_version: "1.0.0",
        health: "conformant",
        built_in_connector: true,
        authorized: true,
        last_conformance_at: "2026-09-05T11:47:00Z",
        effective_config: { repo: "ovrline/billing-platform", branch: "main" },
        capabilities: [
          { name: "df.vcs.branch", status: "passed" },
          { name: "df.vcs.commit", status: "passed" },
          { name: "df.vcs.open_pr", status: "passed" },
        ],
      },
      {
        id: "s-workspace-demo",
        name: "workspace-demo",
        domain: "df.vcs.*",
        tier: "community",
        convention_version: "1.0.0",
        health: "conformant",
        capabilities: [
          { name: "df.files.read_many", status: "passed" },
          { name: "df.files.write", status: "passed" },
          { name: "df.exec.run", status: "passed" },
          { name: "df.vcs.commit", status: "passed" },
        ],
      },
    ],
  },
  {
    project_id: "billing-platform",
    domain: "df.standards.*",
    title: "Standards",
    servers: [
      {
        id: "s-acme",
        name: "acme-standards",
        domain: "df.standards.*",
        tier: "premium",
        convention_version: "1.0.0",
        health: "degraded",
        last_conformance_at: "2026-09-05T09:12:00Z",
        capabilities: [
          { name: "df.standards.query", status: "passed" },
          {
            name: "df.standards.index",
            status: "failed",
            detail:
              "df.standards.index timed out after 30s during conformance. Everything else works, so the server stays in use and turns read the last good index.",
          },
        ],
      },
      {
        id: "s-dotnet",
        name: "dotnet-standards",
        domain: "df.standards.*",
        tier: "premium",
        convention_version: "1.0.0",
        health: "conformant",
        effective_config: { subscription: "ovrline-enterprise", layers: "api, data, infra" },
        capabilities: [
          { name: "df.standards.query", status: "passed" },
          { name: "df.standards.index", status: "passed" },
        ],
      },
    ],
  },
  {
    project_id: "billing-platform",
    domain: "df.deploy.*",
    title: "Deploy",
    servers: [
      {
        id: "s-azure",
        name: "Azure",
        domain: "df.deploy.*",
        tier: "built_in",
        convention_version: "1.0.0",
        health: "conformant",
        built_in_connector: true,
        authorized: true,
        effective_config: { environment: "billing-prod", resource: "aca/billing-api" },
        capabilities: [],
      },
      {
        id: "s-vercel",
        name: "Vercel",
        domain: "df.deploy.*",
        tier: "built_in",
        convention_version: "1.0.0",
        health: "conformant",
        built_in_connector: true,
        authorized: false,
        capabilities: [],
      },
    ],
  },
  {
    project_id: "billing-platform",
    domain: "df.tickets.*",
    title: "Ticketing",
    servers: [
      {
        id: "s-tickets",
        name: "internal-ticketing",
        domain: "df.tickets.*",
        tier: "community",
        convention_version: "0.9.0",
        health: "unreachable",
        effective_config: { endpoint: "tickets.internal:8443" },
        last_conformance_at: "2026-09-02T14:00:00Z",
        capabilities: [],
      },
    ],
  },
  {
    project_id: "billing-platform",
    domain: "df.qa.*",
    title: "QA",
    servers: [],
    empty: {
      title: "No QA server connected.",
      body: "Verify currently runs the repo's own test command. A QA server adds coverage gates and a typed TestReport the reviewer can read.",
      action: "Browse QA servers",
    },
  },
  {
    project_id: "billing-platform",
    domain: "df.telemetry.*",
    title: "Observability",
    servers: [],
    empty: {
      title: "Nothing connected.",
      body: "Connect one and a deployed batch can be checked against real traffic before the next one starts.",
      action: "Browse",
    },
  },
];

// ------------------------------------------------------------------- usage

const usage: LocalDb["usage"] = {
  project_id: "billing-platform",
  period: "1–5 September",
  headline: [
    { label: "Cost, Sep", value: "$18.40", delta: 0.34, delta_is_good: false, note: "vs Aug" },
    {
      label: "Per shipped amendment",
      value: "$2.30",
      delta: -0.18,
      delta_is_good: true,
      note: "8 shipped",
    },
    {
      // Nine percentage points, not nine percent. It stays in words.
      label: "Cache hit rate",
      value: "75",
      unit: "%",
      note: "+9pp of input tokens",
    },
    { label: "First-try artifacts", value: "82", unit: "%", note: "valid without a retry" },
    {
      label: "Thinking tokens",
      value: "—",
      missing: true,
      note: "no data — Foundry has not reported since 04 Sep",
    },
  ],
  by_stage: [
    { stage: "implement", share: 0.613, usd: 11.28, tokens: "1.1M", calls: 64, retried: 11 },
    { stage: "conversation", share: 0.24, usd: 4.41, tokens: "612,400", calls: 38, retried: 0 },
    { stage: "plan", share: 0.11, usd: 2.02, tokens: "184,200", calls: 22, retried: 1 },
    { stage: "verify", share: 0.037, usd: 0.69, tokens: "61,800", calls: 18, retried: 0 },
    { stage: "ship", share: 0.0001, usd: 0.0004, tokens: "1,240", calls: 8, retried: 0 },
  ],
  by_member: [
    {
      member: "Hollis",
      qualifier: "implementer",
      avatar: true,
      model: "claude-sonnet-5 · deliberate",
      usd: 6.31,
      per_amendment: 0.79,
      first_try: "91%",
      budget: "4% used",
    },
    {
      member: "architect",
      model: "claude-opus-5 · deliberate",
      usd: 4.41,
      per_amendment: 0.55,
      first_try: "—",
      budget: "no cap",
    },
    {
      member: "Wren",
      qualifier: "implementer, retired 03 Sep",
      model: "claude-sonnet-5 · balanced",
      usd: 4.97,
      per_amendment: 1.66,
      first_try: "62%",
      budget: "—",
    },
    {
      member: "reviewer",
      qualifier: "agent server",
      model: "claude-opus-5 · quick",
      usd: 0.69,
      per_amendment: 0.09,
      first_try: "—",
      budget: "no cap",
    },
    {
      member: "router",
      model: "claude-haiku-5 · quick",
      usd: 2.02,
      per_amendment: 0.25,
      first_try: "—",
      budget: "112% used",
      over_budget: true,
    },
  ],
  by_member_note:
    "Wren cost twice as much per amendment as Hollis on the same model family, because it retried more often. That comparison is why this screen exists.",
  trend: [
    { day: "1 Sep", usd: 1.9, cached: 0.62, uncached: 1.28 },
    { day: "2 Sep", usd: 2.4, cached: 0.9, uncached: 1.5 },
    { day: "3 Sep", usd: 1.72, cached: 0.58, uncached: 1.14 },
    { day: "4 Sep", usd: 4.96, cached: 2.1, uncached: 2.86 },
    { day: "5 Sep", usd: 7.42, cached: 3.4, uncached: 4.02 },
  ],
  trend_note: "Two batches ran on 4–5 Sep, which is the whole of the rise.",
  totals: { usd: 18.4, tokens: "2.0M", calls: 150, retried: 12 },
};

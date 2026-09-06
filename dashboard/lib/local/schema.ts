/**
 * The rows the dashboard reads, as ADR-0031 syncs them.
 *
 * This file is the *shape* of the local SQLite mirror, not a copy of the
 * server's schema: it names only the tables ADR-0031 lists as synced, and
 * only the columns a screen actually renders. The wire types it builds on
 * live in `@dark-factory/ui` and are shared with the components, so a
 * screen cannot drift from what the design system knows how to draw.
 *
 * Everything here is snake_case, because that is what the sync layer
 * delivers — see DESIGN.md § Wire shapes for why the two conventions meet
 * at this seam rather than somewhere less visible.
 */

import type {
  Amendment,
  Approval,
  Batch,
  Payload,
  Server,
  SpecNode,
  StageTimelineData,
  SyncState,
  TeamMember,
  TokenCost,
} from "@dark-factory/ui";

/** `projects`. `layers` is derived from the graph, not a stored column. */
export interface ProjectRow {
  id: string;
  name: string;
  org: string;
  node_count: number;
  snapshot_id: string;
  layers: string[];
  /** Layers a standards server declared rather than the built-in five. */
  declared_layers?: string[];
  active_runs: number;
  parked_runs: number;
  awaiting_amendments: number;
  deployable_batches: number;
  spend_usd: number;
  /** No workspace server authorized yet — the factory cannot reach code. */
  unconnected?: boolean;
}

/** `conversations`. */
export interface ConversationRow {
  id: string;
  project_id: string;
  title: string;
  /** The one-line state shown in the list: "1 amendment awaiting", etc. */
  subtitle: string;
  updated_at: string;
  turn_count: number;
  deployment: string;
  snapshot_id: string;
}

/**
 * `turns`. A turn is an author plus an ordered list of ADR-0021 payloads —
 * the whole reason the vocabulary exists is that a turn does not know what
 * it will need to render.
 */
export interface TurnRow {
  id: string;
  conversation_id: string;
  seq: number;
  author: "human" | "agent" | "run";
  author_name: string;
  /** The role-named deployment (ADR-0027), never a vendor model id. */
  deployment?: string;
  at: string;
  payloads: Payload[];
  /** Set while an optimistic local write has not been acknowledged. */
  pending?: boolean;
  /** A run posted this turn because it parked on a question (ADR-0029). */
  run_id?: string;
  batch_label?: string;
  cost?: TokenCost;
  /** Fractional change against the previous turn, for the cost strip. */
  cost_delta?: number | null;
  cost_series?: number[];
}

/** What the architect retrieved for a turn — the "Looking at" panel. */
export interface RetrievalRow {
  turn_id: string;
  neighbourhood: { spec_id: string; layer: string; text: string; drifted?: boolean }[];
  node_total: number;
  standards: { name: string; health: string; detail: string }[];
  standards_note?: string;
  cost: TokenCost;
  cache_hit_rate: number;
  proposer: string;
  deployment: string;
  at: string;
  skill_revisions: { name: string; revision: string }[];
}

/** `amendments`, joined to the approval it is waiting on. */
export interface AmendmentRow extends Amendment {
  project_id: string;
  conversation_id?: string;
  conversation_title?: string;
  turn_seq?: number;
  snapshot_id?: string;
  proposer?: string;
  deployment?: string;
  /** The `spec_diff` payload this amendment renders as. */
  payload?: Payload;
  approval?: Approval;
  /** Set on a rejected amendment. */
  rejected_reason?: string;
  conflict_count?: number;
}

/** `batches` and `batch_items`, plus the roll-ups the screen shows. */
export interface BatchRow extends Batch {
  project_id: string;
  label: string;
  spend_usd: number;
  budget_usd?: number;
  /** Why deploy is unavailable. Absent means it is available. */
  blocked_reason?: string;
  note?: string;
  run_count?: number;
  deployed_note?: string;
}

/** `runs` + `stages`, as the run panel draws them. */
export interface RunRow extends StageTimelineData {
  id: string;
  project_id: string;
  batch_id: string;
  batch_label: string;
  team_revision: number;
  attached: boolean;
}

/** `events` for a run — the live activity list. */
export interface EventRow {
  id: string;
  run_id: string;
  at: string;
  command: string;
  detail?: string;
  outcome?: string;
  /** Still in flight, rendered with a trailing ellipsis rather than a result. */
  streaming?: boolean;
}

/** `spec_nodes` as the graph browser lists them. */
export interface SpecNodeRow extends SpecNode {
  project_id: string;
  /** Set when reconcile found the code no longer matches (ADR-0024). */
  drift_detail?: string;
  /** No `implements` edge points at any code. */
  unimplemented?: boolean;
  retired_label?: string;
  implemented_by?: { path: string; status: string }[];
  edge_summary?: { kind: string; text: string }[];
}

/** Graph-wide counts for the spec screen's rail. */
export interface GraphSummaryRow {
  project_id: string;
  node_count: number;
  edge_count: number;
  layers: { layer: string; count: number }[];
  edge_kinds: { kind: string; count: number }[];
  drifted: number;
  unimplemented: number;
  snapshot_id: string;
}

/** `spec_snapshots` compared pairwise — the Compare… view. */
export interface SnapshotDiffRow {
  project_id: string;
  from: { id: string; at: string; note: string };
  to: { id: string; label: string };
  summary: string;
  changes: { kind: "added" | "changed" | "removed"; spec_id: string; layer: string; text: string; was?: string }[];
}

/** `team_members`, plus the assignment map and the marketplace listing. */
export interface TeamRow {
  project_id: string;
  revision: number;
  members: TeamMember[];
  assignments: {
    stage: string;
    member: string;
    deployment: string;
    why: string;
    human?: boolean;
  }[];
  hireable: {
    name: string;
    author: string;
    role: string;
    price: string;
    blurb: string;
    model_family: string;
    speed: string;
    /** "Installed" and "On this team" are different facts. */
    state?: "installed" | "on_team";
  }[];
}

/** `servers`, grouped by the `df.*` domain they serve. */
export interface ServerGroupRow {
  project_id: string;
  domain: string;
  title: string;
  servers: Server[];
  /** Rendered when the group has no server at all. */
  empty?: { title: string; body: string; action: string };
}

/** `model_calls` rolled up — every figure on the usage screen is a query. */
export interface UsageRow {
  project_id: string;
  period: string;
  headline: {
    label: string;
    value: string;
    unit?: string;
    /**
     * Fractional change against the comparison window, or absent.
     *
     * A fraction, never a formatted string: cache hit rate moved by nine
     * percentage *points*, which is not nine percent, and storing "+9pp"
     * here would invite exactly that arithmetic. Anything that is not a
     * fraction goes in `note` as the words it actually is.
     */
    delta?: number;
    delta_is_good?: boolean;
    note?: string;
    /** The provider stopped reporting; a dash is the honest answer. */
    missing?: boolean;
  }[];
  by_stage: {
    stage: string;
    share: number;
    usd: number;
    tokens: string;
    calls: number;
    retried: number;
  }[];
  by_member: {
    member: string;
    qualifier?: string;
    avatar?: boolean;
    model: string;
    usd: number;
    per_amendment: number;
    first_try: string;
    budget: string;
    over_budget?: boolean;
  }[];
  by_member_note: string;
  trend: { day: string; usd: number; cached: number; uncached: number }[];
  trend_note: string;
  totals: { usd: number; tokens: string; calls: number; retried: number };
}

/** The header's live strip — what is happening across the project now. */
export interface TickerRow {
  id: string;
  project_id: string;
  status: string;
  label: string;
  detail: string;
  screen: ScreenName;
}

/** ADR-0031 makes the UI able to say what it is showing. */
export interface SyncRow {
  state: SyncState;
  /** Local writes not yet acknowledged by the factory. */
  pending: number;
}

export const SCREENS = [
  "conversation",
  "amendments",
  "specs",
  "batches",
  "team",
  "servers",
  "usage",
  "projects",
] as const;
export type ScreenName = (typeof SCREENS)[number];

/** The whole local mirror, as one object. PowerSync replaces the value. */
export interface LocalDb {
  sync: SyncRow;
  org: string;
  actor: { id: string; name: string; initials: string };
  /** Null when the org has no projects at all — there is nothing to be in. */
  project: ProjectRow | null;
  projects: ProjectRow[];
  ticker: TickerRow[];
  conversations: ConversationRow[];
  turns: TurnRow[];
  retrieval: RetrievalRow[];
  amendments: AmendmentRow[];
  batches: BatchRow[];
  runs: RunRow[];
  events: EventRow[];
  spec_nodes: SpecNodeRow[];
  graph: GraphSummaryRow;
  snapshot_diff: SnapshotDiffRow;
  team: TeamRow;
  server_groups: ServerGroupRow[];
  usage: UsageRow;
  /** Built-in connectors offered before anything is authorized (ADR-0019). */
  connectors: { id: string; name: string; domain: string; note: string }[];
  /**
   * The approval decision on the amendment currently in view.
   *
   * It is on the mirror rather than in a component because two screens
   * render the same approval — the conversation that produced it and the
   * amendments queue — and they have to agree about it.
   */
  decision: DecisionRow;
  /** The architect is mid-turn: the thread stops at the question. */
  thinking: boolean;
}

export type DecisionRow =
  | { state: "undecided" }
  | { state: "approved" }
  | { state: "rejected"; reason: string };

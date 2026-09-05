/**
 * Runs, batches, teams, servers and cost.
 *
 * Where a shape has a schema under `contracts/schemas/` the type follows it
 * exactly, snake_case included. Where it does not, the type is **proposed**
 * here and recorded in DESIGN.md so that writing the schema is a deliberate
 * act (ADR-0021's rule that the vocabulary grows on purpose) rather than a
 * transcription of whatever the first component happened to need.
 */

import type { StageStatus } from "../lib/vocabulary";

/** ADR-0003 as amended: a run is plan → implement → verify, and ship is a batch action (ADR-0029). */
export const RUN_STAGES = ["plan", "implement", "verify", "ship"] as const;
export type RunStage = (typeof RUN_STAGES)[number];

/** The member that owned a stage, from the run's team snapshot (ADR-0028). */
export interface StageOwner {
  team_member_id: string;
  role: string;
  /** The role-named deployment, never a vendor model id (ADR-0027). */
  deployment: string;
  /** Present once a persona is more than a nullable column (ADR-0028, deferred). */
  persona_name?: string;
  avatar_url?: string;
}

/** A typed artifact, by reference (ADR-0004). */
export interface ArtifactChip {
  /** `factory://artifacts/{id}` */
  ref: string;
  /** Plan | ChangeSet | TestReport | PrBody | Patch | ContextPack */
  type: string;
  label?: string;
}

/**
 * One stage of one run.
 *
 * **Proposed** as the `stage_timeline` payload (ADR-0021). The pieces exist
 * separately today — `stages`, `artifacts`, `model_calls` — but no schema
 * joins them, and the join is the thing worth rendering: who did it, what
 * came out, what it cost.
 */
export interface StageTimelineEntry {
  stage: RunStage;
  status: StageStatus;
  owner?: StageOwner;
  artifact?: ArtifactChip;
  /** Summed from `model_calls` for this stage (ADR-0032). */
  cost?: TokenCost;
  attempt?: number;
  started_at?: string;
  ended_at?: string;
  /**
   * Why a parked stage is parked: the question the agent stopped to ask.
   * A parked stage that cannot say what it is waiting for is a dead end,
   * so this is what makes `parked` different from `pending`.
   */
  question?: string;
}

/** Named `...Data` because `StageTimeline` is the component that draws it. */
export interface StageTimelineData {
  run_id: string;
  snapshot_id?: string;
  stages: StageTimelineEntry[];
}

/** A cost figure, in the terms `model_calls` records (ADR-0032). */
export interface TokenCost {
  input_tokens: number;
  /** A portion of input, billed differently — never a sum with it. */
  cached_input_tokens?: number;
  output_tokens: number;
  /** A portion of output, not an addition to it. */
  thinking_tokens?: number;
  /** USD. Null where the provider has not priced it. */
  usd?: number | null;
}

/**
 * **Proposed** as the `approval_card` payload. Approvers are configurable
 * per project (ADR-0017) and may be several, so "who else are we waiting
 * on" is part of the shape rather than an afterthought.
 */
export interface Approval {
  id: string;
  target_type: "amendment" | "gate";
  target_id: string;
  summary: string;
  status: "awaiting" | "approved" | "rejected";
  required_approvers: Approver[];
  reason?: string;
}

export interface Approver {
  id: string;
  name: string;
  avatar_url?: string;
  decision?: "approved" | "rejected";
  decided_at?: string;
  reason?: string;
}

/** An approved amendment waiting in the project backlog (ADR-0029). */
export interface Amendment {
  id: string;
  summary: string;
  status: "proposed" | "approved" | "rejected";
  /** How much of the graph it moves — creates + revises + retires + edges. */
  change_count: number;
  layers: string[];
  created_at?: string;
  /** Amendments it must follow, from `depends_on` traversal (ADR-0029). */
  depends_on?: string[];
  /** Set when a dependency is not yet in this batch, or not yet approved. */
  blocked_by?: string;
}

export const BATCH_STATUSES = [
  "composing",
  "running",
  "blocked_by_verify",
  "deployable",
  "deployed",
] as const;
export type BatchStatus = (typeof BATCH_STATUSES)[number];

/**
 * An ordered set of amendments executed as a sequence (ADR-0029).
 *
 * `deploy` is a batch action, not a run action, which is why the deploy
 * control belongs here and nowhere on a run.
 */
export interface Batch {
  id: string;
  name?: string;
  status: BatchStatus;
  items: BatchItem[];
  deployed_at?: string | null;
}

export interface BatchItem {
  seq: number;
  amendment_id: string;
  summary: string;
  run_id?: string | null;
  status: StageStatus;
}

/** ADR-0028's speed preset, mapping to the gateway's thinking budget. */
export const SPEED_PRESETS = ["quick", "balanced", "deliberate"] as const;
export type SpeedPreset = (typeof SPEED_PRESETS)[number];

/** A versioned instruction bundle, held centrally (ADR-0028 amendment). */
export interface SkillRef {
  name: string;
  revision: string;
}

/** A member of the project's standing team (ADR-0028). */
export interface TeamMember {
  id: string;
  role: string;
  /** Native agents run in-process; agent servers are external MCP servers. */
  kind: "native" | "agent_server" | "persona";
  deployment: string;
  /**
   * The model behind the deployment. A persona card is required to state
   * it (ADR-0028), because "which model am I buying" is the question a
   * price makes people ask.
   */
  model_family?: string;
  speed?: SpeedPreset;
  skills?: SkillRef[];
  /** Null means no explicit cap; the org default applies. */
  token_budget?: number | null;
  /** Summed from `model_calls`, not a separate ledger (ADR-0032). */
  spend?: TokenCost;
  persona?: Persona;
  avatar_url?: string;
}

/** A team member sold under a name and a portrait (ADR-0028, deferred). */
export interface Persona {
  id: string;
  name: string;
  author: string;
  description?: string;
  /** USD per month. Null or absent means free. */
  price_usd?: number | null;
  installed?: boolean;
  avatar_url?: string;
}

export const SERVER_TIERS = ["built_in", "premium", "community"] as const;
export type ServerTier = (typeof SERVER_TIERS)[number];

/** A registered server, from `df.describe` (ADR-0018, describe.schema.json). */
export interface ServerCapability {
  name: string;
  /** From the registration conformance check. */
  status?: "passed" | "failed" | "skipped";
  detail?: string;
}

export interface Server {
  id: string;
  name: string;
  domain: string;
  tier: ServerTier;
  convention_version: string;
  capabilities: ServerCapability[];
  /** What this instance is pointed at. Never secrets — it is rendered. */
  effective_config?: Record<string, string>;
  health: "conformant" | "degraded" | "unreachable";
  /** Built-in connectors are authorised in the UI rather than registered by URL (ADR-0019). */
  built_in_connector?: boolean;
  authorized?: boolean;
  last_conformance_at?: string;
}

/**
 * **Proposed** as the `metric` payload. A number with no comparison is a
 * number nobody can act on, so the delta and the series are part of the
 * shape rather than optional decoration.
 */
export interface Metric {
  label: string;
  value: number | string;
  unit?: string;
  /** Fractional change against the comparison window. Null when unknown. */
  delta?: number | null;
  /** Whether an increase is good. Cost going up is not the same as throughput going up. */
  delta_is_good?: boolean;
  series?: number[];
  period?: string;
}

/** One band of a stacked cost bar. */
export interface CostSegment {
  label: string;
  tokens: number;
  usd?: number | null;
  /** Renders in the over-budget token regardless of size. */
  over_budget?: boolean;
}

/** ADR-0031: local-first, so the UI has to be able to say what it is showing. */
export const SYNC_STATES = ["synced", "syncing", "offline", "conflict"] as const;
export type SyncState = (typeof SYNC_STATES)[number];

/** Every amendment carries provenance (ADR-0016). This is it, for display. */
export interface Provenance {
  conversation_id?: string;
  conversation_title?: string;
  turn_id?: string;
  proposer: { name: string; type: "human" | "agent" };
  approver?: { name: string; at?: string };
  /** Pinned revisions the run actually had, not the library's current ones. */
  skill_revisions?: SkillRef[];
  model_family?: string;
  deployment?: string;
  at?: string;
}

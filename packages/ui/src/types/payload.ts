/**
 * ADR-0021's rendering vocabulary, as it appears on the wire.
 *
 * Ten component types, of which `spec_diff` and `decision` (ADR-0041) are
 * the ones with a schema under `contracts/schemas/` today. The rest are typed
 * here and marked **proposed** so that writing their schemas stays a
 * deliberate act.
 *
 * The wire is snake_case throughout, envelope included, matching every
 * schema in `contracts/schemas/`. It briefly was not: the envelope
 * serialised as camelCase by inheriting the SDK's default handling of C#
 * records, and every payload carried two discriminators (`$type` from the
 * serializer and `type` from ADR-0021) that agreed only by luck. Both were
 * fixed once the vocabulary still had two members rather than nine. See
 * DESIGN.md § Wire shapes for why that is recorded rather than forgotten.
 */

import type { SpecConflict, SpecDiffDocument } from "./spec";
import type { Approval, CostSegment, Metric, StageTimelineData } from "./run";

export const PAYLOAD_TYPES = [
  "markdown",
  "spec_diff",
  "dependency_graph",
  "stage_timeline",
  "approval_card",
  "table",
  "code_diff",
  "form",
  "metric",
  "decision",
] as const;
export type PayloadType = (typeof PAYLOAD_TYPES)[number];

interface PayloadBase {
  /** The serializer's discriminator and ADR-0021's component type, one field. */
  type: PayloadType;
}

export interface MarkdownPayload extends PayloadBase {
  type: "markdown";
  text: string;
}

export interface SpecDiffPayload extends PayloadBase {
  type: "spec_diff";
  amendment_id: string;
  diff: SpecDiffDocument;
  summary: string;
  /** Proposed — see SpecConflict. Absent means "none detected", not "none". */
  conflicts?: SpecConflict[];
}

/** A node in the small graph view. Proposed. */
export interface GraphNode {
  spec_id: string;
  layer: string;
  label: string;
  retired?: boolean;
}

export interface GraphEdge {
  from: string;
  to: string;
  kind: string;
}

export interface DependencyGraphPayload extends PayloadBase {
  type: "dependency_graph";
  nodes: GraphNode[];
  edges: GraphEdge[];
  /** The node the neighbourhood was traversed from, if any. */
  focus?: string;
}

export interface StageTimelinePayload extends PayloadBase, StageTimelineData {
  type: "stage_timeline";
}

export interface ApprovalCardPayload extends PayloadBase {
  type: "approval_card";
  approval: Approval;
}

export interface TablePayload extends PayloadBase {
  type: "table";
  columns: { key: string; label: string; numeric?: boolean }[];
  rows: Record<string, string | number | null>[];
  caption?: string;
}

export interface CodeDiffPayload extends PayloadBase {
  type: "code_diff";
  /** A unified diff, as the factory generated it (never model-authored). */
  patch: string;
  /** Gutter annotations: line number → the spec id that line implements. */
  spec_annotations?: Record<number, string>;
  files_changed?: string[];
}

export interface FormField {
  name: string;
  label: string;
  kind: "text" | "textarea" | "select" | "switch";
  options?: { value: string; label: string }[];
  value?: string | boolean;
  required?: boolean;
  help?: string;
}

export interface FormPayload extends PayloadBase {
  type: "form";
  title?: string;
  fields: FormField[];
  submit_label?: string;
}

export interface MetricPayload extends PayloadBase, Metric {
  type: "metric";
}

/**
 * One path open to a person deciding. `contracts/schemas/decision.schema.json`.
 * A recommended option is the proposer's view, labelled as such — it is never
 * preselected, and choosing it is still recorded as the person's decision.
 */
export interface DecisionOption {
  id: string;
  /** Short enough for a button. */
  label: string;
  /** What follows from choosing it: the specification that results, what it rules out. */
  consequence?: string;
  /** At most one per decision. */
  recommended?: boolean;
}

/**
 * Something a person has to decide, with the paths open to them (ADR-0041).
 * Schema-backed: `contracts/schemas/decision.schema.json`.
 */
export interface Decision {
  /** Stable within its source: an intake question id, or one the proposer chose. */
  id: string;
  /** The decision, as one question a person can answer. */
  title: string;
  /** Why it needs deciding now: what goes wrong if nobody does. */
  why?: string;
  /** The document or node it is about, when there is one. */
  source_ref?: string;
  /** What sort of decision — for an intake question, its hole kind. */
  kind?: string;
  /** Two to four. */
  options: DecisionOption[];
  /** Whether answering in one's own words is allowed. Absent means true. */
  allow_other?: boolean;
  /** Whether it may be left open on purpose. Absent means true. */
  allow_defer?: boolean;
}

export interface DecisionPayload extends PayloadBase, Decision {
  type: "decision";
}

export type Payload =
  | MarkdownPayload
  | SpecDiffPayload
  | DependencyGraphPayload
  | StageTimelinePayload
  | ApprovalCardPayload
  | TablePayload
  | CodeDiffPayload
  | FormPayload
  | MetricPayload
  | DecisionPayload;

/** A cost breakdown for CostBar. Not a payload type — a component input. */
export type CostBreakdown = CostSegment[];

/**
 * ADR-0021's rendering vocabulary, as it appears on the wire.
 *
 * Nine component types, of which `spec_diff` is the only one with a schema
 * under `contracts/schemas/` today. The rest are typed here and marked
 * **proposed** so that writing their schemas stays a deliberate act.
 *
 * The wire is snake_case throughout, envelope included, matching all eight
 * schemas in `contracts/schemas/`. It briefly was not: the envelope
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

export type Payload =
  | MarkdownPayload
  | SpecDiffPayload
  | DependencyGraphPayload
  | StageTimelinePayload
  | ApprovalCardPayload
  | TablePayload
  | CodeDiffPayload
  | FormPayload
  | MetricPayload;

/** A cost breakdown for CostBar. Not a payload type — a component input. */
export type CostBreakdown = CostSegment[];

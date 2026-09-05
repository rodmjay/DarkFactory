/**
 * The class strings for the four token families that are keyed by a value
 * coming out of the database rather than chosen by a developer: stage
 * status, spec layer, diff kind, server health.
 *
 * They are written out in full rather than interpolated (`bg-status-${s}-fill`)
 * because Tailwind scans source text: a class name that only exists after a
 * template literal is evaluated is a class name Tailwind never generates.
 */

export const STAGE_STATUSES = [
  "pending",
  "running",
  "passed",
  "parked",
  "failed",
  "over-budget",
] as const;
export type StageStatus = (typeof STAGE_STATUSES)[number];

/** The five layers the built-in standards ship with. `layer` is an open
 *  string in specdiff.schema.json — a customer's standards server declares
 *  its own — so anything unrecognised renders as `other`. */
export const SPEC_LAYERS = ["product", "api", "data", "infra", "ui"] as const;
export type SpecLayer = (typeof SPEC_LAYERS)[number];
export type LayerKey = SpecLayer | "other";

export const DIFF_KINDS = ["added", "removed", "changed", "conflict"] as const;
export type DiffKind = (typeof DIFF_KINDS)[number];

export const SERVER_HEALTHS = ["conformant", "degraded", "unreachable"] as const;
export type ServerHealth = (typeof SERVER_HEALTHS)[number];

export const statusChip: Record<StageStatus, string> = {
  pending: "bg-status-pending-fill border-status-pending-border text-status-pending-text",
  running: "bg-status-running-fill border-status-running-border text-status-running-text",
  passed: "bg-status-passed-fill border-status-passed-border text-status-passed-text",
  parked: "bg-status-parked-fill border-status-parked-border text-status-parked-text",
  failed: "bg-status-failed-fill border-status-failed-border text-status-failed-text",
  "over-budget":
    "bg-status-over-budget-fill border-status-over-budget-border text-status-over-budget-text",
};

export const statusDot: Record<StageStatus, string> = {
  pending: "bg-status-pending-border",
  running: "bg-status-running-text",
  passed: "bg-status-passed-text",
  parked: "bg-status-parked-text",
  failed: "bg-status-failed-text",
  "over-budget": "bg-status-over-budget-text",
};

/** The two states that mean a person is required. The accent exists for
 *  exactly this (ADR-0033), which is why both live in its hue family. */
export const NEEDS_YOU: readonly StageStatus[] = ["parked", "over-budget"];

export function needsYou(status: StageStatus): boolean {
  return NEEDS_YOU.includes(status);
}

export const layerChip: Record<LayerKey, string> = {
  product: "bg-layer-product-fill border-layer-product-border text-layer-product-text",
  api: "bg-layer-api-fill border-layer-api-border text-layer-api-text",
  data: "bg-layer-data-fill border-layer-data-border text-layer-data-text",
  infra: "bg-layer-infra-fill border-layer-infra-border text-layer-infra-text",
  ui: "bg-layer-ui-fill border-layer-ui-border text-layer-ui-text",
  other: "bg-layer-other-fill border-layer-other-border text-layer-other-text",
};

export function layerKey(layer: string): LayerKey {
  return (SPEC_LAYERS as readonly string[]).includes(layer) ? (layer as SpecLayer) : "other";
}

export const diffBand: Record<DiffKind, string> = {
  added: "bg-diff-added-fill border-diff-added-border text-diff-added-text",
  removed: "bg-diff-removed-fill border-diff-removed-border text-diff-removed-text",
  changed: "bg-diff-changed-fill border-diff-changed-border text-diff-changed-text",
  conflict: "bg-diff-conflict-fill border-diff-conflict-border text-diff-conflict-text",
};

export const diffMarker: Record<DiffKind, string> = {
  added: "text-diff-added-marker",
  removed: "text-diff-removed-marker",
  changed: "text-diff-changed-marker",
  conflict: "text-diff-conflict-marker",
};

export const serverChip: Record<ServerHealth, string> = {
  conformant:
    "bg-server-conformant-fill border-server-conformant-border text-server-conformant-text",
  degraded: "bg-server-degraded-fill border-server-degraded-border text-server-degraded-text",
  unreachable:
    "bg-server-unreachable-fill border-server-unreachable-border text-server-unreachable-text",
};

export const STATUS_LABEL: Record<StageStatus, string> = {
  pending: "Pending",
  running: "Running",
  passed: "Passed",
  parked: "Parked",
  failed: "Failed",
  "over-budget": "Over budget",
};

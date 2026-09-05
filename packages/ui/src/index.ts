/**
 * @dark-factory/ui — the Dark Factory design system (ADR-0033).
 *
 * The package holds visual code and nothing else: no data fetching, no
 * routing, no app state. Components take props; the app wires them.
 */

export { cn } from "./lib/cn";
export { FOCUS_RING, FOCUS_RING_INSET, DISABLED } from "./lib/styles";
export * from "./lib/vocabulary";
export * as format from "./lib/format";

// ---- types: the wire shapes, and the ones proposed for schemas ----
export type * from "./types/spec";
export type * from "./types/run";
export type * from "./types/payload";
export { SPEC_KINDS, EDGE_KINDS } from "./types/spec";
export { RUN_STAGES, BATCH_STATUSES, SPEED_PRESETS, SERVER_TIERS, SYNC_STATES } from "./types/run";
export { PAYLOAD_TYPES } from "./types/payload";

// ---- shadcn base set, restyled onto the tokens ----
export * from "./components/ui/avatar";
export * from "./components/ui/badge";
export * from "./components/ui/button";
export * from "./components/ui/card";
export * from "./components/ui/command";
export * from "./components/ui/dialog";
export * from "./components/ui/dropdown-menu";
export * from "./components/ui/input";
export * from "./components/ui/label";
export * from "./components/ui/popover";
export * from "./components/ui/scroll-area";
export * from "./components/ui/select";
export * from "./components/ui/separator";
export * from "./components/ui/sheet";
export * from "./components/ui/skeleton";
export * from "./components/ui/switch";
export * from "./components/ui/table";
export * from "./components/ui/tabs";
export * from "./components/ui/textarea";
export * from "./components/ui/toast";
export * from "./components/ui/tooltip";

// ---- domain ----
export * from "./components/domain/amendment-row";
export * from "./components/domain/approval-card";
export * from "./components/domain/batch-card";
export * from "./components/domain/code-diff";
export * from "./components/domain/cost-bar";
export * from "./components/domain/dependency-graph";
export * from "./components/domain/layer-badge";
export * from "./components/domain/metric-tile";
export * from "./components/domain/payload-renderer";
export * from "./components/domain/persona-card";
export * from "./components/domain/provenance-popover";
export * from "./components/domain/server-card";
export * from "./components/domain/spec-diff";
export * from "./components/domain/spec-id";
export * from "./components/domain/spec-node-card";
export * from "./components/domain/stage-timeline";
export * from "./components/domain/status-chip";
export * from "./components/domain/sync-status";
export * from "./components/domain/team-member-card";

/**
 * The spec graph on the wire.
 *
 * `SpecDiffDocument` and everything under it is typed from
 * `contracts/schemas/specdiff.schema.json` — a published contract, so these
 * names are snake_case and stay that way. The envelope around it
 * (`SpecDiffPayload`) is camelCase, because it is serialised by convention
 * rather than by schema. That the two conventions meet inside one object is
 * a real wire seam, not a mistake in this file; see DESIGN.md § Wire shapes.
 */

/** ADR-0016's node kinds. Deliberately closed: an open set becomes synonym soup. */
export const SPEC_KINDS = [
  "rule",
  "behavior",
  "constraint",
  "decision",
  "entity",
  "interface",
] as const;
export type SpecKind = (typeof SPEC_KINDS)[number];

/** ADR-0016's typed edges. Plugin kinds are added through the convention process. */
export const EDGE_KINDS = [
  "depends_on",
  "conflicts_with",
  "supersedes",
  "implements",
  "constrains",
] as const;
export type EdgeKind = (typeof EDGE_KINDS)[number];

export interface SpecDiffCreate {
  kind: SpecKind;
  /** Open string — a customer's standards server declares its own layers. */
  layer: string;
  text: string;
  rationale?: string | null;
}

export interface SpecDiffRevise {
  spec_id: string;
  text: string;
  rationale?: string | null;
}

export interface SpecDiffRetire {
  spec_id: string;
  rationale?: string | null;
}

export interface SpecDiffEdgeAdd {
  /** A ULID, or `new:N` referring to `creates[N]` in this same diff. */
  from_spec_id: string;
  to_spec_id: string;
  kind: EdgeKind | string;
  rationale?: string | null;
}

export interface SpecDiffEdgeRetire {
  edge_id: string;
  rationale?: string | null;
}

export interface SpecDiffDocument {
  creates: SpecDiffCreate[];
  revises: SpecDiffRevise[];
  retires: SpecDiffRetire[];
  edge_adds: SpecDiffEdgeAdd[];
  edge_retires: SpecDiffEdgeRetire[];
}

/**
 * A conflict the factory detected between this diff and the existing graph.
 *
 * **Proposed.** `specdiff.schema.json` carries no conflict list — today a
 * conflict is expressed as an `edge_add` of kind `conflicts_with`, which
 * says two nodes disagree but not what the disagreement is or when the
 * other decision was made. Rendering "this contradicts the rule you set in
 * March" needs the sentence and the date, so the component takes them; if
 * the shape is adopted it belongs in the schema.
 */
export interface SpecConflict {
  /** The existing node this diff contradicts. */
  spec_id: string;
  /** The existing node's text, so the reader does not have to go and look. */
  text: string;
  /** Why the factory believes these disagree. */
  detail: string;
  /** When the contradicted decision was made. ISO-8601. */
  decided_at?: string;
}

/** A node as the graph holds it, for `SpecNodeCard`. */
export interface SpecNode {
  spec_id: string;
  kind: SpecKind;
  layer: string;
  text: string;
  /** SHA-256 of the canonical content (ADR-0016). */
  revision_hash?: string;
  retired_at?: string | null;
  edges?: { outgoing: number; incoming: number };
  /**
   * Code no longer matches the spec (ADR-0024). Computable rather than
   * prevented, so it is a state a node can be in rather than an error.
   */
  drifted?: boolean;
}

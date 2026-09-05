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

/**
 * Why a change was made — `rationale` throughout this file.
 *
 * **Optional, never null.** Serialization drops nulls, so a change with no
 * reason arrives as an *absent key* rather than `"rationale": null`. Three
 * states are distinguishable on the wire and must not be collapsed:
 *
 *   absent            nobody gave a reason
 *   ""                someone gave an empty one — the schema permits it
 *                     (maxLength 2000, no minLength)
 *   "…"               a reason
 *
 * `rationaleState()` is the one place that decides which is which, so the
 * distinction cannot quietly rot into `if (rationale)`, which would fold the
 * middle case into the first.
 *
 * Rationale is deliberately **not** part of the text a revision is hashed
 * from: two people can agree on a rule and disagree about why, and that must
 * not fork the revision. A revision whose reason changed but whose text did
 * not is therefore not a new revision, and no history will ever show one.
 */
export interface SpecDiffCreate {
  kind: SpecKind;
  /** Open string — a customer's standards server declares its own layers. */
  layer: string;
  text: string;
  rationale?: string;
}

export interface SpecDiffRevise {
  spec_id: string;
  text: string;
  rationale?: string;
}

export interface SpecDiffRetire {
  spec_id: string;
  rationale?: string;
}

export interface SpecDiffEdgeAdd {
  /** A ULID, or `new:N` referring to `creates[N]` in this same diff. */
  from_spec_id: string;
  to_spec_id: string;
  kind: EdgeKind | string;
  rationale?: string;
}

export interface SpecDiffEdgeRetire {
  edge_id: string;
  rationale?: string;
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
 * **Proposed — a projection, not a new shape.**
 *
 * A conflict is an `edge_add` of kind `conflicts_with`, and rendering "this
 * contradicts the rule you set in March" needs the sentence and the date
 * that a bare edge does not carry.
 *
 * Proposing this turned up a provenance bug rather than a modelling gap:
 * `rationale` is on every element of `specdiff.schema.json` and the model
 * answers it, but none of it could be read back — buried inside
 * `ContentJson` on creates and revises, dropped at translation on retires
 * and both edge kinds. Fixed in `bc2834e`, with a backfill and an ADR-0016
 * amendment: an amendment now records *why* per change, not only who and
 * when.
 *
 * So the sentence is persisted and rendered, and the date comes from the
 * contradicted node's revision provenance. What is left for this type is
 * joining the two, which is why it stays proposed rather than urgent.
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

/**
 * One immutable revision of a node, as `df.specs.get` returns them —
 * oldest first.
 *
 * `actor_id` and `approved_by` are separate fields and are not collapsed,
 * even though they hold the same value today. Approval is single-actor now;
 * ADR-0017 makes approvers configurable per project, so the day they differ
 * is the day the distinction matters most, and a card that had merged them
 * would be silently wrong rather than newly wrong.
 *
 * Every optional field here is absent-when-unset, not null — same
 * serialization rule as `rationale`.
 */
export interface SpecRevision {
  /** SHA-256 of the canonical content. Rationale is not part of it. */
  hash: string;
  text: string;
  rationale?: string;
  created_at: string;
  /** Who proposed it. */
  actor_id?: string;
  /** Who approved it. Same as the proposer today; not always. */
  approved_by?: string;
  conversation_id?: string;
  turn_id?: string;
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
  /** From `df.specs.get`, oldest first. Absent when only the node was fetched. */
  revisions?: SpecRevision[];
}

/** What a `rationale` field is actually saying. */
export type RationaleState = "absent" | "blank" | "given";

/**
 * The one place the three states are told apart.
 *
 * `if (rationale)` folds `""` into "nobody said why", which is a different
 * fact: somebody was asked and left it empty. That is worth showing
 * differently, because the fix is different — one is a missing prompt, the
 * other is a person to go and ask.
 */
export function rationaleState(rationale: string | undefined | null): RationaleState {
  if (rationale === undefined || rationale === null) return "absent";
  return rationale.trim().length === 0 ? "blank" : "given";
}

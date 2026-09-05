import * as React from "react";
import { TriangleAlertIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { at } from "../../lib/format";
import { diffBand, diffMarker } from "../../lib/vocabulary";
import type { SpecConflict, SpecDiffDocument } from "../../types/spec";
import { LayerBadge } from "./layer-badge";
import { Rationale } from "./rationale";
import { SpecId } from "./spec-id";

export interface SpecDiffProps extends React.ComponentProps<"div"> {
  diff: SpecDiffDocument;
  /** Detected against the existing graph. Absent means none were detected. */
  conflicts?: SpecConflict[];
  summary?: string;
}

const MARKS = { created: "+", revised: "~", retired: "−" } as const;

/**
 * An amendment as a diff against the graph (ADR-0017).
 *
 * Conflicts come first and are the loudest thing the system draws. "This
 * contradicts the rule you set in March" is the single most valuable
 * sentence the product says, and burying it under a list of additions —
 * where a reader who is scanning for what is new will scroll straight past
 * it — would waste the one thing a spec graph knows that a person does not.
 *
 * Edge changes are shown separately from node changes because they are a
 * different kind of claim: a node change says what is true, an edge change
 * says what depends on what, and conflating them makes the ordering
 * decisions in ADR-0029 impossible to review.
 */
export function SpecDiff({ diff, conflicts = [], summary, className, ...props }: SpecDiffProps) {
  const empty =
    diff.creates.length === 0 &&
    diff.revises.length === 0 &&
    diff.retires.length === 0 &&
    diff.edge_adds.length === 0 &&
    diff.edge_retires.length === 0;

  return (
    <div data-slot="spec-diff" className={cn("flex flex-col gap-3", className)} {...props}>
      {summary && <p className="text-sm text-secondary">{summary}</p>}

      {conflicts.length > 0 && (
        <section
          aria-label="Conflicts"
          className={cn(
            "flex flex-col gap-2 rounded-card border-2 px-3.5 py-3",
            diffBand.conflict,
          )}
        >
          <h4 className="flex items-center gap-1.5 text-2xs font-semibold tracking-wide uppercase">
            <TriangleAlertIcon className={cn("size-3.5", diffMarker.conflict)} aria-hidden />
            {conflicts.length} conflict{conflicts.length === 1 ? "" : "s"}
          </h4>
          <ul className="flex flex-col gap-2">
            {conflicts.map((conflict) => (
              <li key={conflict.spec_id} className="flex flex-col gap-1">
                <div className="flex flex-wrap items-center gap-1.5 text-2xs">
                  <SpecId id={conflict.spec_id} />
                  {conflict.decided_at && (
                    <span className="opacity-80">decided {at(conflict.decided_at)}</span>
                  )}
                </div>
                <p className="text-sm text-primary">{conflict.text}</p>
                <p className="text-xs opacity-90">{conflict.detail}</p>
              </li>
            ))}
          </ul>
        </section>
      )}

      {empty ? (
        <p className="rounded-card border border-dashed border-border px-3.5 py-6 text-center text-sm text-muted">
          This amendment changes nothing. Nothing to approve.
        </p>
      ) : (
        <div className="overflow-hidden rounded-card border border-border">
          {diff.creates.map((create, index) => (
            <Row
              key={`create-${index}`}
              kind="created"
              // The factory assigns ids at approval time, so a created node
              // has none yet — `new:N` is how the diff refers to it, and
              // showing that is more honest than showing a blank.
              id={`new:${index}`}
              layer={create.layer}
              badge={create.kind}
              text={create.text}
              rationale={create.rationale}
            />
          ))}
          {diff.revises.map((revise) => (
            <Row
              key={`revise-${revise.spec_id}`}
              kind="revised"
              id={revise.spec_id}
              text={revise.text}
              rationale={revise.rationale}
            />
          ))}
          {diff.retires.map((retire) => (
            <Row
              key={`retire-${retire.spec_id}`}
              kind="retired"
              id={retire.spec_id}
              text="Retired."
              rationale={retire.rationale}
            />
          ))}

          {(diff.edge_adds.length > 0 || diff.edge_retires.length > 0) && (
            <div className="border-t border-border bg-sunken px-3 py-2">
              <h4 className="text-2xs font-medium tracking-wide text-muted uppercase">
                Edges
              </h4>
              <ul className="mt-1.5 flex flex-col gap-2">
                {diff.edge_adds.map((edge, index) => (
                  <li key={`add-${index}`} className="flex flex-col gap-0.5">
                    <span className="flex items-baseline gap-1.5 font-mono text-2xs">
                      <span className={cn("w-3 shrink-0 font-semibold", diffMarker.added)}>+</span>
                      <span className="text-secondary">
                        <Endpoint reference={edge.from_spec_id} diff={diff} />{" "}
                        <span className="text-muted">{edge.kind}</span>{" "}
                        <Endpoint reference={edge.to_spec_id} diff={diff} />
                      </span>
                    </span>
                    <Rationale rationale={edge.rationale} className="pl-[1.125rem]" />
                  </li>
                ))}
                {diff.edge_retires.map((edge) => (
                  <li key={`retire-${edge.edge_id}`} className="flex flex-col gap-0.5">
                    <span className="flex items-baseline gap-1.5 font-mono text-2xs">
                      <span className={cn("w-3 shrink-0 font-semibold", diffMarker.removed)}>−</span>
                      <span className="text-secondary line-through">{edge.edge_id}</span>
                    </span>
                    <Rationale rationale={edge.rationale} className="pl-[1.125rem]" />
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * An edge endpoint. `new:N` is a forward reference to `creates[N]` in this
 * same diff — a node that has no id until approval assigns one.
 *
 * Rendered as the node it points at rather than as the literal, because
 * "new:0 depends_on new:1" is not a sentence anyone can read, and the
 * ordering decisions in ADR-0029 are made by reading exactly these lines.
 */
function Endpoint({ reference, diff }: { reference: string; diff: SpecDiffDocument }) {
  const match = /^new:(\d+)$/.exec(reference);
  if (!match) return <>{reference}</>;

  const created = diff.creates[Number(match[1])];
  if (!created) {
    // A reference past the end of `creates` is a malformed diff, and saying
    // so is more use than rendering a dangling literal.
    return (
      <span className={diffMarker.conflict} title="This diff has no such created node.">
        {reference} (unresolved)
      </span>
    );
  }

  return (
    <span
      className="text-primary"
      title={`${reference} — a node this amendment creates: ${created.text}`}
    >
      “{truncate(created.text, 34)}”
      <span className="text-muted"> (new)</span>
    </span>
  );
}

function truncate(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max - 1)}…` : text;
}

function Row({
  kind,
  id,
  layer,
  badge,
  text,
  rationale,
}: {
  kind: keyof typeof MARKS;
  id: string;
  layer?: string;
  badge?: string;
  text: string;
  rationale?: string;
}) {
  const band = kind === "created" ? "added" : kind === "retired" ? "removed" : "changed";

  return (
    <div
      className={cn(
        "flex gap-3 border-b border-border px-3 py-2.5 last:border-b-0",
        diffBand[band],
      )}
    >
      <span className={cn("w-3 shrink-0 text-center font-mono font-semibold", diffMarker[band])}>
        {MARKS[kind]}
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="font-mono text-2xs opacity-80">{id}</span>
          {layer && <LayerBadge layer={layer} />}
          {badge && <span className="text-2xs opacity-70">{badge}</span>}
        </div>
        <p className={cn("text-sm text-primary", kind === "retired" && "line-through")}>{text}</p>
        {/* Always rendered. "No reason given" on a retirement is itself
            information — it is the change most likely to need one. */}
        <Rationale rationale={rationale} />
      </div>
    </div>
  );
}

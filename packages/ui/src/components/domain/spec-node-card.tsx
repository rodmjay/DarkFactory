import * as React from "react";
import { ArrowDownLeftIcon, ArrowUpRightIcon, TriangleAlertIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { at } from "../../lib/format";
import type { SpecNode, SpecRevision } from "../../types/spec";
import { LayerBadge } from "./layer-badge";
import { Rationale } from "./rationale";
import { SpecId } from "./spec-id";

export interface SpecNodeCardProps extends React.ComponentProps<"div"> {
  node: SpecNode;
  selected?: boolean;
}

/**
 * One small-grained spec node (ADR-0016): a single behaviour, rule or
 * constraint, not a feature.
 *
 * The text is the subject and gets the size; the id, layer and edge counts
 * are apparatus and stay quiet. That ordering is the whole argument for
 * small grain — a node you can read in one line is a node you can find a
 * contradiction in.
 *
 * Two states are not styling variants but statements about the graph.
 * `retired` is struck through and dimmed but still legible, because
 * append-only means it is still there and still referenced by history.
 * `drifted` says the code no longer matches (ADR-0024) — computed, not
 * prevented, so it is a fact about a node rather than an error.
 */
export function SpecNodeCard({ node, selected = false, className, ...props }: SpecNodeCardProps) {
  const retired = Boolean(node.retired_at);

  return (
    <div
      data-slot="spec-node-card"
      data-selected={selected || undefined}
      data-retired={retired || undefined}
      data-drifted={node.drifted || undefined}
      aria-current={selected ? "true" : undefined}
      className={cn(
        "flex flex-col gap-2 rounded-card border bg-card px-3.5 py-3 motion-fast transition-colors",
        selected ? "border-accent-border bg-accent-fill" : "border-border",
        retired && "opacity-60",
        className,
      )}
      {...props}
    >
      <div className="flex flex-wrap items-center gap-1.5">
        <SpecId id={node.spec_id} />
        <LayerBadge layer={node.layer} />
        <span className="text-2xs text-muted">{node.kind}</span>
        {retired && (
          <span className="ml-auto text-2xs tracking-wide text-muted uppercase">retired</span>
        )}
        {node.drifted && !retired && (
          <span
            className={cn(
              "ml-auto inline-flex items-center gap-1 rounded-pill border px-1.5 py-0.5 text-2xs",
              "border-status-failed-border bg-status-failed-fill text-status-failed-text",
            )}
            title="The implementing code no longer matches this specification (ADR-0024)."
          >
            <TriangleAlertIcon className="size-3" aria-hidden />
            drifted
          </span>
        )}
      </div>

      <p className={cn("text-sm text-primary", retired && "line-through decoration-1")}>
        {node.text}
      </p>

      {node.revisions && node.revisions.length > 0 && (
        <RevisionHistory revisions={node.revisions} current={node.revision_hash} />
      )}

      {(node.edges || node.revision_hash) && (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-2xs text-muted">
          {node.edges && (
            <>
              <span className="tnum inline-flex items-center gap-1" title="Outgoing edges">
                <ArrowUpRightIcon className="size-3" aria-hidden />
                {node.edges.outgoing}
              </span>
              <span className="tnum inline-flex items-center gap-1" title="Incoming edges">
                <ArrowDownLeftIcon className="size-3" aria-hidden />
                {node.edges.incoming}
              </span>
            </>
          )}
          {node.revision_hash && (
            <span className="font-mono" title="Content hash of this revision (SHA-256)">
              {node.revision_hash.slice(0, 12)}
            </span>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * The node's revisions, oldest first, each with its own reason.
 *
 * Oldest first rather than newest first: a spec node's history is an argument
 * that developed, and reading it backwards loses the sequence that makes the
 * current text make sense. The current revision is marked rather than moved
 * to the top.
 *
 * `actor_id` and `approved_by` are shown separately even though they hold the
 * same value today. Approval is single-actor now; ADR-0017 makes approvers
 * configurable per project, and the day they differ is the day the
 * distinction matters most — a card that had merged them would be silently
 * wrong rather than newly wrong.
 *
 * A revision whose reason changed but whose text did not will never appear
 * here, and that is correct: rationale is not part of what a revision is
 * hashed from, because two people can agree on a rule and disagree about why,
 * and that must not fork the revision.
 *
 * Always visible, and deliberately not a `<details>`. A card only receives
 * revisions on a detail view — a list passes none — so the history is the
 * reason the card is being looked at, and putting it behind a disclosure
 * makes the reader click to find out whether there is anything worth
 * clicking for.
 *
 * It was a `<details open>` for one commit and that made the visual suite
 * flaky: `open` is browser-managed state as well as a React attribute, and
 * the two do not reliably agree across hydration, so the section rendered at
 * two different heights between runs. Folding a long history is a real
 * feature if it is ever needed; a disclosure that renders
 * non-deterministically is not a substitute for one.
 */
function RevisionHistory({
  revisions,
  current,
}: {
  revisions: SpecRevision[];
  current?: string;
}) {
  return (
    <section className="border-t border-border pt-2">
      <h4 className="text-2xs tracking-wide text-muted uppercase">
        {revisions.length} revision{revisions.length === 1 ? "" : "s"}
      </h4>

      <ol className="mt-2 flex flex-col">
        {revisions.map((revision, index) => {
          const isCurrent = current !== undefined && revision.hash === current;
          return (
            <li
              key={revision.hash}
              className={cn(
                "flex gap-2.5 border-l-2 py-1.5 pl-2.5",
                isCurrent ? "border-accent-border" : "border-border",
              )}
            >
              <span className="tnum mt-0.5 w-4 shrink-0 text-right text-2xs text-muted">
                {index + 1}
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <p className="text-sm text-primary">{revision.text}</p>
                <Rationale rationale={revision.rationale} />
                <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-2xs text-muted">
                  <span
                    className="font-mono"
                    title="Content hash (SHA-256). Rationale is not part of it."
                  >
                    {revision.hash.slice(0, 8)}
                  </span>
                  <span className="tnum">{at(revision.created_at)}</span>
                  {revision.actor_id && <span>proposed by {revision.actor_id}</span>}
                  {revision.approved_by && <span>approved by {revision.approved_by}</span>}
                  {isCurrent && <span className="text-accent-text">current</span>}
                </div>
              </div>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

import * as React from "react";
import { ArrowDownLeftIcon, ArrowUpRightIcon, TriangleAlertIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import type { SpecNode } from "../../types/spec";
import { LayerBadge } from "./layer-badge";
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

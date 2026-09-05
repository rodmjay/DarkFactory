import * as React from "react";
import { GripVerticalIcon, LockIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import type { Amendment } from "../../types/run";
import { LayerBadge } from "./layer-badge";
import { SpecId } from "./spec-id";

export interface AmendmentRowProps extends React.ComponentProps<"div"> {
  amendment: Amendment;
  /** Already placed in a batch, so it is out of the backlog. */
  inBatch?: boolean;
  /** Its position once in a batch (ADR-0029's `seq`). */
  seq?: number;
}

/**
 * A backlog item, draggable into a batch (ADR-0029).
 *
 * The drag handle is always present and always visible rather than
 * appearing on hover: the ordering *is* the interaction on this screen, and
 * a control that only exists once you find it is a control that reads as
 * absent on a list of forty.
 *
 * `blocked-by-dependency` disables dragging and says which amendment it is
 * waiting on. The factory proposes the order by traversing `depends_on` and
 * the user's order wins (ADR-0029), so this is advice with a reason
 * attached rather than a rule — which is why it explains itself instead of
 * simply refusing.
 */
export function AmendmentRow({
  amendment,
  inBatch = false,
  seq,
  className,
  ...props
}: AmendmentRowProps) {
  const blocked = Boolean(amendment.blocked_by);

  return (
    <div
      data-slot="amendment-row"
      data-in-batch={inBatch || undefined}
      data-blocked={blocked || undefined}
      draggable={!blocked}
      aria-disabled={blocked || undefined}
      className={cn(
        "group flex items-start gap-2.5 rounded-card border px-3 py-2.5 motion-fast transition-colors",
        blocked
          ? "cursor-not-allowed border-border bg-sunken opacity-70"
          : "cursor-grab border-border bg-card hover:border-border-strong active:cursor-grabbing",
        inBatch && !blocked && "border-accent-border bg-accent-fill",
        className,
      )}
      {...props}
    >
      <span
        aria-hidden
        className={cn("mt-0.5 shrink-0", blocked ? "text-muted" : "text-border-strong")}
      >
        {blocked ? <LockIcon className="size-4" /> : <GripVerticalIcon className="size-4" />}
      </span>

      {inBatch && seq !== undefined && (
        <span className="tnum mt-0.5 w-4 shrink-0 text-right text-2xs font-medium text-accent-text">
          {seq}
        </span>
      )}

      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <p className="text-sm text-primary">{amendment.summary}</p>

        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <SpecId id={amendment.id} />
          {amendment.layers.map((layer) => (
            <LayerBadge key={layer} layer={layer} />
          ))}
          <span className="tnum text-2xs text-muted">
            {amendment.change_count} change{amendment.change_count === 1 ? "" : "s"}
          </span>
        </div>

        {blocked && (
          <p className="text-2xs text-status-failed-text">
            Blocked — depends on {amendment.blocked_by}. Order it after that one, or add it to
            this batch.
          </p>
        )}
      </div>
    </div>
  );
}

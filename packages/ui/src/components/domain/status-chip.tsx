import * as React from "react";

import { cn } from "../../lib/cn";
import {
  STATUS_LABEL,
  statusChip,
  statusDot,
  type StageStatus,
} from "../../lib/vocabulary";

export interface StatusChipProps extends React.ComponentProps<"span"> {
  status: StageStatus;
  /** Hide the label and render only the dot, for dense rows. */
  compact?: boolean;
}

/**
 * The one renderer for a stage status. Status arrives from the database as
 * a string; every place that shows one goes through here so that adding a
 * status is a change in `vocabulary.ts` and nowhere else.
 *
 * `running` breathes. The animation is the only motion in a run timeline,
 * which is what makes it readable at a glance in a list of forty stages.
 */
export function StatusChip({ status, compact = false, className, ...props }: StatusChipProps) {
  return (
    <span
      data-slot="status-chip"
      data-status={status}
      className={cn(
        "inline-flex w-fit shrink-0 items-center gap-1.5 rounded-pill border",
        "text-2xs font-medium whitespace-nowrap",
        compact ? "size-5 justify-center p-0" : "px-2 py-0.5",
        statusChip[status],
        className,
      )}
      {...props}
    >
      <span
        aria-hidden
        className={cn(
          "size-1.5 shrink-0 rounded-pill",
          statusDot[status],
          status === "running" && "breathe",
        )}
      />
      {compact ? (
        <span className="sr-only">{STATUS_LABEL[status]}</span>
      ) : (
        STATUS_LABEL[status]
      )}
    </span>
  );
}

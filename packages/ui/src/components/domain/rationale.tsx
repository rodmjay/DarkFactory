import * as React from "react";

import { cn } from "../../lib/cn";
import { rationaleState } from "../../types/spec";

export interface RationaleProps extends React.ComponentProps<"p"> {
  /** Absent, empty, or a reason. All three render differently. */
  rationale?: string | null;
  /** Say nothing at all when there is no reason, rather than saying so. */
  hideWhenAbsent?: boolean;
}

/**
 * Why a change was made.
 *
 * Three states, three renderings, deliberately not two:
 *
 *   absent   "No reason given."   nobody was asked, or nobody answered
 *   blank    "Reason left blank."  somebody was asked and left it empty
 *   given    the reason
 *
 * Collapsing blank into absent would hide the more actionable of the two —
 * an empty reason means there is a person to go and ask, and a missing one
 * usually means a prompt that never asked.
 *
 * Never truncated. The reasons the architect actually writes run 150–250
 * characters and are two sentences: a claim and its consequence. Cutting at
 * a tidy 80 removes the consequence, which is the half worth reading —
 * "30 days after due date is the assumed grace window; confirm before this
 * is built on" is useless without its second clause.
 */
export function Rationale({
  rationale,
  hideWhenAbsent = false,
  className,
  ...props
}: RationaleProps) {
  const state = rationaleState(rationale);

  if (state === "absent" && hideWhenAbsent) return null;

  return (
    <p
      data-slot="rationale"
      data-state={state}
      className={cn(
        "text-xs",
        state === "given" ? "text-secondary" : "text-muted italic",
        className,
      )}
      {...props}
    >
      {state === "given"
        ? rationale
        : state === "blank"
          ? "Reason left blank."
          : "No reason given."}
    </p>
  );
}

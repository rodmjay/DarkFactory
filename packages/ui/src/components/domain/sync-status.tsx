import * as React from "react";
import { CheckIcon, CloudOffIcon, RefreshCwIcon, TriangleAlertIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import type { SyncState } from "../../types/run";

export interface SyncStatusProps extends React.ComponentProps<"span"> {
  state: SyncState;
  /** Pending local writes, when syncing or offline. */
  pending?: number;
  /** Hide the label, leaving the icon and its accessible name. */
  compact?: boolean;
}

const COPY: Record<SyncState, { label: string; title: string }> = {
  synced: { label: "Synced", title: "Everything local matches the factory." },
  syncing: { label: "Syncing", title: "Sending local writes to the factory." },
  // Deliberately reassuring. Local-first means offline is a supported mode.
  offline: {
    label: "Offline",
    title: "Working from the local copy. Changes will sync when the connection returns.",
  },
  conflict: {
    label: "Conflict",
    title: "A write was rejected by the factory and needs a decision.",
  },
};

/**
 * The local-first affordance (ADR-0031).
 *
 * `offline` is drawn in neutral, not in a warning colour. The whole point of
 * a local SQLite mirror is that losing the connection is a supported mode
 * rather than a failure: reads keep working from the mirror and writes queue.
 * Colouring it amber would tell people something is wrong at exactly the
 * moment the design is working as intended — and would spend the same signal
 * that `conflict`, which genuinely needs a person, has to use.
 *
 * `conflict` is the one state here that means "a person must act", so it is
 * the only one that borrows from the diff-conflict token. Append-only tables
 * mean this is rare — it is a write the factory rejected, not two versions of
 * a row to merge.
 */
export function SyncStatus({
  state,
  pending,
  compact = false,
  className,
  ...props
}: SyncStatusProps) {
  const Icon =
    state === "synced"
      ? CheckIcon
      : state === "syncing"
        ? RefreshCwIcon
        : state === "offline"
          ? CloudOffIcon
          : TriangleAlertIcon;

  const tone: Record<SyncState, string> = {
    synced: "border-border bg-sunken text-muted",
    syncing: "border-status-running-border bg-status-running-fill text-status-running-text",
    offline: "border-border bg-sunken text-secondary",
    conflict:
      "border-diff-conflict-border bg-diff-conflict-fill text-diff-conflict-text",
  };

  return (
    <span
      data-slot="sync-status"
      data-state={state}
      title={COPY[state].title}
      className={cn(
        "inline-flex w-fit items-center gap-1.5 rounded-pill border px-2 py-0.5 text-2xs font-medium",
        tone[state],
        className,
      )}
      {...props}
    >
      <Icon
        aria-hidden
        className={cn("size-3 shrink-0", state === "syncing" && "animate-spin [animation-duration:1600ms]")}
      />
      {compact ? (
        <span className="sr-only">{COPY[state].label}</span>
      ) : (
        COPY[state].label
      )}
      {pending !== undefined && pending > 0 && (
        <span className="tnum opacity-80">{pending}</span>
      )}
    </span>
  );
}

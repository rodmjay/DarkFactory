import * as React from "react";
import { FileTextIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { tokens, usd } from "../../lib/format";
import { STATUS_LABEL, needsYou, statusChip, statusDot } from "../../lib/vocabulary";
import type { StageTimelineData, StageTimelineEntry } from "../../types/run";
import { Avatar, AvatarFallback, AvatarImage } from "../ui/avatar";
import { Button } from "../ui/button";
import { SpecId } from "./spec-id";
import { StatusChip } from "./status-chip";

export interface StageTimelineProps
  extends Omit<React.ComponentProps<"ol">, "onSubmit">,
    StageTimelineData {
  /** Rendered on a parked stage, under the agent's question. */
  onAnswer?: (stage: StageTimelineEntry) => void;
}

/**
 * plan → implement → verify → ship for one run.
 *
 * Ship is on the timeline but it is a *batch* action (ADR-0029): a run that
 * verifies clean is deployable, not deployed. Showing it here and having it
 * driven from the batch is the honest depiction of that split — the stage
 * exists, it is simply not this run's to invoke.
 *
 * A parked stage opens inline with the agent's question. The alternative —
 * a badge you click to find out what it wants — makes the one state that
 * needs a person the one that takes an extra step, and a run parked
 * overnight because nobody expanded the row is the whole failure mode.
 */
export function StageTimeline({
  run_id,
  snapshot_id,
  stages,
  onAnswer,
  className,
  ...props
}: StageTimelineProps) {
  return (
    <ol data-slot="stage-timeline" className={cn("flex flex-col", className)} {...props}>
      {stages.map((entry, index) => (
        // Keyed by position, not by stage name: a run that retried a stage
        // has two `implement` entries, and keying by name collapses them.
        <li key={`${entry.stage}-${entry.attempt ?? index}`} className="relative flex gap-3">
          {/* The rail. Drawn per row rather than as one absolute line so it
              stops cleanly at the last stage instead of trailing past it. */}
          <div className="flex flex-col items-center">
            <span
              aria-hidden
              className={cn(
                "mt-1.5 size-2.5 shrink-0 rounded-pill ring-2 ring-card",
                statusDot[entry.status],
                entry.status === "running" && "breathe",
              )}
            />
            {index < stages.length - 1 && (
              <span aria-hidden className="w-px flex-1 bg-border" />
            )}
          </div>

          <div className={cn("min-w-0 flex-1", index < stages.length - 1 && "pb-4")}>
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-medium text-primary">{entry.stage}</span>
              <StatusChip status={entry.status} />
              {entry.attempt !== undefined && entry.attempt > 1 && (
                <span className="tnum text-2xs text-muted">attempt {entry.attempt}</span>
              )}
            </div>

            <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
              {entry.owner && (
                <span className="flex items-center gap-1.5 text-xs text-secondary">
                  <Avatar className="size-4 rounded-pill">
                    {entry.owner.avatar_url && <AvatarImage src={entry.owner.avatar_url} alt="" />}
                    <AvatarFallback className="text-[9px]">
                      {(entry.owner.persona_name ?? entry.owner.role).slice(0, 2).toUpperCase()}
                    </AvatarFallback>
                  </Avatar>
                  {entry.owner.persona_name ?? entry.owner.role}
                  <span className="font-mono text-2xs text-muted">{entry.owner.deployment}</span>
                </span>
              )}

              {entry.artifact && (
                <span
                  className={cn(
                    "inline-flex items-center gap-1 rounded-control border border-border",
                    "bg-sunken px-1.5 py-0.5 font-mono text-2xs text-secondary",
                  )}
                  title={entry.artifact.ref}
                >
                  <FileTextIcon className="size-3 text-muted" aria-hidden />
                  {entry.artifact.label ?? entry.artifact.type}
                </span>
              )}

              {entry.cost && (
                <span className="tnum text-2xs text-muted">
                  {tokens(entry.cost.input_tokens + entry.cost.output_tokens)} tokens
                  {entry.cost.usd != null && ` · ${usd(entry.cost.usd)}`}
                </span>
              )}
            </div>

            {entry.question && (
              <div
                className={cn(
                  "mt-2 rounded-card border px-3 py-2",
                  statusChip[entry.status],
                )}
              >
                <div className="text-2xs font-medium tracking-wide uppercase opacity-80">
                  {entry.owner?.role ?? "The agent"} is waiting on you
                </div>
                <p className="mt-1 text-sm text-primary">{entry.question}</p>
                {onAnswer && needsYou(entry.status) && (
                  <Button
                    variant="needs-you"
                    size="sm"
                    className="mt-2.5"
                    onClick={() => onAnswer(entry)}
                  >
                    Answer
                  </Button>
                )}
              </div>
            )}
          </div>
        </li>
      ))}

      {(run_id || snapshot_id) && (
        <li className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 pl-[22px] text-2xs text-muted">
          {run_id && (
            <span className="flex items-center gap-1">
              run <SpecId id={run_id} />
            </span>
          )}
          {snapshot_id && (
            <span className="flex items-center gap-1">
              snapshot <SpecId id={snapshot_id} />
            </span>
          )}
        </li>
      )}
    </ol>
  );
}

export { STATUS_LABEL };

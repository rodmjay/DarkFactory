import * as React from "react";

import { cn } from "../../lib/cn";
import { tokens, tokensExact, usd } from "../../lib/format";
import type { CostSegment } from "../../types/run";

export interface CostBarProps extends React.ComponentProps<"div"> {
  segments: CostSegment[];
  /** The member or run budget this is measured against, when one is set. */
  budget?: number | null;
  /** Hide the legend, for a bar inside a dense row. */
  compact?: boolean;
}

/**
 * Cost broken down by stage or by member.
 *
 * Segments are sized by token share rather than by dollars, because the two
 * disagree — cached input is billed at a fraction of fresh input (ADR-0032)
 * — and the question this answers is "where did the work go". The dollar
 * figures are stated per segment rather than encoded in the geometry, so
 * nothing pretends the bar is a cost chart.
 *
 * A segment marked `over_budget` wears the over-budget token whatever its
 * size, since a small segment that took the run over the line is exactly
 * the one worth seeing.
 */
export function CostBar({
  segments,
  budget,
  compact = false,
  className,
  ...props
}: CostBarProps) {
  const total = segments.reduce((sum, segment) => sum + segment.tokens, 0);
  const totalUsd = segments.reduce((sum, segment) => sum + (segment.usd ?? 0), 0);
  const overBudget = budget != null && total > budget;

  // What the full width means. Without a budget the bar is a breakdown and
  // the width is the total; with one, the reader inevitably reads the bar as
  // progress against the budget, so that is what it has to be. Scaling
  // segments to the total while printing "1,132 of 200,000" beside a full
  // bar says the budget is spent when 0.5% of it is.
  const scale = budget != null && !overBudget ? budget : total;
  const width = (tokens: number) => (scale > 0 ? `${(tokens / scale) * 100}%` : "0%");

  // Written out rather than interpolated — Tailwind scans source text.
  const FILLS = [
    "bg-layer-product-border",
    "bg-layer-api-border",
    "bg-layer-data-border",
    "bg-layer-infra-border",
    "bg-layer-ui-border",
    "bg-layer-other-border",
  ];

  return (
    <div data-slot="cost-bar" className={cn("flex flex-col gap-2", className)} {...props}>
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-2xs tracking-wide text-muted uppercase">Cost</span>
        <span className="tnum text-xs text-secondary">
          {tokens(total)} tokens
          {totalUsd > 0 && <span className="text-muted"> · {usd(totalUsd)}</span>}
        </span>
      </div>

      <div
        className="flex h-2 w-full overflow-hidden rounded-pill bg-sunken"
        role="img"
        aria-label={
          budget != null
            ? `${tokensExact(total)} of ${tokensExact(budget)} tokens used`
            : `${tokensExact(total)} tokens across ${segments.length} segments`
        }
      >
        {segments.map((segment, index) => (
          <div
            key={segment.label}
            title={`${segment.label}: ${tokensExact(segment.tokens)} tokens`}
            style={{ width: width(segment.tokens) }}
            className={cn(
              "h-full",
              segment.over_budget
                ? "bg-status-over-budget-text"
                : FILLS[index % FILLS.length],
            )}
          />
        ))}
      </div>

      {budget != null && (
        <div
          className={cn(
            "tnum text-2xs",
            overBudget ? "text-status-over-budget-text" : "text-muted",
          )}
        >
          {tokens(total)} of {tokens(budget)} budget
          {overBudget && ` — over by ${tokens(total - budget)}`}
        </div>
      )}

      {!compact && (
        <ul className="flex flex-wrap gap-x-4 gap-y-1">
          {segments.map((segment, index) => (
            <li key={segment.label} className="flex items-center gap-1.5 text-2xs">
              <span
                aria-hidden
                className={cn(
                  "size-2 shrink-0 rounded-[2px]",
                  segment.over_budget
                    ? "bg-status-over-budget-text"
                    : FILLS[index % FILLS.length],
                )}
              />
              <span
                className={cn(
                  segment.over_budget ? "text-status-over-budget-text" : "text-secondary",
                )}
              >
                {segment.label}
              </span>
              <span className="tnum text-muted">{tokens(segment.tokens)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

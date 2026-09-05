import * as React from "react";
import { ArrowDownIcon, ArrowUpIcon, MinusIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { delta as formatDelta } from "../../lib/format";
import type { Metric } from "../../types/run";

export interface MetricTileProps extends React.ComponentProps<"div">, Metric {}

/**
 * One number, with what it is measured against.
 *
 * The direction of a delta is coloured by whether it is *good*, not by its
 * sign: cost rising and throughput rising are both "up" and mean opposite
 * things. `delta_is_good` carries that, and when it is absent the delta is
 * shown in neutral text rather than guessed at — a green number that means
 * "we are spending more" is worse than no colour at all.
 *
 * `no-data` is a first-class state. A tile that renders 0 when it means
 * "nothing reported" teaches people to distrust the ones that mean zero.
 */
export function MetricTile({
  label,
  value,
  unit,
  delta,
  delta_is_good,
  series,
  period,
  className,
  ...props
}: MetricTileProps) {
  const hasData = value !== null && value !== undefined && value !== "";
  const direction = delta == null || delta === 0 ? "flat" : delta > 0 ? "up" : "down";

  const Icon =
    direction === "up" ? ArrowUpIcon : direction === "down" ? ArrowDownIcon : MinusIcon;

  // Neutral unless we were told which way is good.
  const deltaTone =
    delta == null || delta_is_good === undefined || direction === "flat"
      ? "text-muted"
      : (direction === "up") === delta_is_good
        ? "text-status-passed-text"
        : "text-status-failed-text";

  return (
    <div
      data-slot="metric-tile"
      data-state={hasData ? direction : "no-data"}
      className={cn(
        "flex flex-col gap-1.5 rounded-card border border-border bg-card px-3.5 py-3",
        className,
      )}
      {...props}
    >
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-2xs tracking-wide text-muted uppercase">{label}</span>
        {period && <span className="text-2xs text-muted">{period}</span>}
      </div>

      <div className="flex items-baseline gap-1.5">
        {hasData ? (
          <>
            <span className="tnum text-xl leading-none font-semibold text-primary">{value}</span>
            {unit && <span className="text-xs text-muted">{unit}</span>}
          </>
        ) : (
          <span className="text-xl leading-none font-semibold text-muted">—</span>
        )}
      </div>

      <div className="flex items-center justify-between gap-2">
        {hasData && delta !== undefined ? (
          <span className={cn("tnum inline-flex items-center gap-0.5 text-2xs", deltaTone)}>
            <Icon className="size-3" aria-hidden />
            {formatDelta(delta)}
          </span>
        ) : (
          <span className="text-2xs text-muted">{hasData ? "" : "no data"}</span>
        )}
        {hasData && series && series.length > 1 && <Sparkline values={series} />}
      </div>
    </div>
  );
}

/**
 * A sparkline, drawn rather than charted. No axes, no gridlines, no
 * library: it exists to say "roughly this shape", and anything more would
 * be a chart pretending to be a decoration.
 */
function Sparkline({ values }: { values: number[] }) {
  const width = 56;
  const height = 16;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;

  const points = values
    .map((value, index) => {
      const x = (index / (values.length - 1)) * width;
      const y = height - ((value - min) / span) * height;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");

  return (
    <svg
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      className="overflow-visible"
      aria-hidden
      focusable="false"
    >
      <polyline
        points={points}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.25"
        strokeLinecap="round"
        strokeLinejoin="round"
        className="text-border-strong"
      />
    </svg>
  );
}

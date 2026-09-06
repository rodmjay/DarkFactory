"use client";

/**
 * Usage and cost.
 *
 * One row per model call (ADR-0032), so every figure here is a query rather
 * than an estimate — which is why the screen can say "no data" for thinking
 * tokens instead of quietly showing a zero. A missing number and a zero are
 * different facts, and only one of them is about the provider.
 */

import {
  Button,
  CostBar,
  MetricTile,
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
  cn,
  format,
} from "@dark-factory/ui";

import { useQuery } from "@/lib/local/store";

export default function UsagePage() {
  const usage = useQuery((db) => db.usage);
  const project = useQuery((db) => db.project);

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 px-6 py-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold">Usage and cost</h1>
          <p className="mt-0.5 text-xs text-secondary">
            {project?.name} · {usage.period} · one row per model call, so every figure here is a
            query rather than an estimate.
          </p>
        </div>
        <div className="flex gap-2">
          <Button size="sm" variant="default">
            This project
          </Button>
          <Button size="sm" variant="outline">
            Whole org
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-5 gap-3">
        {usage.headline.map((metric) => (
          <MetricTile
            key={metric.label}
            label={metric.label}
            value={metric.value}
            unit={metric.unit}
            delta={metric.delta}
            delta_is_good={metric.delta_is_good}
            period={metric.note}
          />
        ))}
      </div>

      <section className="flex flex-col gap-2.5">
        <div className="flex items-baseline gap-3">
          <h2 className="text-sm font-semibold">By stage</h2>
          <span className="text-2xs text-muted">
            Where the money goes, and whether the outcome justified it.
          </span>
        </div>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Stage</TableHead>
              <TableHead>Share</TableHead>
              <TableHead numeric>Cost</TableHead>
              <TableHead numeric>Tokens</TableHead>
              <TableHead numeric>Calls</TableHead>
              <TableHead numeric>Retried</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {usage.by_stage.map((row) => (
              <TableRow key={row.stage}>
                <TableCell className="font-mono text-2xs">{row.stage}</TableCell>
                <TableCell>
                  <Share fraction={row.share} />
                </TableCell>
                <TableCell numeric>{format.usd(row.usd)}</TableCell>
                <TableCell numeric>{row.tokens}</TableCell>
                <TableCell numeric>{row.calls}</TableCell>
                <TableCell numeric>{row.retried}</TableCell>
              </TableRow>
            ))}
          </TableBody>
          <TableFooter>
            <TableRow>
              <TableCell>Total</TableCell>
              <TableCell />
              <TableCell numeric>{format.usd(usage.totals.usd)}</TableCell>
              <TableCell numeric>{usage.totals.tokens}</TableCell>
              <TableCell numeric>{usage.totals.calls}</TableCell>
              <TableCell numeric>{usage.totals.retried}</TableCell>
            </TableRow>
          </TableFooter>
        </Table>
      </section>

      <section className="flex flex-col gap-2.5">
        <div className="flex items-baseline gap-3">
          <h2 className="text-sm font-semibold">By member and persona</h2>
          <span className="text-2xs text-muted">
            Two personas on one model differ by speed, and that shows up here rather than in a
            price list.
          </span>
        </div>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Member</TableHead>
              <TableHead>Model · speed</TableHead>
              <TableHead numeric>Cost</TableHead>
              <TableHead numeric>Per amendment</TableHead>
              <TableHead numeric>First try</TableHead>
              <TableHead numeric>Budget</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {usage.by_member.map((row) => (
              <TableRow key={row.member}>
                <TableCell>
                  {row.member}
                  {row.qualifier ? (
                    <span className="text-muted"> {row.qualifier}</span>
                  ) : null}
                </TableCell>
                <TableCell className="font-mono text-2xs text-secondary">{row.model}</TableCell>
                <TableCell numeric>{format.usd(row.usd)}</TableCell>
                <TableCell numeric>{format.usd(row.per_amendment)}</TableCell>
                <TableCell numeric>{row.first_try}</TableCell>
                <TableCell numeric>
                  <span
                    className={cn(
                      row.over_budget &&
                        "rounded-pill border border-status-over-budget-border bg-status-over-budget-fill px-1.5 text-status-over-budget-text",
                    )}
                  >
                    {row.budget}
                  </span>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <p className="text-2xs text-secondary">{usage.by_member_note}</p>
      </section>

      <section className="flex flex-col gap-2.5">
        <div className="flex items-baseline gap-3">
          <h2 className="text-sm font-semibold">Trend</h2>
          <span className="text-2xs text-muted">
            Daily cost, split cached against uncached input.
          </span>
        </div>
        <div className="flex flex-col gap-2 rounded-card border border-border bg-card px-4 py-3.5">
          {usage.trend.map((day) => (
            <div key={day.day} className="flex items-center gap-3">
              <span className="w-14 shrink-0 text-2xs text-muted">{day.day}</span>
              <CostBar
                className="flex-1"
                segments={[
                  { label: "uncached input + output", tokens: Math.round(day.uncached * 1000), usd: day.uncached },
                  { label: "cached input", tokens: Math.round(day.cached * 1000), usd: day.cached },
                ]}
              />
              <span className="tnum w-14 shrink-0 text-right text-2xs text-primary">
                {format.usd(day.usd)}
              </span>
            </div>
          ))}
          <p className="mt-1 text-2xs text-muted">{usage.trend_note}</p>
        </div>
      </section>
    </div>
  );
}

/** A share of total spend, drawn rather than written — the column exists to
 *  be scanned, and five percentages are not scannable. */
function Share({ fraction }: { fraction: number }) {
  return (
    <span className="flex h-1.5 w-24 overflow-hidden rounded-pill bg-sunken">
      <span
        className="h-full rounded-pill bg-accent"
        style={{ width: `${Math.max(fraction * 100, 1)}%` }}
      />
    </span>
  );
}

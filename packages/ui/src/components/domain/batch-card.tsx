import * as React from "react";
import { RocketIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { at } from "../../lib/format";
import type { Batch, BatchStatus } from "../../types/run";
import { Button } from "../ui/button";
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from "../ui/card";
import { SpecId } from "./spec-id";
import { StatusChip } from "./status-chip";

export interface BatchCardProps extends React.ComponentProps<"div"> {
  batch: Batch;
  onDeploy?: (batch: Batch) => void;
}

const BATCH_LABEL: Record<BatchStatus, string> = {
  composing: "Composing",
  running: "Running",
  blocked_by_verify: "Blocked by verify",
  deployable: "Deployable",
  deployed: "Deployed",
};

/**
 * An ordered set of amendments, executed as a sequence (ADR-0029).
 *
 * The deploy control lives here and nowhere on a run, because deploy is a
 * batch action — a run that verifies clean is *deployable*, not deployed —
 * and the batch is therefore the unit of release notes and rollback. Its
 * placement is the ADR made visible: if the button were on a run, the
 * schema would be lying about what a release is.
 *
 * `blocked_by_verify` is not a failure state of the batch so much as a
 * report about one of its runs, so it names the item that stopped it. A
 * halted batch deploys nothing at all, which is the point — a partial batch
 * never reaches production.
 */
export function BatchCard({ batch, onDeploy, className, ...props }: BatchCardProps) {
  const failed = batch.items.find((item) => item.status === "failed");
  const parked = batch.items.find((item) => item.status === "parked");
  const blocker = failed ?? parked;

  return (
    <Card data-slot="batch-card" data-status={batch.status} className={className} {...props}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <CardTitle>{batch.name ?? `Batch ${batch.id.slice(-4)}`}</CardTitle>
            <div className="mt-1 flex flex-wrap items-center gap-2 text-2xs text-muted">
              <SpecId id={batch.id} />
              <span className="tnum">
                {batch.items.length} item{batch.items.length === 1 ? "" : "s"}
              </span>
              {batch.deployed_at && <span>deployed {at(batch.deployed_at)}</span>}
            </div>
          </div>
          <BatchPill status={batch.status} />
        </div>
      </CardHeader>

      <CardContent>
        <ol className="flex flex-col">
          {batch.items.map((item, index) => (
            <li
              key={item.amendment_id}
              className={cn(
                "flex items-center gap-2.5 border-b border-border py-2 last:border-b-0",
                index === 0 && "pt-0",
              )}
            >
              <span className="tnum w-4 shrink-0 text-right text-2xs text-muted">{item.seq}</span>
              <StatusChip status={item.status} compact />
              <span className="min-w-0 flex-1 truncate text-sm text-primary">{item.summary}</span>
              {item.run_id && <SpecId id={item.run_id} />}
            </li>
          ))}
        </ol>

        {batch.status === "blocked_by_verify" && blocker && (
          <p className="mt-3 rounded-control border border-status-failed-border bg-status-failed-fill px-2.5 py-2 text-xs text-status-failed-text">
            Halted at item {blocker.seq}. Nothing in this batch deploys until it passes — a
            partial batch never reaches production.
          </p>
        )}
      </CardContent>

      <CardFooter>
        {/* Enabled only when every run has verified. The gate is the batch's,
            not any single run's (ADR-0029). */}
        <Button
          variant={batch.status === "deployable" ? "needs-you" : "default"}
          size="sm"
          disabled={batch.status !== "deployable"}
          onClick={() => onDeploy?.(batch)}
        >
          <RocketIcon />
          {batch.status === "deployed" ? "Deployed" : "Deploy batch"}
        </Button>
        {batch.status === "composing" && (
          <span className="text-2xs text-muted">Reorder before executing.</span>
        )}
        {batch.status === "running" && (
          <span className="text-2xs text-muted">
            Runs execute in sequence — each builds on the last.
          </span>
        )}
      </CardFooter>
    </Card>
  );
}

function BatchPill({ status }: { status: BatchStatus }) {
  const map: Record<BatchStatus, string> = {
    composing: "border-border bg-sunken text-secondary",
    running: "border-status-running-border bg-status-running-fill text-status-running-text",
    blocked_by_verify:
      "border-status-failed-border bg-status-failed-fill text-status-failed-text",
    deployable: "border-accent-border bg-accent-fill text-accent-text",
    deployed: "border-status-passed-border bg-status-passed-fill text-status-passed-text",
  };

  return (
    <span
      className={cn(
        "shrink-0 rounded-pill border px-2 py-0.5 text-2xs font-medium whitespace-nowrap",
        map[status],
      )}
    >
      {BATCH_LABEL[status]}
    </span>
  );
}

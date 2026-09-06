"use client";

/**
 * Steps 3 and 4 — Execute and Deploy.
 *
 * Deploy is a batch action and never a run action (ADR-0029), which is why
 * the only deploy control on this screen sits on a batch header, and why it
 * says *why* it is disabled rather than just being grey. A batch that
 * cannot ship and will not say why is the thing people file bugs about.
 */

import * as React from "react";

import {
  Button,
  LayerBadge,
  SpecId,
  StageTimeline,
  StatusChip,
  Textarea,
  cn,
  format,
} from "@dark-factory/ui";

import { useDispatch, useQuery } from "@/lib/local/store";
import type { BatchRow } from "@/lib/local/schema";

export default function BatchesPage() {
  const batches = useQuery((db) => db.batches);

  const running = batches.filter((b) => b.status === "running").length;
  const blocked = batches.filter((b) => b.status === "blocked_by_verify").length;
  const deployable = batches.filter((b) => b.status === "deployable").length;

  return (
    <div className="flex h-full min-h-0">
      <Backlog />

      <div className="min-h-0 flex-1 overflow-auto px-5 py-4">
        <div className="mb-3 flex items-baseline gap-3">
          <h1 className="text-sm font-semibold">Batches</h1>
          <span className="text-2xs text-muted">
            {running} running · {blocked} blocked · {deployable} deployable
          </span>
        </div>

        <div className="flex flex-col gap-3">
          {batches.map((batch) => (
            <BatchCard key={batch.id} batch={batch} />
          ))}
        </div>
      </div>

      <RunPanel />
    </div>
  );
}

// ----------------------------------------------------------------- backlog

/** Approved amendments, in the order `depends_on` proposes for them. */
function Backlog() {
  const amendments = useQuery((db) => db.amendments.filter((a) => a.status === "approved"));
  const dispatch = useDispatch();

  return (
    <aside className="flex w-72 flex-none flex-col border-r border-border bg-card">
      <div className="flex-none border-b border-border px-3.5 py-3">
        <span className="text-xs font-medium text-primary">Backlog</span>
        <p className="mt-0.5 text-2xs text-muted">
          {amendments.length} approved amendment{amendments.length === 1 ? "" : "s"}. Order
          proposed from <code className="font-mono">depends_on</code>; drag to change it.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 flex-col gap-2 overflow-auto p-2.5">
        {amendments.map((amendment, index) => (
          <div
            key={amendment.id}
            draggable
            className="flex cursor-grab gap-2 rounded-control border border-border bg-page px-2.5 py-2"
          >
            <span aria-hidden className="mt-0.5 text-muted">
              ⠿
            </span>
            <div className="flex min-w-0 flex-col gap-1">
              <span className="text-2xs text-primary">{amendment.summary}</span>
              <div className="flex flex-wrap items-center gap-1 text-[10px] text-muted">
                {amendment.layers.map((layer) => (
                  <LayerBadge key={layer} layer={layer} />
                ))}
                <span className="tnum">{amendment.change_count} changes</span>
                {index > 0 ? (
                  <span className="text-accent-text">blocked by the item above</span>
                ) : null}
              </div>
            </div>
          </div>
        ))}

        {amendments.length > 0 ? (
          <Button
            size="sm"
            variant="needs-you"
            className="mt-1"
            onClick={() => dispatch("df.batches.compose")}
          >
            Compose batch 17 from these {amendments.length}
          </Button>
        ) : (
          <p className="px-1 text-2xs text-muted">
            Nothing approved and unbatched. Approve an amendment and it lands here.
          </p>
        )}
      </div>
    </aside>
  );
}

// ------------------------------------------------------------- batch cards

function BatchCard({ batch }: { batch: BatchRow }) {
  const dispatch = useDispatch();
  const deployable = batch.status === "deployable";
  const deployed = batch.status === "deployed";

  return (
    <div className="rounded-card border border-border bg-card">
      <div className="flex items-center gap-3 border-b border-border px-3.5 py-2.5">
        <span className="text-xs font-medium text-primary">{batch.label}</span>
        <BatchStatus status={batch.status} />
        <span className="tnum text-2xs text-muted">
          {deployed
            ? batch.deployed_note
            : batch.budget_usd
              ? `${format.usd(batch.spend_usd)} of ${format.usd(batch.budget_usd)}`
              : `${format.usd(batch.spend_usd)} · ${batch.run_count} runs passed`}
        </span>

        {deployed ? (
          <Button size="sm" variant="outline" className="ml-auto">
            Release notes
          </Button>
        ) : (
          <Button
            size="sm"
            variant={deployable ? "needs-you" : "outline"}
            className="ml-auto"
            disabled={!deployable}
            title={deployable ? "Ships every run in this batch as one release." : batch.blocked_reason}
            onClick={() => dispatch("df.batches.deploy", { id: batch.id })}
          >
            {deployable ? "Deploy batch" : "Deploy"}
          </Button>
        )}
      </div>

      {batch.note ? (
        <p className="border-b border-border px-3.5 py-2 text-2xs text-secondary">{batch.note}</p>
      ) : null}

      {batch.items.length > 0 ? (
        <ol className="flex flex-col">
          {batch.items.map((item) => (
            <li
              key={item.seq}
              className="flex items-center gap-3 border-b border-border px-3.5 py-2 text-2xs last:border-b-0"
            >
              <span className="tnum w-4 shrink-0 text-muted">{item.seq}</span>
              <StatusChip status={item.status} />
              <span className="truncate text-primary">{item.summary}</span>
              {item.run_id ? (
                <span className="ml-auto shrink-0 font-mono text-muted">implement · attached</span>
              ) : item.status === "parked" ? (
                <Button size="sm" variant="needs-you" className="ml-auto shrink-0">
                  Answer
                </Button>
              ) : null}
            </li>
          ))}
        </ol>
      ) : null}
    </div>
  );
}

const BATCH_LABEL: Record<BatchRow["status"], string> = {
  composing: "Composing",
  running: "Running",
  blocked_by_verify: "Blocked by verify",
  deployable: "Deployable",
  deployed: "Deployed",
};

/** A batch status is not a stage status — `deployable` has no run analogue —
 *  so it gets its own small mapping rather than being forced through one. */
function BatchStatus({ status }: { status: BatchRow["status"] }) {
  if (status === "running") return <StatusChip status="running" />;
  if (status === "blocked_by_verify")
    return (
      <span className="inline-flex items-center gap-1.5 rounded-pill border border-status-failed-border bg-status-failed-fill px-2 py-0.5 text-2xs font-medium text-status-failed-text">
        <span aria-hidden className="size-1.5 rounded-pill bg-status-failed-text" />
        {BATCH_LABEL[status]}
      </span>
    );

  return (
    <span
      className={cn(
        "inline-flex items-center rounded-pill border px-2 py-0.5 text-2xs font-medium",
        status === "deployable"
          ? "border-accent-border bg-accent-fill text-accent-text"
          : "border-border bg-sunken text-secondary",
      )}
    >
      {BATCH_LABEL[status]}
    </span>
  );
}

// --------------------------------------------------------------- run panel

/**
 * The attached run: its timeline, what it is doing right now, and a way to
 * steer it without cancelling it. Steering is recorded as an event with a
 * name on it, which is what makes it different from editing the plan.
 */
function RunPanel() {
  const run = useQuery((db) => db.runs[0]);
  const events = useQuery((db) => db.events);
  const dispatch = useDispatch();
  const [steer, setSteer] = React.useState("");

  if (!run) return null;

  return (
    <aside className="flex w-96 flex-none flex-col border-l border-border bg-card">
      <div className="flex-none border-b border-border px-4 py-3">
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium text-primary">{run.batch_label}</span>
          {run.attached ? (
            <span className="inline-flex items-center gap-1.5 rounded-pill border border-status-running-border bg-status-running-fill px-1.5 text-[10px] text-status-running-text">
              <span aria-hidden className="size-1.5 rounded-pill bg-status-running-text breathe" />
              Attached
            </span>
          ) : null}
        </div>
        <div className="mt-1 flex flex-wrap gap-2 font-mono text-2xs text-muted">
          <span className="flex gap-1">
            run <SpecId id={run.id} />
          </span>
          <span className="flex gap-1">
            snapshot <SpecId id={run.snapshot_id ?? ""} />
          </span>
          <span>team rev {run.team_revision}</span>
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-auto px-4 py-3">
        <StageTimeline run_id={run.run_id} snapshot_id={run.snapshot_id} stages={run.stages} />

        <section className="mt-4 border-t border-border pt-3">
          <div className="flex items-baseline justify-between">
            <span className="text-2xs font-medium text-secondary">Live activity</span>
            <span className="font-mono text-[10px] text-muted">df.work.attach</span>
          </div>
          <ul className="mt-2 flex flex-col gap-1">
            {events.map((event) => (
              <li key={event.id} className="flex gap-2 font-mono text-[10px]">
                <span className="tnum shrink-0 text-muted">{event.at}</span>
                <span className="truncate text-secondary">
                  {event.command}
                  {event.detail ? <span className="text-muted"> {event.detail}</span> : null}
                  {event.outcome ? (
                    <span className="text-status-passed-text"> {event.outcome}</span>
                  ) : null}
                  {event.streaming ? <span className="text-muted"> …</span> : null}
                </span>
              </li>
            ))}
          </ul>

          <div className="mt-3 flex flex-col gap-2">
            <Textarea
              value={steer}
              onChange={(event) => setSteer(event.target.value)}
              placeholder="Steer this run without cancelling it — guidance lands in the next agent turn."
              className="min-h-16 resize-none text-2xs"
            />
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="needs-you"
                disabled={!steer.trim()}
                onClick={() => {
                  dispatch("df.runs.steer", { run_id: run.id, text: steer });
                  setSteer("");
                }}
              >
                Steer
              </Button>
              <span className="text-[10px] text-muted">
                Recorded as an event, with your name on it.
              </span>
            </div>
          </div>
        </section>
      </div>
    </aside>
  );
}

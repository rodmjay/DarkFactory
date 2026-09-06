"use client";

/**
 * Step 2 — Approve.
 *
 * A queue on the left, the selected amendment on the right. The detail pane
 * leads with the conflict rather than the diff: a change that contradicts a
 * decision someone made in March is a different decision from a change that
 * does not, and burying that under a list of edits is how it gets approved
 * by accident.
 */

import * as React from "react";
import Link from "next/link";

import {
  ApprovalCard,
  Button,
  LayerBadge,
  SpecDiff,
  SpecId,
  cn,
  format,
} from "@dark-factory/ui";

import { useDecision, useDispatch, useQuery } from "@/lib/local/store";
import type { AmendmentRow } from "@/lib/local/schema";

export default function AmendmentsPage() {
  const amendments = useQuery((db) => db.amendments);
  const [selectedId, setSelectedId] = React.useState<string | null>(null);

  const proposed = amendments.filter((a) => a.status === "proposed");
  const settled = amendments.filter((a) => a.status !== "proposed");
  const selected = amendments.find((a) => a.id === selectedId) ?? proposed[0] ?? settled[0];
  // The backlog is a fact about the project, so it survives this queue being
  // empty — which is exactly when someone wants to know it.
  const backlogCount = amendments.filter((a) => a.status === "approved").length;

  return (
    <div className="flex h-full min-h-0">
      <aside className="flex w-96 flex-none flex-col border-r border-border bg-card">
        <div className="flex-none border-b border-border px-4 py-3.5">
          <h1 className="text-sm font-semibold">Amendments</h1>
          <p className="mt-0.5 text-2xs text-secondary">
            {proposed.length} awaiting approval ·{" "}
            {amendments.filter((a) => a.status === "approved").length} approved and unbatched
          </p>
        </div>

        {proposed.length === 0 ? (
          <NothingAwaiting backlog={backlogCount} />
        ) : (
          <div className="min-h-0 flex-1 overflow-auto p-2">
            {proposed.map((amendment) => (
              <QueueRow
                key={amendment.id}
                amendment={amendment}
                selected={amendment.id === selected?.id}
                onSelect={() => setSelectedId(amendment.id)}
              />
            ))}

            {settled.length > 0 ? (
              <div className="mt-3 px-2.5 py-1.5 text-2xs font-medium tracking-wide text-muted uppercase">
                Approved, not batched
              </div>
            ) : null}
            {settled.map((amendment) => (
              <QueueRow
                key={amendment.id}
                amendment={amendment}
                selected={amendment.id === selected?.id}
                onSelect={() => setSelectedId(amendment.id)}
              />
            ))}
          </div>
        )}
      </aside>

      <div className="min-h-0 flex-1 overflow-auto">
        {selected ? <Detail amendment={selected} /> : <NoSelection />}
      </div>
    </div>
  );
}

function QueueRow({
  amendment,
  selected,
  onSelect,
}: {
  amendment: AmendmentRow;
  selected: boolean;
  onSelect: () => void;
}) {
  const awaitingYou = amendment.approval?.required_approvers.some(
    (approver) => approver.id === "u-rod" && !approver.decision,
  );

  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "mb-1 flex w-full flex-col gap-1.5 rounded-control border px-3 py-2.5 text-left",
        selected ? "border-border-strong bg-sunken" : "border-transparent hover:bg-sunken",
      )}
    >
      <div className="flex items-center gap-1.5">
        <StatusTag amendment={amendment} awaitingYou={awaitingYou} />
        {amendment.conflict_count ? (
          <span className="rounded-pill border border-diff-conflict-border bg-diff-conflict-fill px-1.5 text-[10px] text-diff-conflict-text">
            {amendment.conflict_count} conflict
          </span>
        ) : null}
        <span className="ml-auto text-2xs text-muted">
          {amendment.created_at ? format.at(amendment.created_at) : "in backlog"}
        </span>
      </div>

      <span className="text-xs text-primary">{amendment.summary}</span>

      {amendment.status === "rejected" && amendment.rejected_reason ? (
        <span className="text-2xs text-muted italic">“{amendment.rejected_reason}”</span>
      ) : (
        <div className="flex flex-wrap items-center gap-1 text-2xs text-muted">
          {amendment.layers.map((layer) => (
            <LayerBadge key={layer} layer={layer} />
          ))}
          <span className="tnum">{changeSummary(amendment)}</span>
          {amendment.proposer ? <span>· {amendment.proposer}</span> : null}
        </div>
      )}
    </button>
  );
}

/** `+1 ~2 −1` — creates, revises, retires, as the diff counts them. */
function changeSummary(amendment: AmendmentRow): string {
  const diff = amendment.payload?.type === "spec_diff" ? amendment.payload.diff : null;
  if (!diff) return `${amendment.change_count} changes`;
  return `+${diff.creates.length} ~${diff.revises.length} −${diff.retires.length}`;
}

function StatusTag({
  amendment,
  awaitingYou,
}: {
  amendment: AmendmentRow;
  awaitingYou?: boolean;
}) {
  if (amendment.status === "approved") {
    return (
      <span className="rounded-pill border border-status-passed-border bg-status-passed-fill px-1.5 text-[10px] text-status-passed-text">
        approved
      </span>
    );
  }
  if (amendment.status === "rejected") {
    return (
      <span className="rounded-pill border border-status-failed-border bg-status-failed-fill px-1.5 text-[10px] text-status-failed-text">
        rejected
      </span>
    );
  }
  return (
    <span
      className={cn(
        "rounded-pill border px-1.5 text-[10px]",
        awaitingYou
          ? "border-accent-border bg-accent-fill text-accent-text"
          : "border-border bg-sunken text-secondary",
      )}
    >
      {awaitingYou ? "awaiting you" : "awaiting"}
    </span>
  );
}

function NothingAwaiting({ backlog }: { backlog: number }) {
  return (
    <div className="flex flex-1 flex-col items-start gap-3 px-4 py-8">
      <span className="text-sm font-medium text-primary">Nothing is waiting on you.</span>
      <p className="text-xs text-secondary">
        Amendments arrive here when a conversation settles. The backlog holds {backlog} approved
        amendment{backlog === 1 ? "" : "s"} ready to batch.
      </p>
      <div className="flex gap-2">
        <Button size="sm" variant="needs-you" asChild>
          <Link href="/conversation">Open a conversation</Link>
        </Button>
        <Button size="sm" variant="outline" asChild>
          <Link href="/batches">See the backlog</Link>
        </Button>
      </div>
    </div>
  );
}

function NoSelection() {
  return (
    <div className="flex h-full items-center justify-center">
      <p className="text-sm text-muted">Select an amendment to review it.</p>
    </div>
  );
}

function Detail({ amendment }: { amendment: AmendmentRow }) {
  const dispatch = useDispatch();
  const decision = useDecision();
  const diff = amendment.payload?.type === "spec_diff" ? amendment.payload : null;

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-5 px-6 py-6">
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2">
          <SpecId id={amendment.id} />
          <StatusTag amendment={amendment} />
          {amendment.conversation_id ? (
            <Link href="/conversation" className="text-2xs text-accent-text hover:underline">
              from “{amendment.conversation_title}”, turn {amendment.turn_seq} →
            </Link>
          ) : null}
        </div>

        <h2 className="text-base font-medium text-primary">{amendment.summary}</h2>

        {amendment.proposer ? (
          <p className="text-xs text-secondary">
            Proposed by {amendment.proposer}
            {amendment.deployment ? ` (${amendment.deployment})` : ""}
            {amendment.created_at ? ` at ${format.at(amendment.created_at)}` : ""}
            {amendment.snapshot_id ? ` against snapshot ${format.shortId(amendment.snapshot_id)}` : ""}
            .
          </p>
        ) : null}
      </div>

      {diff ? (
        <SpecDiff diff={diff.diff} conflicts={diff.conflicts} summary={diff.summary} />
      ) : (
        <p className="text-sm text-muted">
          This amendment has not been expanded yet. Open it from its conversation to see the
          diff it proposes.
        </p>
      )}

      {amendment.approval ? (
        <ApprovalCard
          approval={{
            ...amendment.approval,
            status:
              decision.state === "approved"
                ? "approved"
                : decision.state === "rejected"
                  ? "rejected"
                  : "awaiting",
            reason: decision.state === "rejected" ? decision.reason : undefined,
          }}
          onApprove={() => dispatch("df.amendments.approve", { id: amendment.id })}
          onReject={(reason) => dispatch("df.amendments.reject", { id: amendment.id, reason })}
        />
      ) : null}

      {decision.state === "approved" ? (
        <div className="flex items-center gap-3 rounded-card border border-status-passed-border bg-status-passed-fill px-3.5 py-3">
          <div className="flex flex-col">
            <span className="text-sm text-status-passed-text">
              Approved. The March rule is retired.
            </span>
            <span className="text-2xs text-secondary">
              In the backlog, ready to batch. Order will be proposed from depends_on edges.
            </span>
          </div>
          <Button size="sm" variant="needs-you" asChild className="ml-auto shrink-0">
            <Link href="/batches">Compose a batch</Link>
          </Button>
        </div>
      ) : null}
    </div>
  );
}

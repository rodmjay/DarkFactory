"use client";

/**
 * The spec graph.
 *
 * Three columns: what the graph is made of, the nodes themselves, and one
 * node in full. The right pane leads with revisions because the question
 * this screen exists to answer is "why does this rule say what it says",
 * and the answer is the rationale on each revision (ADR-0016 as amended) —
 * not the current text, which is the part everyone already has.
 */

import * as React from "react";

import {
  Button,
  Input,
  LayerBadge,
  PayloadRenderer,
  Rationale,
  SpecId,
  SpecNodeCard,
  cn,
  diffBand,
  diffMarker,
  format,
} from "@dark-factory/ui";

import { usePrototype, useQuery, useScenario, useSpecs } from "@/lib/local/store";
import type { GraphNodeRow, SpecNodeRow } from "@/lib/local/schema";
import type { Result } from "@/lib/factory/actions";
import type { FactoryIntakeSource } from "@/lib/factory/mcp";

/**
 * Without `?state=` the screen reads the factory. A project whose
 * specifications were imported has them long before it has nodes, so the
 * screen leads with what was imported and how far along each document is,
 * with the graph's own nodes counted beside it — see docs/screens/specs.md,
 * "Imported, awaiting the graph". The prototype stays under `?state=`.
 */
export default function SpecsPage() {
  return usePrototype() ? <PrototypeSpecsPage /> : <LiveSpecsPage />;
}

function LiveSpecsPage() {
  const imported = useQuery((db) => db.imported_specs);
  const nodes = useQuery((db) => db.graph_nodes);
  const specs = useSpecs();
  const [selectedId, setSelectedId] = React.useState<string | null>(null);
  const [search, setSearch] = React.useState("");
  const [detail, setDetail] = React.useState<{ id: string; result: Result<FactoryIntakeSource> } | null>(null);

  const activeId = selectedId ?? imported[0]?.id ?? null;
  const active = imported.find((s) => s.id === activeId);
  const revision = active?.draft_revision ?? 0;
  const openCount = active?.open_questions ?? 0;
  const status = active?.status ?? "";

  React.useEffect(() => {
    if (activeId === null) return;
    let cancelled = false;
    specs.openSource(activeId).then((result) => {
      if (!cancelled) setDetail({ id: activeId, result });
    });
    return () => {
      cancelled = true;
    };
    // Re-read when the document moves on — a new draft, a new question, a
    // new status — not on every poll; `specs` changes identity on each one.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId, revision, openCount, status]);

  const needle = search.trim().toLowerCase();
  const shown = needle
    ? imported.filter((s) => s.title.toLowerCase().includes(needle) || s.source_ref.toLowerCase().includes(needle))
    : imported;
  const extracted = imported.filter((s) => s.status === "extracted" || s.status === "proposed").length;
  const draftNodes = imported.reduce((sum, s) => sum + s.nodes, 0);
  const open = imported.reduce((sum, s) => sum + s.open_questions, 0);
  const data = detail?.id === activeId && detail.result.ok ? detail.result.data : null;
  const error = detail?.id === activeId && !detail.result.ok ? detail.result.error : null;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-none items-center gap-4 border-b border-border px-5 py-3">
        <h1 className="text-sm font-semibold">Spec graph</h1>
        <span className="text-2xs text-muted">
          {nodes.length} nodes in the graph · {imported.length} imported specs · {extracted} extracted ·{" "}
          {draftNodes} draft nodes · {open} open questions
        </span>
      </div>

      <div className="flex min-h-0 flex-1">
        <aside className="flex w-72 flex-none flex-col border-r border-border bg-card">
          <div className="flex-none border-b border-border px-3 py-3">
            <div className="text-xs font-medium text-secondary">In the graph</div>
            {nodes.length === 0 ? (
              <p className="mt-1 text-2xs text-muted">
                No nodes yet. A document&apos;s draft becomes nodes when it is proposed and approved.
              </p>
            ) : (
              <LayerCounts nodes={nodes} />
            )}
          </div>

          <div className="flex-none px-2.5 pt-2.5">
            <Input
              placeholder="Search imported specs"
              className="h-8 text-xs"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <div className="flex-none px-3 pt-2.5 pb-1 text-xs font-medium text-secondary">
            Imported · {imported.length}
          </div>

          <div className="min-h-0 flex-1 overflow-auto px-2 pb-2">
            {!specs.hydrated ? (
              <p className="px-2.5 py-2 text-2xs text-muted">Reading from the factory…</p>
            ) : imported.length === 0 ? (
              <p className="px-2.5 py-2 text-2xs text-muted">
                Nothing imported yet. Ingest specs from the Sources panel on the conversation screen.
              </p>
            ) : null}
            {shown.map((source) => (
              <button
                key={source.id}
                type="button"
                onClick={() => setSelectedId(source.id)}
                className={cn(
                  "flex w-full flex-col gap-0.5 rounded-control px-2.5 py-2 text-left",
                  source.id === activeId ? "bg-sunken" : "hover:bg-sunken",
                )}
              >
                <span className="flex items-center gap-1.5">
                  <StatusPill status={source.status} />
                  <span className="truncate text-xs font-medium text-primary">{source.title}</span>
                </span>
                <span className="truncate text-2xs text-muted">
                  {shortRef(source.source_ref)}
                  {source.nodes > 0 ? ` · ${source.nodes} nodes` : ""}
                  {source.open_questions > 0 ? ` · ${source.open_questions} open` : ""}
                </span>
              </button>
            ))}
          </div>
        </aside>

        <section className="flex min-w-0 flex-1 flex-col gap-3 overflow-auto px-5 py-4">
          {active ? (
            <>
              <div className="flex flex-wrap items-baseline gap-2">
                <h2 className="text-sm font-semibold text-primary">{active.title}</h2>
                <span className="font-mono text-2xs text-muted">{shortRef(active.source_ref)}</span>
                <StatusPill status={active.status} />
                {active.draft_revision > 0 ? (
                  <span className="text-2xs text-muted">draft revision {active.draft_revision}</span>
                ) : null}
              </div>

              {error ? <p className="text-2xs text-status-failed-text">{error}</p> : null}
              {active.failure ? (
                <p className="rounded-control border border-status-failed-border bg-status-failed-fill px-3 py-2 text-2xs whitespace-pre-wrap text-status-failed-text">
                  Extraction was refused: {active.failure}
                </p>
              ) : null}

              <DraftNodes status={active.status} data={data} />
              <Questions status={active.status} data={data} />
            </>
          ) : null}
        </section>

        <section className="flex w-[40%] flex-none flex-col gap-2 overflow-auto border-l border-border px-5 py-4">
          <span className="text-xs font-medium text-secondary">Source document</span>
          {data ? (
            <PayloadRenderer payloads={[{ type: "markdown", text: withoutFrontMatter(data.content) }]} />
          ) : active ? (
            <p className="text-2xs text-muted">Reading…</p>
          ) : null}
        </section>
      </div>
    </div>
  );
}

function DraftNodes({ status, data }: { status: string; data: FactoryIntakeSource | null }) {
  const creates = data?.draft?.creates ?? [];

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-xs font-medium text-secondary">
        Draft nodes{creates.length > 0 ? ` · ${creates.length}` : ""}
      </h3>
      {status === "pending" ? (
        <p className="text-2xs text-muted">
          Not extracted yet. Extraction turns this document into draft nodes — one rule, behaviour or
          constraint each — and raises what it leaves open as questions.
        </p>
      ) : !data ? (
        <p className="text-2xs text-muted">Reading…</p>
      ) : creates.length === 0 ? (
        <p className="text-2xs text-muted">This document yielded no nodes.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {creates.map((node, index) => (
            <li key={index} className="flex flex-col gap-1 rounded-card border border-border bg-card px-3 py-2">
              <span className="flex items-center gap-1.5">
                <LayerBadge layer={node.layer} />
                <span className="text-2xs text-muted">{node.kind}</span>
              </span>
              <span className="text-sm text-primary">{node.text}</span>
              {node.rationale ? <span className="text-2xs text-muted">{node.rationale}</span> : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Questions({ status, data }: { status: string; data: FactoryIntakeSource | null }) {
  const questions = data?.questions ?? [];
  const open = questions.filter((q) => q.status === "open").length;

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-xs font-medium text-secondary">
        Questions{questions.length > 0 ? ` · ${open} open of ${questions.length}` : ""}
      </h3>
      {questions.length === 0 ? (
        <p className="text-2xs text-muted">
          {status === "pending" ? "None yet." : data ? "Extraction found nothing it would have to guess at." : ""}
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {questions.map((question) => (
            <li key={question.question_id} className="flex flex-col gap-1 rounded-card border border-border px-3 py-2">
              <span className="flex items-center gap-1.5 text-2xs">
                <span className="rounded-pill border border-border bg-sunken px-1.5 text-[10px] text-secondary">
                  {question.kind.replaceAll("_", " ")}
                </span>
                <span className="text-muted">{question.status}</span>
              </span>
              <span className="text-sm text-primary">{question.question}</span>
              {question.quote ? (
                <span className="border-l-2 border-border pl-2 text-2xs text-muted">{question.quote}</span>
              ) : null}
              {question.answer ? <span className="text-2xs text-secondary">Answer: {question.answer}</span> : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function LayerCounts({ nodes }: { nodes: GraphNodeRow[] }) {
  const counts = new Map<string, number>();
  for (const node of nodes) counts.set(node.layer, (counts.get(node.layer) ?? 0) + 1);

  return (
    <ul className="mt-1.5 flex flex-col gap-1">
      {[...counts.entries()].sort().map(([layer, count]) => (
        <li key={layer} className="flex items-center gap-1.5 text-2xs">
          <LayerBadge layer={layer} />
          <span className="ml-auto tnum text-muted">{count}</span>
        </li>
      ))}
    </ul>
  );
}

function StatusPill({ status }: { status: string }) {
  const tone =
    status === "extracted"
      ? "border-status-passed-border bg-status-passed-fill text-status-passed-text"
      : status === "proposed"
        ? "border-accent-border bg-accent-fill text-accent-text"
        : status === "failed"
          ? "border-status-failed-border bg-status-failed-fill text-status-failed-text"
          : "border-border bg-sunken text-muted";
  return <span className={cn("shrink-0 rounded-pill border px-1.5 text-[10px]", tone)}>{status}</span>;
}

/** `moonbeam-specs:drones/0079-the-working-swarm` → `drones/0079-the-working-swarm`. */
function shortRef(sourceRef: string): string {
  const colon = sourceRef.indexOf(":");
  return colon < 0 ? sourceRef : sourceRef.slice(colon + 1);
}

/** The metadata block is for tools; a reader wants the document. */
function withoutFrontMatter(content: string): string {
  return content.replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n?/, "");
}

function PrototypeSpecsPage() {
  const graph = useQuery((db) => db.graph);
  const nodes = useQuery((db) => db.spec_nodes);
  const [scenario, setScenario] = useScenario();
  const [selectedId, setSelectedId] = React.useState(nodes[0]?.spec_id ?? "");

  const selected = nodes.find((node) => node.spec_id === selectedId) ?? nodes[0];
  const diffView = scenario === "graph-diff";

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-none items-center gap-4 border-b border-border px-5 py-3">
        <h1 className="text-sm font-semibold">Spec graph</h1>
        <span className="text-2xs text-muted">
          {graph.node_count} nodes · {graph.edge_count.toLocaleString("en-GB")} edges ·{" "}
          {graph.layers.length} layers
        </span>

        <div className="flex items-center gap-2">
          <span className="text-2xs text-muted">Snapshot</span>
          <button
            type="button"
            className="flex h-6 items-center gap-1 rounded-control border border-border bg-sunken px-2 font-mono text-2xs text-secondary hover:text-primary"
          >
            <SpecId id={graph.snapshot_id} />
            <span className="text-muted">▾</span>
          </button>
          <Button
            variant={diffView ? "default" : "outline"}
            size="sm"
            onClick={() => setScenario(diffView ? "settled" : "graph-diff")}
          >
            Compare…
          </Button>
        </div>

        <Input
          placeholder="Search 412 nodes — text, spec id, or edge kind"
          className="ml-auto h-8 max-w-md text-xs"
        />
      </div>

      <div className="flex min-h-0 flex-1">
        <Rail />
        <div className="min-h-0 flex-1 overflow-auto px-5 py-4">
          {diffView ? <SnapshotDiff onBack={() => setScenario("settled")} /> : (
            <Browse nodes={nodes} selectedId={selected?.spec_id} onSelect={setSelectedId} />
          )}
        </div>
        {selected && !diffView ? <NodeDetail node={selected} /> : null}
      </div>
    </div>
  );
}

// -------------------------------------------------------------------- rail

function Rail() {
  const graph = useQuery((db) => db.graph);

  return (
    <aside className="flex w-56 flex-none flex-col gap-4 overflow-auto border-r border-border bg-card px-3 py-4">
      <Section title="Layers">
        <ul className="flex flex-col gap-0.5">
          {graph.layers.map((entry) => (
            <li key={entry.layer}>
              <button
                type="button"
                className="flex w-full items-center gap-2 rounded-control px-2 py-1 hover:bg-sunken"
              >
                <LayerBadge layer={entry.layer} />
                <span className="tnum ml-auto text-2xs text-muted">{entry.count}</span>
              </button>
            </li>
          ))}
        </ul>
      </Section>

      <Section title="Edge kinds">
        <ul className="flex flex-col gap-0.5">
          {graph.edge_kinds.map((entry) => (
            <li key={entry.kind} className="flex items-center px-2 py-0.5 font-mono text-2xs">
              <span className="text-secondary">{entry.kind}</span>
              <span className="tnum ml-auto text-muted">{entry.count}</span>
            </li>
          ))}
        </ul>
      </Section>

      <Section title="Health">
        <div className="flex flex-col gap-1">
          <HealthRow label="drifted" count={graph.drifted} tone="changed" />
          <HealthRow label="no implementation" count={graph.unimplemented} tone="muted" />
        </div>
      </Section>
    </aside>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className="px-2 text-2xs font-medium tracking-wide text-muted uppercase">{title}</span>
      {children}
    </div>
  );
}

function HealthRow({
  label,
  count,
  tone,
}: {
  label: string;
  count: number;
  tone: "changed" | "muted";
}) {
  return (
    <span className="flex items-center gap-2 px-2 text-2xs">
      <span
        className={cn(
          "rounded-pill border px-1.5 text-[10px]",
          tone === "changed"
            ? "border-diff-changed-border bg-diff-changed-fill text-diff-changed-text"
            : "border-border bg-sunken text-secondary",
        )}
      >
        {label}
      </span>
      <span className="tnum ml-auto text-muted">{count}</span>
    </span>
  );
}

// ------------------------------------------------------------------ browse

function Browse({
  nodes,
  selectedId,
  onSelect,
}: {
  nodes: SpecNodeRow[];
  selectedId?: string;
  onSelect: (id: string) => void;
}) {
  const graph = useQuery((db) => db.graph);
  const byLayer = new Map<string, SpecNodeRow[]>();
  for (const node of nodes) {
    byLayer.set(node.layer, [...(byLayer.get(node.layer) ?? []), node]);
  }

  return (
    <div className="flex flex-col gap-5">
      {[...byLayer.entries()].map(([layer, layerNodes]) => (
        <section key={layer} className="flex flex-col gap-2">
          <div className="flex items-baseline gap-2">
            <LayerBadge layer={layer} />
            <span className="text-2xs text-muted">
              {graph.layers.find((entry) => entry.layer === layer)?.count ?? layerNodes.length}{" "}
              nodes · showing {layerNodes.length}
            </span>
          </div>
          <ul className="flex flex-col gap-2">
            {layerNodes.map((node) => (
              <li key={node.spec_id}>
                <button type="button" onClick={() => onSelect(node.spec_id)} className="w-full text-left">
                  {/* Revisions belong in the detail pane. Passing them here
                      too would print the whole history twice on one screen. */}
                  <SpecNodeCard
                    node={{ ...node, revisions: undefined }}
                    selected={node.spec_id === selectedId}
                  />
                  {node.drift_detail ? (
                    <p className="mt-1 px-3 text-2xs text-diff-changed-text">{node.drift_detail}</p>
                  ) : null}
                </button>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

// --------------------------------------------------------- snapshot compare

function SnapshotDiff({ onBack }: { onBack: () => void }) {
  const diff = useQuery((db) => db.snapshot_diff);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2 text-2xs">
        <Button variant="ghost" size="sm" onClick={onBack}>
          ← Back to browse
        </Button>
        <SpecId id={diff.from.id} />
        <span className="text-muted">
          {diff.from.at} · {diff.from.note}
        </span>
        <span className="text-muted">→</span>
        <SpecId id={diff.to.id} />
        <span className="rounded-pill border border-border bg-sunken px-1.5 text-[10px] text-secondary">
          {diff.to.label}
        </span>
      </div>

      <p className="text-sm text-secondary">{diff.summary}</p>

      <div className="flex flex-col gap-2">
        {diff.changes.map((change) => (
          <div
            key={change.spec_id}
            className={cn("flex gap-3 rounded-card border px-3 py-2.5", diffBand[change.kind])}
          >
            <span className={cn("font-mono text-sm", diffMarker[change.kind])}>
              {change.kind === "added" ? "+" : change.kind === "removed" ? "−" : "~"}
            </span>
            <div className="flex min-w-0 flex-col gap-1">
              <div className="flex items-center gap-2">
                <SpecId id={change.spec_id} />
                <LayerBadge layer={change.layer} />
              </div>
              <p className="text-sm text-primary">{change.text}</p>
              {change.was ? (
                <p className="text-2xs text-muted line-through">was: {change.was}</p>
              ) : null}
            </div>
          </div>
        ))}
      </div>

      <Button variant="outline" size="sm" className="w-fit">
        Export as markdown
      </Button>
    </div>
  );
}

// ------------------------------------------------------------- node detail

function NodeDetail({ node }: { node: SpecNodeRow }) {
  return (
    <aside className="flex w-96 flex-none flex-col gap-4 overflow-auto border-l border-border bg-card px-4 py-4">
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-1.5">
          <LayerBadge layer={node.layer} />
          <span className="rounded-pill border border-border bg-sunken px-1.5 text-[10px] text-secondary">
            {node.kind}
          </span>
          <SpecId id={node.spec_id} />
        </div>
        <p className="text-sm text-primary">{node.text}</p>
        <div className="flex gap-3 font-mono text-2xs text-muted">
          {node.revision_hash ? <span>revision {node.revision_hash.slice(0, 7)}…</span> : null}
          {node.edges ? (
            <span>
              {node.edges.outgoing} out · {node.edges.incoming} in
            </span>
          ) : null}
        </div>
      </div>

      {node.implemented_by ? (
        <section className="flex flex-col gap-1.5 border-t border-border pt-3">
          <span className="text-2xs font-medium text-secondary">Implemented by</span>
          <ul className="flex flex-col gap-1">
            {node.implemented_by.map((entry) => (
              <li key={entry.path} className="flex items-center gap-2 text-2xs">
                <span className="truncate font-mono text-secondary">{entry.path}</span>
                <span className="ml-auto shrink-0 text-status-passed-text">{entry.status}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {node.revisions ? (
        <section className="flex flex-col gap-2 border-t border-border pt-3">
          <span className="text-2xs font-medium text-secondary">Revisions</span>
          <ol className="flex flex-col gap-3">
            {[...node.revisions].reverse().map((revision, index) => (
              <li key={revision.hash} className="flex flex-col gap-1">
                <div className="flex items-center gap-2 font-mono text-2xs">
                  <span className="text-primary">{revision.hash}</span>
                  <span className="text-muted">{format.at(revision.created_at)}</span>
                  {index === 0 ? (
                    <span className="rounded-pill border border-accent-border bg-accent-fill px-1.5 font-sans text-[10px] text-accent-text">
                      current
                    </span>
                  ) : null}
                </div>
                <p className="text-2xs text-secondary">{revision.text}</p>
                <Rationale rationale={revision.rationale} className="text-2xs" />
                {revision.approved_by ? (
                  <p className="text-[10px] text-muted">
                    proposed by {revision.actor_id} · approved by {revision.approved_by}
                  </p>
                ) : null}
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      {node.edge_summary ? (
        <section className="flex flex-col gap-1.5 border-t border-border pt-3">
          <span className="text-2xs font-medium text-secondary">Edges</span>
          <ul className="flex flex-col gap-1">
            {node.edge_summary.map((edge) => (
              <li key={edge.kind} className="flex gap-2 text-2xs">
                <span className="shrink-0 font-mono text-muted">{edge.kind}</span>
                <span className="truncate text-secondary">{edge.text}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </aside>
  );
}

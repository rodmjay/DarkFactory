"use client";

/**
 * Step 1 — Converse.
 *
 * The thread is not a chat log with special cases bolted on: every turn is
 * a list of ADR-0021 payloads, and prose, a retrieved neighbourhood, a
 * table, a clarifying form, a spec diff, an approval gate, a parked run's
 * timeline and a patch all arrive through the same renderer. That is the
 * whole reason the vocabulary exists, and this screen is where it earns it.
 */

import * as React from "react";
import Link from "next/link";
import { ArrowDown, Plus } from "lucide-react";

import {
  ApprovalCard,
  Avatar,
  AvatarFallback,
  Button,
  Card,
  Input,
  LayerBadge,
  PayloadRenderer,
  SpecId,
  Textarea,
  cn,
  format,
} from "@dark-factory/ui";
import type { Payload } from "@dark-factory/ui";

import { useDecision, useDispatch, useQuery } from "@/lib/local/store";
import type { TurnRow } from "@/lib/local/schema";

export default function ConversationPage() {
  const conversations = useQuery((db) => db.conversations);
  const conversation = conversations[0];
  const turns = useQuery((db) => db.turns);
  const thinking = useQuery((db) => db.thinking);
  const [panelOpen, setPanelOpen] = React.useState(true);

  /* A conversation opens at its newest turn. Landing at the top means the
   * thing that just happened — a proposal, a run parking on a question — is
   * the one thing the reader has to go looking for. */
  const threadRef = React.useRef<HTMLDivElement>(null);
  React.useEffect(() => {
    const el = threadRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [turns]);

  return (
    <div className="flex h-full min-h-0">
      <ConversationList />

      <main className="flex min-w-0 flex-1 flex-col">
        <div className="flex h-12 flex-none items-center gap-3 border-b border-border px-5">
          <h1 className="text-sm font-semibold">{conversation.title}</h1>
          <SpecId id={conversation.id} className="text-2xs" />
          <span className="text-2xs text-muted">
            {conversation.deployment} · {conversation.turn_count} turns
          </span>
          <Button
            variant="ghost"
            size="sm"
            className="ml-auto"
            onClick={() => setPanelOpen((open) => !open)}
          >
            Context panel
          </Button>
        </div>

        <div ref={threadRef} className="min-h-0 flex-1 overflow-auto px-5 py-5">
          <div className="mx-auto flex max-w-3xl flex-col gap-5">
            {turns.length === 0 ? <EmptyThread /> : turns.map((turn) => <Turn key={turn.id} turn={turn} />)}
            {thinking ? <Thinking /> : null}
          </div>
        </div>

        <Composer />
      </main>

      {panelOpen ? <LookingAt /> : null}
    </div>
  );
}

// ------------------------------------------------------------------ list

function ConversationList() {
  const conversations = useQuery((db) => db.conversations);
  const dispatch = useDispatch();

  return (
    <aside className="flex w-60 flex-none flex-col border-r border-border bg-card">
      <div className="flex h-12 flex-none items-center justify-between border-b border-border px-3">
        <span className="text-xs font-medium text-secondary">Conversations</span>
        <button
          type="button"
          title="New conversation"
          onClick={() => dispatch("df.conversations.create")}
          className="inline-flex size-6 items-center justify-center rounded-control text-secondary hover:bg-sunken hover:text-primary"
        >
          <Plus aria-hidden className="size-3.5" />
          <span className="sr-only">New conversation</span>
        </button>
      </div>

      <div className="flex-none px-2.5 py-2.5">
        <Input placeholder="Search conversations" className="h-8 text-xs" />
      </div>

      <div className="min-h-0 flex-1 overflow-auto px-2 pb-2">
        {conversations.map((conversation, index) => (
          <button
            key={conversation.id}
            type="button"
            className={cn(
              "flex w-full flex-col gap-0.5 rounded-control px-2.5 py-2 text-left",
              index === 0 ? "bg-sunken" : "hover:bg-sunken",
            )}
          >
            <span className="truncate text-xs font-medium text-primary">{conversation.title}</span>
            <span className="truncate text-2xs text-muted">{conversation.subtitle}</span>
          </button>
        ))}
      </div>
    </aside>
  );
}

// ----------------------------------------------------------------- thread

function EmptyThread() {
  const suggestions = [
    "What governs renewal today?",
    "Where has the code drifted from the graph?",
    "Summarise what shipped last week",
  ];

  return (
    <div className="mx-auto flex max-w-xl flex-col gap-4 py-16 text-center">
      <h2 className="text-lg font-medium text-primary">
        Ask the architect anything about this stack.
      </h2>
      <p className="text-sm text-secondary">
        It has read all 412 spec nodes in this project and the standards indexed from two
        servers. When the conversation settles it will propose an amendment; nothing changes
        the graph until someone approves it.
      </p>
      <div className="flex flex-wrap justify-center gap-2">
        {suggestions.map((suggestion) => (
          <button
            key={suggestion}
            type="button"
            className="rounded-pill border border-border bg-card px-3 py-1.5 text-xs text-secondary hover:border-border-strong hover:text-primary"
          >
            {suggestion}
          </button>
        ))}
      </div>
    </div>
  );
}

function Thinking() {
  return (
    <div className="flex items-center gap-2.5">
      <Initials>AR</Initials>
      <span className="flex items-center gap-2 text-xs text-secondary">
        <span aria-hidden className="size-1.5 rounded-pill bg-accent breathe" />
        Traversing the spec neighbourhood — 3 layers routed, 2 standards servers consulted
      </span>
    </div>
  );
}

function Turn({ turn }: { turn: TurnRow }) {
  if (turn.author === "human") return <HumanTurn turn={turn} />;
  return <AgentTurn turn={turn} />;
}

function HumanTurn({ turn }: { turn: TurnRow }) {
  const text = turn.payloads[0]?.type === "markdown" ? turn.payloads[0].text : "";

  return (
    <div className="flex justify-end">
      <div
        className={cn(
          "flex max-w-xl flex-col gap-1 rounded-card border px-3.5 py-2.5",
          turn.pending ? "border-dashed border-border-strong bg-sunken" : "border-border bg-card",
        )}
      >
        <p className="text-sm text-primary">{text}</p>
        <span className="text-2xs text-muted">
          {turn.author_name} · {turn.pending ? "sending…" : format.at(turn.at)}
        </span>
      </div>
    </div>
  );
}

function AgentTurn({ turn }: { turn: TurnRow }) {
  const isRun = turn.author === "run";

  return (
    <div className="flex gap-2.5">
      <Initials>{isRun ? "RN" : "AR"}</Initials>
      <div className="flex min-w-0 flex-1 flex-col gap-3">
        <div className="flex items-baseline gap-2">
          {isRun ? (
            <>
              <SpecId id={turn.run_id ?? ""} className="text-2xs" />
              <span className="text-2xs text-muted">{turn.batch_label}</span>
            </>
          ) : (
            <>
              <span className="text-xs font-medium text-primary">{turn.author_name}</span>
              <span className="text-2xs text-muted">
                {turn.deployment} · {format.at(turn.at)}
              </span>
            </>
          )}
        </div>

        <TurnPayloads payloads={turn.payloads} />

        {turn.cost ? <CostStrip turn={turn} /> : null}
      </div>
    </div>
  );
}

/**
 * Everything goes through `PayloadRenderer` except the approval gate, which
 * needs handlers this screen owns — approving is a `df.*` command, not a
 * visual state. The design system draws it; the app decides what it means.
 */
function TurnPayloads({ payloads }: { payloads: Payload[] }) {
  const dispatch = useDispatch();
  const decision = useDecision();

  return (
    <div className="flex flex-col gap-4">
      {payloads.map((payload, index) => {
        if (payload.type !== "approval_card") {
          return <PayloadRenderer key={index} payloads={[payload]} />;
        }

        const approval = {
          ...payload.approval,
          status:
            decision.state === "approved"
              ? ("approved" as const)
              : decision.state === "rejected"
                ? ("rejected" as const)
                : ("awaiting" as const),
          reason: decision.state === "rejected" ? decision.reason : undefined,
        };

        return (
          <div key={index} className="flex flex-col gap-2">
            <ApprovalCard
              approval={approval}
              onApprove={() => dispatch("df.amendments.approve", { id: payload.approval.target_id })}
              onReject={(reason) =>
                dispatch("df.amendments.reject", { id: payload.approval.target_id, reason })
              }
            />
            {decision.state === "undecided" ? (
              <p className="text-2xs text-muted">
                Approving queues it into the backlog, not into a run.
              </p>
            ) : null}
            {decision.state === "approved" ? (
              <div className="flex items-center gap-2 text-2xs text-muted">
                <span>Amendment is in the backlog. It enters a run when someone batches it.</span>
                <Link href="/batches" className="text-accent-text hover:underline">
                  Open backlog
                </Link>
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

/** Tokens this turn, against the last one. A number with no comparison is
 *  a number nobody can act on. */
function CostStrip({ turn }: { turn: TurnRow }) {
  const total =
    (turn.cost?.input_tokens ?? 0) +
    (turn.cost?.cached_input_tokens ?? 0) +
    (turn.cost?.output_tokens ?? 0) +
    (turn.cost?.thinking_tokens ?? 0);

  return (
    <Card className="flex w-fit flex-row items-center gap-4 px-3 py-2">
      <div className="flex flex-col">
        <span className="text-2xs text-muted">Tokens this turn</span>
        <span className="tnum text-sm font-medium text-primary">
          {format.tokensExact(total)}
        </span>
      </div>
      <div className="flex flex-col items-end">
        <span className="text-2xs text-muted">vs last turn</span>
        <span className="flex items-center gap-1 text-2xs text-status-passed-text">
          <ArrowDown aria-hidden className="size-3" />
          {format.delta(turn.cost_delta)}
        </span>
      </div>
    </Card>
  );
}

function Initials({ children }: { children: React.ReactNode }) {
  return (
    <Avatar className="size-6 flex-none">
      <AvatarFallback className="text-[9px]">{children}</AvatarFallback>
    </Avatar>
  );
}

// --------------------------------------------------------------- composer

function Composer() {
  const dispatch = useDispatch();
  const snapshot = useQuery((db) => db.project?.snapshot_id ?? "");
  const [draft, setDraft] = React.useState("");

  function send() {
    if (!draft.trim()) return;
    dispatch("df.conversations.send", { text: draft });
    setDraft("");
  }

  return (
    <div className="flex-none border-t border-border bg-card px-5 py-3">
      <div className="mx-auto flex max-w-3xl flex-col gap-2">
        <Textarea
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && (event.metaKey || event.ctrlKey)) send();
          }}
          placeholder="Ask the architect, or describe what should be true."
          className="min-h-[68px] resize-none"
        />
        <div className="flex items-center gap-3 text-2xs text-muted">
          <span className="flex items-center gap-1">
            snapshot <SpecId id={snapshot} />
          </span>
          <span>Retrieval scoped to product, data, api · 2 standards servers indexed</span>
          <span className="ml-auto">⌘↵ to send</span>
          <Button size="sm" variant="needs-you" onClick={send} disabled={!draft.trim()}>
            Send
          </Button>
        </div>
      </div>
    </div>
  );
}

// ----------------------------------------------------------- context panel

/** What the architect actually retrieved. The panel exists so that "why did
 *  it say that" has an answer that is not a guess. */
function LookingAt() {
  const retrieval = useQuery((db) => db.retrieval[0]);
  if (!retrieval) return null;

  const cost = retrieval.cost;
  const cached = cost.cached_input_tokens ?? 0;

  return (
    <aside className="flex w-80 flex-none flex-col gap-4 overflow-auto border-l border-border bg-card px-4 py-4">
      <div>
        <div className="text-xs font-medium text-primary">Looking at</div>
        <p className="mt-0.5 text-2xs text-muted">
          What the architect retrieved for the turn at {format.at(retrieval.at)}.
        </p>
      </div>

      <section className="flex flex-col gap-2">
        <div className="flex items-baseline justify-between">
          <span className="text-2xs font-medium text-secondary">Spec neighbourhood</span>
          <span className="text-2xs text-muted">
            {retrieval.neighbourhood.length} of {retrieval.node_total}
          </span>
        </div>
        <ul className="flex flex-col gap-2">
          {retrieval.neighbourhood.map((node) => (
            <li key={node.spec_id} className="flex flex-col gap-1">
              <div className="flex items-center gap-1.5">
                <LayerBadge layer={node.layer} />
                <SpecId id={node.spec_id} />
                {node.drifted ? (
                  <span className="rounded-pill border border-diff-changed-border bg-diff-changed-fill px-1.5 text-[10px] text-diff-changed-text">
                    drifted
                  </span>
                ) : null}
              </div>
              <span className="text-2xs text-secondary">{node.text}</span>
            </li>
          ))}
        </ul>
      </section>

      <section className="flex flex-col gap-2 border-t border-border pt-3">
        <span className="text-2xs font-medium text-secondary">Standards consulted</span>
        <ul className="flex flex-col gap-1.5">
          {retrieval.standards.map((server) => (
            <li key={server.name} className="flex items-center gap-1.5 text-2xs">
              <span
                className={cn(
                  "rounded-pill border px-1.5 text-[10px]",
                  server.health === "conformant"
                    ? "border-server-conformant-border bg-server-conformant-fill text-server-conformant-text"
                    : "border-server-degraded-border bg-server-degraded-fill text-server-degraded-text",
                )}
              >
                {server.health}
              </span>
              <span className="text-primary">{server.name}</span>
              <span className="ml-auto text-muted">{server.detail}</span>
            </li>
          ))}
        </ul>
        {retrieval.standards_note ? (
          <p className="text-[10px] text-muted">{retrieval.standards_note}</p>
        ) : null}
      </section>

      <section className="flex flex-col gap-2 border-t border-border pt-3">
        <span className="text-2xs font-medium text-secondary">Cost of this turn</span>
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-2xs">
          <Figure label="input, uncached" value={format.tokensExact(cost.input_tokens)} />
          <Figure label="input, cached" value={format.tokensExact(cached)} />
          <Figure label="thinking" value={format.tokensExact(cost.thinking_tokens ?? 0)} />
          <Figure label="output" value={format.tokensExact(cost.output_tokens)} />
          <Figure label="cost" value={format.usd(cost.usd)} />
        </dl>
        <p className="text-[10px] text-muted">
          {Math.round(retrieval.cache_hit_rate * 100)}% cache hit · {retrieval.proposer} on{" "}
          {retrieval.deployment}, deliberate
        </p>
      </section>

      <section className="flex flex-col gap-2 border-t border-border pt-3">
        <span className="text-2xs font-medium text-secondary">Provenance</span>
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-2xs">
          <Figure label="Proposed by" value={`${retrieval.proposer} (agent)`} />
          <Figure label="Model" value={retrieval.deployment} />
          <Figure label="At" value={format.at(retrieval.at)} />
        </dl>
        <span className="mt-1 text-2xs font-medium text-secondary">Skill revisions in use</span>
        <ul className="flex flex-col gap-0.5">
          {retrieval.skill_revisions.map((skill) => (
            <li key={skill.name} className="font-mono text-[10px] text-secondary">
              {skill.name}
              <span className="text-muted">@{skill.revision}</span>
            </li>
          ))}
        </ul>
      </section>
    </aside>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className="tnum text-right text-primary">{value}</dd>
    </>
  );
}

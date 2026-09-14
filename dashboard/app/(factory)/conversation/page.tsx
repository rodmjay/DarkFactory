"use client";

/**
 * Step 1 — Converse.
 *
 * The thread is not a chat log with special cases bolted on: every turn is
 * a list of ADR-0021 payloads, and prose, a retrieved neighbourhood, a
 * table, a clarifying form, a spec diff, an approval gate, a parked run's
 * timeline and a patch all arrive through the same renderer. That is the
 * whole reason the vocabulary exists, and this screen is where it earns it.
 *
 * Without `?state=` every row here is the factory's: the list from
 * `df.conversations.list`, the thread from `df.conversations.get`, and each
 * send, approve and reject a real call whose result is re-read rather than
 * assumed. Under `?state=` it is the prototype, exactly as designed.
 */

import * as React from "react";
import Link from "next/link";
import { AlertTriangle, ArrowDown, Plus, RefreshCw } from "lucide-react";

import {
  ApprovalCard,
  Avatar,
  AvatarFallback,
  Button,
  Card,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Input,
  Label,
  LayerBadge,
  PayloadRenderer,
  SpecId,
  Textarea,
  cn,
  format,
} from "@dark-factory/ui";
import type { Payload } from "@dark-factory/ui";

import {
  useConversation,
  useDecision,
  useDispatch,
  usePrototype,
  useQuery,
  useSources,
} from "@/lib/local/store";
import type { ConnectionRow, TurnRow } from "@/lib/local/schema";
import type { CorpusArea } from "@/lib/factory/mcp";

export default function ConversationPage() {
  const conversations = useQuery((db) => db.conversations);
  const { activeId, error, dismissError } = useConversation();
  const conversation = conversations.find((c) => c.id === activeId) ?? null;
  const turns = useQuery((db) => db.turns);
  const thinking = useQuery((db) => db.thinking);
  const hasRetrieval = useQuery((db) => db.retrieval.length > 0);
  const [panelOpen, setPanelOpen] = React.useState(true);

  /* A conversation opens at its newest turn. Landing at the top means the
   * thing that just happened — a proposal, a run parking on a question — is
   * the one thing the reader has to go looking for. */
  const threadRef = React.useRef<HTMLDivElement>(null);
  React.useEffect(() => {
    const el = threadRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [turns, thinking]);

  return (
    <div className="flex h-full min-h-0">
      <ConversationList />

      <main className="flex min-w-0 flex-1 flex-col">
        <div className="flex h-12 flex-none items-center gap-3 border-b border-border px-5">
          <h1 className="truncate text-sm font-semibold">{conversation?.title ?? "New conversation"}</h1>
          {conversation ? (
            <>
              <SpecId id={conversation.id} className="text-2xs" />
              <span className="shrink-0 text-2xs text-muted">
                {conversation.deployment} · {conversation.turn_count} turns
              </span>
            </>
          ) : null}
          {/* The panel shows what the architect retrieved for a turn. Until
              the factory returns that, a toggle for an empty panel is a
              control that does nothing. */}
          {hasRetrieval ? (
            <Button
              variant="ghost"
              size="sm"
              className="ml-auto"
              onClick={() => setPanelOpen((open) => !open)}
            >
              Context panel
            </Button>
          ) : null}
        </div>

        <div ref={threadRef} className="min-h-0 flex-1 overflow-auto px-5 py-5">
          <div className="mx-auto flex max-w-3xl flex-col gap-5">
            {turns.length === 0 && !thinking ? (
              <EmptyThread />
            ) : (
              turns.map((turn) => <Turn key={turn.id} turn={turn} />)
            )}
            {thinking ? <Thinking /> : null}
            {error ? <TurnError message={error} onDismiss={dismissError} /> : null}
          </div>
        </div>

        <Composer />
      </main>

      {panelOpen && hasRetrieval ? <LookingAt /> : null}
    </div>
  );
}

// ------------------------------------------------------------------ list

function ConversationList() {
  const conversations = useQuery((db) => db.conversations);
  const projectId = useQuery((db) => db.project?.id ?? "");
  const dispatch = useDispatch();
  const prototype = usePrototype();
  const { activeId, select, hydrated } = useConversation();
  const [search, setSearch] = React.useState("");

  const needle = search.trim().toLowerCase();
  const shown = needle
    ? conversations.filter((c) => c.title.toLowerCase().includes(needle))
    : conversations;

  return (
    <aside className="flex w-60 flex-none flex-col border-r border-border bg-card">
      <div className="flex h-12 flex-none items-center justify-between border-b border-border px-3">
        <span className="text-xs font-medium text-secondary">Conversations</span>
        <button
          type="button"
          title="New conversation"
          // In the product a new conversation is created by its first
          // message, so this only clears the thread to write one.
          onClick={() =>
            prototype ? dispatch("df.conversations.start", { project_id: projectId }) : select(null)
          }
          className="inline-flex size-6 items-center justify-center rounded-control text-secondary hover:bg-sunken hover:text-primary"
        >
          <Plus aria-hidden className="size-3.5" />
          <span className="sr-only">New conversation</span>
        </button>
      </div>

      <div className="flex-none px-2.5 py-2.5">
        <Input
          placeholder="Search conversations"
          className="h-8 text-xs"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </div>

      <div className="min-h-0 flex-1 overflow-auto px-2 pb-2">
        {!hydrated ? (
          <p className="px-2.5 py-2 text-2xs text-muted">Reading from the factory…</p>
        ) : conversations.length === 0 ? (
          <p className="px-2.5 py-2 text-2xs text-muted">No conversations yet.</p>
        ) : null}
        {shown.map((conversation) => (
          <button
            key={conversation.id}
            type="button"
            onClick={() => select(conversation.id)}
            className={cn(
              "flex w-full flex-col gap-0.5 rounded-control px-2.5 py-2 text-left",
              conversation.id === activeId ? "bg-sunken" : "hover:bg-sunken",
            )}
          >
            <span className="truncate text-xs font-medium text-primary">{conversation.title}</span>
            <span className="truncate text-2xs text-muted">{conversation.subtitle}</span>
          </button>
        ))}
      </div>

      {prototype ? null : <SourcesPanel />}
    </aside>
  );
}

// ---------------------------------------------------------------- sources

/**
 * Where this project's specifications come from, and the one button that
 * moves them along (ADR-0038). Which button depends on the state: nothing
 * connected → Connect MCP; connected but down → Reconnect; connected and
 * not yet ingested → Ingest specs; ingested → Check for changes. Every
 * server the project uses is listed with the health monitor's word on it,
 * so "is it wired up" is answered by looking.
 */
function SourcesPanel() {
  const connections = useQuery((db) => db.connections);
  const intakes = useQuery((db) => db.intakes);
  const projectName = useQuery((db) => db.project?.name ?? "");
  const sources = useSources();
  const [dialog, setDialog] = React.useState<"connect" | "ingest" | null>(null);
  const [note, setNoteState] = React.useState<{ text: string; failed?: boolean; at: string } | null>(null);
  const [busy, setBusy] = React.useState(false);
  /** Set when the last check found differences — the moment Update from source is offered. */
  const [stale, setStale] = React.useState(false);

  // Every answer is stamped with when it was given. A check that finds
  // nothing new looks exactly like the last one otherwise, and a button
  // that seems to do nothing when pressed twice reads as broken.
  const setNote = (value: { text: string; failed?: boolean } | null) =>
    setNoteState(value ? { ...value, at: new Date().toISOString() } : null);

  const corpus = connections.find((c) => c.domain === "corpus");
  const imported = corpus ? intakes.find((i) => i.source_server_id === corpus.id) : undefined;

  async function reconnect(serverId: string) {
    setBusy(true);
    setNote(null);
    const result = await sources.check(serverId);
    setBusy(false);
    setNote(
      result.ok
        ? { text: `${result.data.server.name}: ${result.data.server.status.toLowerCase()}`, failed: result.data.server.status === "Unreachable" }
        : { text: result.error, failed: true },
    );
  }

  async function checkForChanges(intakeId: string) {
    setBusy(true);
    setNote(null);
    const result = await sources.drift(intakeId);
    setBusy(false);
    if (!result.ok) return setNote({ text: result.error, failed: true });
    const { changed, added, removed } = result.data;
    setStale(changed.length + added.length + removed.length > 0);
    setNote({
      text:
        changed.length + added.length + removed.length === 0
          ? "No changes at the source since the import."
          : `Since the import: ${changed.length} edited, ${added.length} new, ${removed.length} removed at the source.`,
    });
  }

  async function updateFromSource(intakeId: string) {
    setBusy(true);
    setNote(null);
    const result = await sources.refresh(intakeId);
    setBusy(false);
    if (!result.ok) return setNote({ text: result.error, failed: true });
    const { updated, added, removed, changed_after_extraction: kept } = result.data;
    setStale(kept.length > 0);
    setNote({
      text:
        `Updated from source: ${updated} brought current, ${added} added, ${removed} dropped.` +
        (kept.length > 0
          ? ` ${kept.length} already extracted and left as they were — re-extract them to take the new text.`
          : ""),
    });
  }

  let action: React.ReactNode;
  if (!sources.hydrated) {
    action = <p className="text-2xs text-muted">Reading connections…</p>;
  } else if (!corpus) {
    action = (
      <Button size="sm" variant="needs-you" className="w-full" onClick={() => setDialog("connect")}>
        Connect MCP
      </Button>
    );
  } else if (corpus.status === "Unreachable") {
    action = (
      <Button size="sm" variant="outline" className="w-full" disabled={busy} onClick={() => reconnect(corpus.id)}>
        {busy ? <Spinning label="Reconnecting…" /> : "Reconnect"}
      </Button>
    );
  } else if (imported) {
    action = (
      <div className="flex flex-col gap-1.5">
        <p className="text-2xs text-secondary">
          {imported.documents} specs ingested from {corpus.name} · {imported.extracted} extracted
        </p>
        <Button size="sm" variant="outline" className="w-full" disabled={busy} onClick={() => checkForChanges(imported.id)}>
          {busy ? <Spinning label={`Checking ${corpus.name}…`} /> : "Check for changes"}
        </Button>
      </div>
    );
  } else {
    action = (
      <Button size="sm" variant="needs-you" className="w-full" onClick={() => setDialog("ingest")}>
        Ingest specs
      </Button>
    );
  }

  return (
    <div className="flex flex-none flex-col gap-2.5 border-t border-border px-3 py-3">
      <span className="text-xs font-medium text-secondary">Sources</span>

      {connections.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {connections.map((connection) => (
            <ConnectionItem key={connection.id} connection={connection} />
          ))}
        </ul>
      ) : null}

      {action}

      {note ? (
        <div
          role="status"
          className={cn(
            "flex flex-col gap-0.5 rounded-control border px-2.5 py-2 text-2xs",
            note.failed
              ? "border-status-failed-border bg-status-failed-fill text-status-failed-text"
              : "border-border-strong bg-sunken text-primary",
          )}
        >
          <span>{note.text}</span>
          <span className={note.failed ? "opacity-80" : "text-muted"}>{format.at(note.at)}</span>
          {stale && imported && !note.failed ? (
            <Button
              size="sm"
              variant="needs-you"
              className="mt-1.5 w-full"
              disabled={busy}
              onClick={() => updateFromSource(imported.id)}
            >
              Update from source
            </Button>
          ) : null}
        </div>
      ) : busy && imported ? (
        <div role="status" className="rounded-control border border-border-strong bg-sunken px-2.5 py-2 text-2xs text-secondary">
          <Spinning label={`Working with ${corpus?.name ?? "the server"}…`} />
        </div>
      ) : null}

      <Dialog open={dialog === "connect"} onOpenChange={(open) => setDialog(open ? "connect" : null)}>
        <DialogContent className="max-w-lg">
          {dialog === "connect" ? (
            <ConnectForm
              onDone={(text) => {
                setDialog(null);
                if (text) setNote({ text });
              }}
            />
          ) : null}
        </DialogContent>
      </Dialog>

      <Dialog open={dialog === "ingest"} onOpenChange={(open) => setDialog(open ? "ingest" : null)}>
        <DialogContent className="max-w-lg">
          {dialog === "ingest" && corpus ? (
            <IngestForm
              serverId={corpus.id}
              serverName={corpus.name}
              suggestedArea={projectName}
              onDone={(text) => {
                setDialog(null);
                if (text) setNote({ text });
              }}
            />
          ) : null}
        </DialogContent>
      </Dialog>
    </div>
  );
}

/** A button's in-flight state: it is doing something, and says what. */
function Spinning({ label }: { label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <RefreshCw aria-hidden className="size-3 animate-spin" />
      {label}
    </span>
  );
}

function ConnectionItem({ connection }: { connection: ConnectionRow }) {
  const tone =
    connection.status === "Conformant"
      ? "border-server-conformant-border bg-server-conformant-fill text-server-conformant-text"
      : connection.status === "Unreachable" || connection.status === "Failed"
        ? "border-server-unreachable-border bg-server-unreachable-fill text-server-unreachable-text"
        : "border-server-degraded-border bg-server-degraded-fill text-server-degraded-text";

  const title =
    connection.status === "Unreachable"
      ? `Not answering since ${connection.unreachable_since ? format.at(connection.unreachable_since) : "recently"}${connection.last_error ? ` — ${connection.last_error}` : ""}. The factory keeps retrying.`
      : connection.last_seen_at
        ? `Last answered ${format.at(connection.last_seen_at)} · ${connection.url}`
        : connection.url;

  return (
    <li className="flex items-center gap-1.5 text-2xs" title={title}>
      <span className={cn("shrink-0 rounded-pill border px-1.5 text-[10px]", tone)}>
        {connection.status === "Conformant" ? "connected" : connection.status.toLowerCase()}
      </span>
      <span className="truncate text-primary">{connection.name}</span>
      <span className="ml-auto shrink-0 text-muted">{connection.domain}</span>
    </li>
  );
}

/** Wire up an MCP server: one URL, and the factory does the handshake and every check. */
function ConnectForm({ onDone }: { onDone: (note?: string) => void }) {
  const sources = useSources();
  const [url, setUrl] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const result = await sources.connect(url);
    setBusy(false);
    if (!result.ok) return setError(result.error);
    const server = result.data;
    onDone(
      server.domain === "corpus"
        ? `Connected ${server.name}: ${server.status.toLowerCase()}. Its specs can be ingested now.`
        : `Connected ${server.name} — a ${server.domain} server, not a specs source.`,
    );
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>Connect an MCP server</DialogTitle>
        <DialogDescription>
          The factory runs the handshake, checks every capability the server declares, and keeps
          checking it every thirty seconds from then on. A specs server is what Ingest specs reads.
        </DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="mcp-url">Server URL</Label>
        <Input
          id="mcp-url"
          value={url}
          onChange={(event) => setUrl(event.target.value)}
          placeholder="http://mb-specs:8941/mcp"
          autoFocus
          disabled={busy}
        />
        <p className="text-2xs text-muted">
          The MCP endpoint as the factory reaches it — in this stack, by container name.
        </p>
      </div>

      {error ? <p className="text-2xs text-status-failed-text">{error}</p> : null}

      <DialogFooter>
        <Button type="button" variant="ghost" onClick={() => onDone()} disabled={busy}>
          Cancel
        </Button>
        <Button type="submit" variant="needs-you" disabled={busy || url.trim() === ""}>
          {busy ? "Running the handshake…" : "Connect"}
        </Button>
      </DialogFooter>
    </form>
  );
}

/** Pick what to bring in, then pull it: every document fetched and checked against its hash. */
function IngestForm({
  serverId,
  serverName,
  suggestedArea,
  onDone,
}: {
  serverId: string;
  serverName: string;
  suggestedArea: string;
  onDone: (note?: string) => void;
}) {
  const sources = useSources();
  const [areas, setAreas] = React.useState<CorpusArea[] | null>(null);
  const [chosen, setChosen] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    let cancelled = false;
    sources.preview(serverId).then((result) => {
      if (cancelled) return;
      if (!result.ok) return setError(result.error);
      setAreas(result.data);
      // The project's own area, when the server has one by that name.
      setChosen(result.data.find((a) => a.area === suggestedArea)?.area ?? result.data[0]?.area ?? null);
    });
    return () => {
      cancelled = true;
    };
    // Read once per opening; `sources` changes identity on every refresh.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serverId, suggestedArea]);

  const selected = areas?.find((a) => a.area === chosen);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!chosen) return;
    setBusy(true);
    setError(null);
    const result = await sources.ingest(serverId, chosen);
    setBusy(false);
    if (!result.ok) return setError(result.error);
    onDone(`Ingested ${result.data.documents} specs from ${serverName} into “Intake: ${result.data.name}”.`);
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>Ingest specs from {serverName}</DialogTitle>
        <DialogDescription>
          Every document in the area is read from the server and checked against the hash it listed.
          Nothing enters the spec graph yet: each one is extracted, its questions answered, then proposed.
        </DialogDescription>
      </DialogHeader>

      {areas === null && !error ? <p className="text-2xs text-muted">Reading what {serverName} has…</p> : null}

      {areas ? (
        <ul className="flex max-h-64 flex-col gap-1 overflow-auto">
          {areas.map((area) => (
            <li key={area.area}>
              <button
                type="button"
                onClick={() => setChosen(area.area)}
                disabled={busy}
                className={cn(
                  "flex w-full items-center justify-between rounded-control border px-3 py-2 text-left text-xs",
                  area.area === chosen
                    ? "border-accent-border bg-accent-fill text-accent-text"
                    : "border-border text-secondary hover:bg-sunken hover:text-primary",
                )}
              >
                <span className="font-medium">{area.area}</span>
                <span className="text-2xs">
                  {area.documents} specs{area.retired > 0 ? ` · ${area.retired} superseded, left out` : ""}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {error ? <p className="text-2xs text-status-failed-text">{error}</p> : null}

      <DialogFooter>
        <Button type="button" variant="ghost" onClick={() => onDone()} disabled={busy}>
          Cancel
        </Button>
        <Button type="submit" variant="needs-you" disabled={busy || !selected}>
          {busy
            ? `Reading ${selected?.documents ?? ""} specs…`
            : selected
              ? `Ingest ${selected.documents} specs`
              : "Ingest"}
        </Button>
      </DialogFooter>
    </form>
  );
}

// ----------------------------------------------------------------- thread

function EmptyThread() {
  const prototype = usePrototype();
  const projectName = useQuery((db) => db.project?.name ?? "this project");

  if (!prototype) {
    return (
      <div className="mx-auto flex max-w-xl flex-col gap-4 py-16 text-center">
        <h2 className="text-lg font-medium text-primary">Ask the architect about {projectName}.</h2>
        <p className="text-sm text-secondary">
          It reads this project&apos;s specifications before every reply. When the conversation
          settles on something concrete it proposes an amendment, and nothing changes the graph
          until someone approves it.
        </p>
      </div>
    );
  }

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
  const prototype = usePrototype();

  return (
    <div className="flex items-center gap-2.5">
      <Initials>AR</Initials>
      <span className="flex items-center gap-2 text-xs text-secondary">
        <span aria-hidden className="size-1.5 rounded-pill bg-accent breathe" />
        {prototype
          ? "Traversing the spec neighbourhood — 3 layers routed, 2 standards servers consulted"
          : "Reading the specifications and writing a reply. A reply that proposes an amendment can take a few minutes."}
      </span>
    </div>
  );
}

/** A send, approve or reject the factory refused, in its own words. */
function TurnError({ message, onDismiss }: { message: string; onDismiss: () => void }) {
  return (
    <div className="flex items-start gap-3 rounded-card border border-status-failed-border bg-status-failed-fill px-4 py-3">
      <AlertTriangle aria-hidden className="mt-0.5 size-4 shrink-0 text-status-failed-text" />
      <p className="min-w-0 flex-1 text-sm text-status-failed-text">{message}</p>
      <Button size="sm" variant="ghost" className="shrink-0" onClick={onDismiss}>
        Dismiss
      </Button>
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
        <p className="text-sm whitespace-pre-wrap text-primary">{text}</p>
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
                {turn.deployment ? `${turn.deployment} · ` : ""}
                {format.at(turn.at)}
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
 *
 * In the product the card's status is the amendment's, read back from the
 * factory after every decision. The prototype keeps its one local decision.
 */
function TurnPayloads({ payloads }: { payloads: Payload[] }) {
  const dispatch = useDispatch();
  const decision = useDecision();
  const prototype = usePrototype();

  return (
    <div className="flex flex-col gap-4">
      {payloads.map((payload, index) => {
        if (payload.type !== "approval_card") {
          return <PayloadRenderer key={index} payloads={[payload]} />;
        }

        const approval = prototype
          ? {
              ...payload.approval,
              status:
                decision.state === "approved"
                  ? ("approved" as const)
                  : decision.state === "rejected"
                    ? ("rejected" as const)
                    : ("awaiting" as const),
              reason: decision.state === "rejected" ? decision.reason : undefined,
            }
          : payload.approval;

        return (
          <div key={index} className="flex flex-col gap-2">
            <ApprovalCard
              approval={approval}
              onApprove={() => dispatch("df.specs.approve", { amendment_id: payload.approval.target_id })}
              onReject={(reason) =>
                dispatch("df.specs.reject", { amendment_id: payload.approval.target_id, reason })
              }
            />
            {prototype ? <PrototypeDecisionNote /> : <DecisionNote status={approval.status} />}
          </div>
        );
      })}
    </div>
  );
}

/** What approving does today: `df.specs.approve` applies the amendment to the graph. */
function DecisionNote({ status }: { status: "awaiting" | "approved" | "rejected" }) {
  if (status === "awaiting") {
    return <p className="text-2xs text-muted">Approving applies it to the spec graph.</p>;
  }
  if (status === "approved") {
    return <p className="text-2xs text-muted">Applied to the spec graph.</p>;
  }
  return null;
}

function PrototypeDecisionNote() {
  const decision = useDecision();

  if (decision.state === "undecided") {
    return <p className="text-2xs text-muted">Approving queues it into the backlog, not into a run.</p>;
  }
  if (decision.state === "approved") {
    return (
      <div className="flex items-center gap-2 text-2xs text-muted">
        <span>Amendment is in the backlog. It enters a run when someone batches it.</span>
        <Link href="/batches?state=settled" className="text-accent-text hover:underline">
          Open backlog
        </Link>
      </div>
    );
  }
  return null;
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
  const prototype = usePrototype();
  const snapshot = useQuery((db) => db.project?.snapshot_id ?? "");
  const { activeId, busy } = useConversation();
  const [draft, setDraft] = React.useState("");

  const canSend = draft.trim() !== "" && !busy;

  function send() {
    if (!canSend) return;
    dispatch("df.conversations.turn", { conversation_id: activeId ?? "", message: draft });
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
          {prototype ? (
            <>
              <span className="flex items-center gap-1">
                snapshot <SpecId id={snapshot} />
              </span>
              <span>Retrieval scoped to product, data, api · 2 standards servers indexed</span>
            </>
          ) : (
            <span>
              {busy
                ? "Waiting for the architect's reply."
                : activeId
                  ? "The architect replies with the project's specifications in front of it."
                  : "Your first message starts a new conversation."}
            </span>
          )}
          <span className="ml-auto">⌘↵ to send</span>
          <Button size="sm" variant="needs-you" onClick={send} disabled={!canSend}>
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

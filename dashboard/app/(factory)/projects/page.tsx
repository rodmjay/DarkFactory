"use client";

/**
 * Projects — the org's roster, and where a new one starts.
 *
 * This is the first screen reading the factory rather than fixtures.
 * `df.projects.list` is a real query; registering is a real
 * `df.projects.register`, which handshakes the workspace server, exercises
 * every capability it declares, derives a name and seeds the project's
 * default team before it returns.
 *
 * The columns are the four things a lead checks before deciding where to
 * look: what is running, what is waiting on them, what is ready to ship,
 * and what it cost. Three of those are not queryable yet, so they render
 * as "—" rather than as zero.
 */

import * as React from "react";
import Link from "next/link";
import { AlertTriangle, RefreshCw } from "lucide-react";

import {
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Input,
  Label,
  LayerBadge,
  SpecId,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
  format,
} from "@dark-factory/ui";

import { useDispatch, useLive, useQuery } from "@/lib/local/store";

export default function ProjectsPage() {
  const projects = useQuery((db) => db.projects);
  const org = useQuery((db) => db.org);
  const { live } = useLive();
  const [registerOpen, setRegisterOpen] = React.useState(false);

  return (
    <div className="mx-auto max-w-6xl px-6 py-6">
      <div className="mb-5 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold">Projects</h1>
          <p className="mt-0.5 text-xs text-secondary">
            {/* The factory carries no org, so outside the prototype there is
                none to prefix — rather than a fixture tenant's name. */}
            {org ? `${org} · ` : ""}
            {projects.length} project{projects.length === 1 ? "" : "s"} ·{" "}
            {live.status === "live"
              ? "read from the factory"
              : live.status === "hydrating"
                ? "reading from the factory…"
                : "the factory is not reachable"}
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" asChild>
            <Link href="/servers">Connectors</Link>
          </Button>
          <Button size="sm" variant="needs-you" onClick={() => setRegisterOpen(true)}>
            Register a project
          </Button>
        </div>
      </div>

      {live.status === "unreachable" ? <Unreachable error={live.error} /> : null}

      {projects.length === 0 && live.status !== "unreachable" ? (
        <NothingConnected onRegister={() => setRegisterOpen(true)} />
      ) : projects.length > 0 ? (
        <Roster />
      ) : null}

      <RegisterDialog open={registerOpen} onOpenChange={setRegisterOpen} />
    </div>
  );
}

/** The factory is down, and the roster on screen is not evidence of anything. */
function Unreachable({ error }: { error: string }) {
  const { refresh } = useLive();

  return (
    <div className="mb-4 flex items-start gap-3 rounded-card border border-status-failed-border bg-status-failed-fill px-4 py-3">
      <AlertTriangle aria-hidden className="mt-0.5 size-4 shrink-0 text-status-failed-text" />
      <div className="flex min-w-0 flex-col gap-1">
        <span className="text-sm text-status-failed-text">The factory is not reachable.</span>
        <p className="text-2xs text-secondary">{error}</p>
        <p className="text-2xs text-muted">
          Nothing is listed below, because a roster from memory presented as the current one is
          worse than no roster.
        </p>
      </div>
      <Button size="sm" variant="outline" className="ml-auto shrink-0" onClick={refresh}>
        <RefreshCw aria-hidden className="size-3" />
        Retry
      </Button>
    </div>
  );
}

function Roster() {
  const projects = useQuery((db) => db.projects);

  const totals = projects.reduce(
    (sum, project) => ({
      runs: add(sum.runs, project.active_runs, project.parked_runs),
      awaiting: add(sum.awaiting, project.awaiting_amendments),
      deployable: add(sum.deployable, project.deployable_batches),
      spend: add(sum.spend, project.spend_usd),
    }),
    { runs: null, awaiting: null, deployable: null, spend: null } as Record<string, number | null>,
  );

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Project</TableHead>
          <TableHead>Layers detected</TableHead>
          <TableHead numeric>Runs now</TableHead>
          <TableHead numeric>Awaiting you</TableHead>
          <TableHead numeric>Ready to deploy</TableHead>
          <TableHead numeric>Spend, Sep</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {projects.map((project) => (
          <TableRow key={project.id}>
            <TableCell>
              <Link href="/conversation" className="flex flex-col gap-0.5">
                <span className="text-sm font-medium text-primary hover:underline">
                  {project.name}
                </span>
                <span className="flex items-center gap-1 text-2xs text-muted">
                  {project.unconnected ? (
                    "no servers connected — the factory cannot reach any code yet"
                  ) : project.snapshot_id ? (
                    <>
                      {project.node_count} nodes · snapshot <SpecId id={project.snapshot_id} />
                    </>
                  ) : (
                    <>
                      {project.node_count} nodes
                      {project.stack_hints?.length ? ` · ${project.stack_hints.join(", ")}` : ""}
                    </>
                  )}
                </span>
              </Link>
            </TableCell>

            <TableCell>
              {project.layers.length === 0 ? (
                <span className="text-2xs text-muted">none detected</span>
              ) : (
                <div className="flex flex-wrap gap-1">
                  {project.layers.map((layer) => (
                    <LayerBadge
                      key={layer}
                      layer={layer}
                      title={
                        project.declared_layers?.includes(layer)
                          ? `Layer declared by a standards server: ${layer}`
                          : undefined
                      }
                    />
                  ))}
                </div>
              )}
            </TableCell>

            <TableCell numeric>
              {project.parked_runs ? (
                <span className="inline-flex items-center gap-1.5">
                  <StatusChip status="parked" compact />
                  {project.parked_runs} parked
                </span>
              ) : project.active_runs ? (
                <span className="inline-flex items-center gap-1.5">
                  <StatusChip status="running" compact />
                  {project.active_runs}
                </span>
              ) : (
                <Unknown value={project.active_runs} />
              )}
            </TableCell>

            <TableCell numeric>
              {project.awaiting_amendments ? (
                <Link href="/amendments" className="text-accent-text hover:underline">
                  {project.awaiting_amendments} amendments
                </Link>
              ) : (
                <Unknown value={project.awaiting_amendments} />
              )}
            </TableCell>

            <TableCell numeric>
              {project.deployable_batches ? (
                <Link href="/batches" className="text-accent-text hover:underline">
                  {project.deployable_batches} batch
                </Link>
              ) : (
                <Unknown value={project.deployable_batches} />
              )}
            </TableCell>

            <TableCell numeric>
              {project.spend_usd === null ? <Unknown value={null} /> : format.usd(project.spend_usd)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
      <TableFooter>
        <TableRow>
          <TableCell>
            {projects.length} project{projects.length === 1 ? "" : "s"}
          </TableCell>
          <TableCell />
          <TableCell numeric>{totals.runs ?? "—"}</TableCell>
          <TableCell numeric>{totals.awaiting ?? "—"}</TableCell>
          <TableCell numeric>{totals.deployable ?? "—"}</TableCell>
          <TableCell numeric>{totals.spend === null ? "—" : format.usd(totals.spend)}</TableCell>
        </TableRow>
      </TableFooter>
    </Table>
  );
}

/**
 * Zero and "not known" look identical in a table cell, so they are drawn
 * differently: a real zero is a dash in the text colour, an unknown is a
 * dash the reader can hover to find out why.
 */
function Unknown({ value }: { value: number | null }) {
  if (value === null) {
    return (
      <span
        className="cursor-help text-muted"
        title="Not queryable from this dashboard yet — the factory does not carry this figure on df.projects.list."
      >
        —
      </span>
    );
  }
  return <span className="text-muted">0</span>;
}

/** A sum of numbers where any unknown makes the total unknown. */
function add(total: number | null, ...values: (number | null)[]): number | null {
  if (total === null && values.every((v) => v === null)) return null;
  return values.reduce<number>((sum, v) => sum + (v ?? 0), total ?? 0);
}

// ------------------------------------------------------------- registration

/**
 * Registering a project is one question: where is the workspace server.
 *
 * Everything else — the name, the stack hints, the conformance result, the
 * default team — is the factory's answer, derived during the handshake. A
 * form that asked for those would be asking the user to guess at facts the
 * factory is about to establish.
 */
function RegisterDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        {/* The form is mounted only while the dialog is open, so a reopened
            dialog is a fresh one. React clears the fields; there is no reset
            effect to forget to update when a field is added. */}
        {open ? <RegisterForm onDone={() => onOpenChange(false)} /> : null}
      </DialogContent>
    </Dialog>
  );
}

function RegisterForm({ onDone }: { onDone: () => void }) {
  const { register } = useLive();
  const [url, setUrl] = React.useState("");
  const [name, setName] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    const result = await register(url, name);

    setBusy(false);
    if (result.ok) onDone();
    else setError(result.error ?? "Registration failed.");
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>Register a project</DialogTitle>
        <DialogDescription>
          The factory runs <code className="font-mono">df.describe</code> against this server
          and exercises every capability it declares before it accepts a single call. It
          derives the project name and seeds the default team from what comes back.
        </DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="workspace-url">Workspace server URL</Label>
        <Input
          id="workspace-url"
          value={url}
          onChange={(event) => setUrl(event.target.value)}
          placeholder="http://workspace-demo:8931/mcp"
          autoFocus
          disabled={busy}
        />
        <p className="text-2xs text-muted">
          The MCP endpoint of a server that speaks the <code className="font-mono">df</code>{" "}
          workspace convention. In this stack, the reference server is at{" "}
          <button
            type="button"
            className="font-mono text-accent-text hover:underline"
            onClick={() => setUrl("http://workspace-demo:8931/mcp")}
          >
            http://workspace-demo:8931/mcp
          </button>
          .
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="project-name">Name (optional)</Label>
        <Input
          id="project-name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="Derived from the workspace if left blank"
          disabled={busy}
        />
      </div>

      {error ? (
        <div className="flex items-start gap-2 rounded-control border border-status-failed-border bg-status-failed-fill px-3 py-2">
          <AlertTriangle aria-hidden className="mt-0.5 size-3.5 shrink-0 text-status-failed-text" />
          <p className="text-2xs text-status-failed-text">{error}</p>
        </div>
      ) : null}

      <DialogFooter>
        <Button type="button" variant="ghost" onClick={onDone} disabled={busy}>
          Cancel
        </Button>
        <Button type="submit" variant="needs-you" disabled={busy || url.trim() === ""}>
          {busy ? "Running the handshake…" : "Register"}
        </Button>
      </DialogFooter>
    </form>
  );
}

/**
 * The empty org.
 *
 * It offers the built-in connectors rather than an illustration, because
 * the only useful thing on this screen is the next action — and ADR-0019
 * makes built-in connectors an authorization rather than a registration,
 * so the shortest path is three buttons.
 */
function NothingConnected({ onRegister }: { onRegister: () => void }) {
  const connectors = useQuery((db) => db.connectors);
  const dispatch = useDispatch();

  return (
    <div className="mx-auto flex max-w-xl flex-col gap-5 rounded-card border border-border bg-card px-6 py-8">
      <div className="flex flex-col gap-2">
        <h2 className="text-base font-medium text-primary">Nothing is connected yet.</h2>
        <p className="text-sm text-secondary">
          The factory needs one workspace server per project — that is how it reads and writes
          code. Authorize a built-in connector, or register a server you run yourself.
        </p>
      </div>

      <ul className="flex flex-col gap-2">
        {connectors.map((connector) => (
          <li
            key={connector.id}
            className="flex items-center gap-3 rounded-control border border-border px-3 py-2.5"
          >
            <span className="inline-flex size-7 items-center justify-center rounded-control bg-sunken font-mono text-2xs text-secondary">
              {connector.id}
            </span>
            <div className="flex min-w-0 flex-col">
              <span className="text-sm text-primary">{connector.name}</span>
              <span className="font-mono text-2xs text-muted">
                {connector.domain} · {connector.note}
              </span>
            </div>
            <Button
              size="sm"
              variant="needs-you"
              className="ml-auto"
              onClick={() => dispatch("df.servers.authorize", { id: connector.id })}
            >
              Authorize
            </Button>
          </li>
        ))}
      </ul>

      <div className="flex items-center gap-3 border-t border-border pt-4">
        <p className="text-2xs text-muted">
          Running your own workspace server? Register it and the factory will run{" "}
          <code className="font-mono text-secondary">df.describe</code> plus a conformance check
          before it accepts a single call.
        </p>
        <Button variant="outline" size="sm" className="ml-auto shrink-0" onClick={onRegister}>
          Register a server
        </Button>
      </div>
    </div>
  );
}

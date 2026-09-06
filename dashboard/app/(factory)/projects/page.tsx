"use client";

/**
 * Projects — the org's roster.
 *
 * The columns are the four things a lead checks before deciding where to
 * look: what is running, what is waiting on them, what is ready to ship,
 * and what it cost. Anything else about a project is one click away and
 * does not belong on this row.
 */

import Link from "next/link";

import {
  Button,
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

import { useDispatch, useQuery } from "@/lib/local/store";

export default function ProjectsPage() {
  const projects = useQuery((db) => db.projects);
  const org = useQuery((db) => db.org);

  const totals = projects.reduce(
    (sum, project) => ({
      runs: sum.runs + project.active_runs + project.parked_runs,
      awaiting: sum.awaiting + project.awaiting_amendments,
      deployable: sum.deployable + project.deployable_batches,
      spend: sum.spend + project.spend_usd,
    }),
    { runs: 0, awaiting: 0, deployable: 0, spend: 0 },
  );

  return (
    <div className="mx-auto max-w-6xl px-6 py-6">
      <div className="mb-5 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold">Projects</h1>
          <p className="mt-0.5 text-xs text-secondary">
            {org} · {projects.length} project{projects.length === 1 ? "" : "s"} · 2 org-wide
            standards servers indexed
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" asChild>
            <Link href="/servers">Connectors</Link>
          </Button>
          <Button size="sm" variant="needs-you">
            Register a project
          </Button>
        </div>
      </div>

      {projects.length === 0 ? <NothingConnected /> : <Roster totals={totals} />}
    </div>
  );
}

function Roster({
  totals,
}: {
  totals: { runs: number; awaiting: number; deployable: number; spend: number };
}) {
  const projects = useQuery((db) => db.projects);

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
                  ) : (
                    <>
                      {project.node_count} nodes · snapshot <SpecId id={project.snapshot_id} />
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
              {project.parked_runs > 0 ? (
                <span className="inline-flex items-center gap-1.5">
                  <StatusChip status="parked" compact />
                  {project.parked_runs} parked
                </span>
              ) : project.active_runs > 0 ? (
                <span className="inline-flex items-center gap-1.5">
                  <StatusChip status="running" compact />
                  {project.active_runs}
                </span>
              ) : (
                <Dash />
              )}
            </TableCell>

            <TableCell numeric>
              {project.awaiting_amendments > 0 ? (
                <Link href="/amendments" className="text-accent-text hover:underline">
                  {project.awaiting_amendments} amendments
                </Link>
              ) : (
                <Dash />
              )}
            </TableCell>

            <TableCell numeric>
              {project.deployable_batches > 0 ? (
                <Link href="/batches" className="text-accent-text hover:underline">
                  {project.deployable_batches} batch
                </Link>
              ) : (
                <Dash />
              )}
            </TableCell>

            <TableCell numeric>{format.usd(project.spend_usd)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
      <TableFooter>
        <TableRow>
          <TableCell>
            {projects.length} project{projects.length === 1 ? "" : "s"}
          </TableCell>
          <TableCell />
          <TableCell numeric>{totals.runs}</TableCell>
          <TableCell numeric>{totals.awaiting}</TableCell>
          <TableCell numeric>{totals.deployable}</TableCell>
          <TableCell numeric>{format.usd(totals.spend)}</TableCell>
        </TableRow>
      </TableFooter>
    </Table>
  );
}

function Dash() {
  return <span className="text-muted">—</span>;
}

/**
 * The empty org.
 *
 * It offers the built-in connectors rather than an illustration, because
 * the only useful thing on this screen is the next action — and ADR-0019
 * makes built-in connectors an authorization rather than a registration,
 * so the shortest path is three buttons.
 */
function NothingConnected() {
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
        <Button variant="outline" size="sm" asChild className="ml-auto shrink-0">
          <Link href="/servers">Register a server</Link>
        </Button>
      </div>
    </div>
  );
}

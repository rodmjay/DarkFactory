"use client";

/**
 * Servers and connectors.
 *
 * Grouped by the `df.*` domain they serve rather than by tier, because the
 * question is always "what can the factory do here", and a group with no
 * server is the most informative row on the screen — it says which
 * capability the factory is currently doing without.
 */

import { Button, ServerCard } from "@dark-factory/ui";

import { useDispatch, useQuery } from "@/lib/local/store";

export default function ServersPage() {
  const groups = useQuery((db) => db.server_groups);

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6 px-6 py-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold">Servers and connectors</h1>
          <p className="mt-0.5 max-w-2xl text-xs text-secondary">
            Everything the factory is connected to. Registration runs{" "}
            <code className="font-mono text-primary">df.describe</code> and exercises every
            declared capability once.
          </p>
        </div>
        <Button size="sm" variant="needs-you">
          Register a server
        </Button>
      </div>

      {groups.length === 0 ? <ConnectedToNothing /> : groups.map((group) => (
        <section key={group.domain} className="flex flex-col gap-2.5">
          <div className="flex items-baseline gap-2">
            <h2 className="text-sm font-semibold">{group.title}</h2>
            <span className="font-mono text-2xs text-muted">{group.domain}</span>
          </div>

          {group.servers.length > 0 ? (
            <div className="grid grid-cols-2 gap-3">
              {group.servers.map((server) => (
                <ServerCardWired key={server.id} server={server} />
              ))}
            </div>
          ) : group.empty ? (
            <div className="flex items-center gap-4 rounded-card border border-dashed border-border-strong bg-card px-4 py-3.5">
              <div className="flex min-w-0 flex-col gap-1">
                <span className="text-sm text-primary">{group.empty.title}</span>
                <p className="text-2xs text-secondary">{group.empty.body}</p>
              </div>
              <Button size="sm" variant="outline" className="ml-auto shrink-0">
                {group.empty.action}
              </Button>
            </div>
          ) : null}
        </section>
      ))}
    </div>
  );
}

function ServerCardWired({ server }: { server: Parameters<typeof ServerCard>[0]["server"] }) {
  const dispatch = useDispatch();

  return (
    <ServerCard
      server={server}
      onAuthorize={() => dispatch("df.servers.authorize", { id: server.id })}
    />
  );
}

/**
 * A project connected to nothing.
 *
 * It says what still works, because that is the surprising half: the spec
 * graph, conversations, amendments and approvals all run on the factory's
 * own database. Only runs need somewhere to read and write code.
 */
function ConnectedToNothing() {
  const dispatch = useDispatch();

  return (
    <div className="flex max-w-2xl flex-col gap-4 rounded-card border border-border bg-card px-6 py-8">
      <h2 className="text-base font-medium text-primary">
        This project is connected to nothing.
      </h2>
      <p className="text-sm text-secondary">
        The spec graph works without a single server — conversations, amendments and approvals
        all run on the factory&apos;s own database. Runs do not: a run needs somewhere to read
        and write code.
      </p>
      <div className="flex gap-2">
        <Button
          size="sm"
          variant="needs-you"
          onClick={() => dispatch("df.servers.authorize", { id: "gh" })}
        >
          Authorize GitHub
        </Button>
        <Button size="sm" variant="outline">
          Register your own server
        </Button>
      </div>
    </div>
  );
}

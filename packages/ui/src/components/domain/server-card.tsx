import * as React from "react";
import { CheckIcon, PlugIcon, XIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { at } from "../../lib/format";
import { serverChip } from "../../lib/vocabulary";
import type { Server } from "../../types/run";
import { Badge } from "../ui/badge";
import { Button } from "../ui/button";
import { Card, CardContent, CardFooter } from "../ui/card";

export interface ServerCardProps extends React.ComponentProps<"div"> {
  server: Server;
  onAuthorize?: (server: Server) => void;
}

const TIER_LABEL = {
  built_in: "built-in",
  premium: "premium",
  community: "community",
} as const;

/**
 * A registered server, as `df.describe` reported it (ADR-0018).
 *
 * Capabilities are listed with their conformance result rather than as a
 * bare list, because registration exercises each declared capability once
 * against a scratch project — a server that *claims* `df.vcs.open_pr` and a
 * server that has demonstrated it are different things, and the registry
 * knows which.
 *
 * `degraded` names the failing capability. "Degraded" alone tells an
 * operator to go and look; naming the capability is the difference between
 * a status and a diagnosis.
 *
 * `unreachable` is drawn in neutral, not red, and `built_in_connector`
 * offers authorisation rather than a URL — built-ins are OAuth'd in the UI
 * and the factory holds the credentials (ADR-0019), so "not authorised yet"
 * is a setup step and not a fault.
 */
export function ServerCard({ server, onAuthorize, className, ...props }: ServerCardProps) {
  const failing = server.capabilities.filter((c) => c.status === "failed");
  const needsAuthorize = server.built_in_connector && !server.authorized;

  return (
    <Card
      data-slot="server-card"
      data-health={server.health}
      className={cn(needsAuthorize && "border-accent-border", className)}
      {...props}
    >
      <CardContent className="flex flex-col gap-3 pt-3.5">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-sm font-semibold text-primary">{server.name}</span>
              <Badge variant="outline">{TIER_LABEL[server.tier]}</Badge>
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-2xs text-muted">
              <span>{server.domain}</span>
              <span className="font-mono">convention {server.convention_version}</span>
              {server.last_conformance_at && (
                <span>checked {at(server.last_conformance_at)}</span>
              )}
            </div>
          </div>

          {/* A built-in connector that was never authorized has no health:
              nothing has contacted it. Showing `conformant` there asserts a
              conformance check that never ran, which is the one claim this
              card must not make. */}
          <span
            className={cn(
              "shrink-0 rounded-pill border px-2 py-0.5 text-2xs font-medium whitespace-nowrap",
              needsAuthorize
                ? "border-border bg-sunken text-secondary"
                : serverChip[server.health],
            )}
          >
            {needsAuthorize ? "not authorized" : server.health}
          </span>
        </div>

        {needsAuthorize && (
          <p className="rounded-control border border-dashed border-border px-2.5 py-2 text-xs text-muted">
            Nothing has failed here. The connector ships with the product and has not been
            given access yet.
          </p>
        )}

        {!needsAuthorize && server.health === "degraded" && failing.length > 0 && (
          <p className="rounded-control border border-server-degraded-border bg-server-degraded-fill px-2.5 py-2 text-xs text-server-degraded-text">
            {failing.map((c) => c.name).join(", ")} failed its conformance check
            {failing[0]?.detail ? ` — ${failing[0].detail}` : "."}
          </p>
        )}

        {!needsAuthorize && server.health === "unreachable" && (
          <p className="rounded-control border border-dashed border-border px-2.5 py-2 text-xs text-muted">
            Not reachable from the factory. Usually a network fact rather than a fault — the
            last known describe is shown below.
          </p>
        )}

        {server.capabilities.length > 0 && (
          <div className="flex flex-col gap-1">
            <span className="text-2xs tracking-wide text-muted uppercase">Capabilities</span>
            <ul className="flex flex-wrap gap-1">
              {server.capabilities.map((capability) => (
                <li key={capability.name}>
                  <span
                    title={capability.detail}
                    className={cn(
                      "inline-flex items-center gap-1 rounded-pill border px-1.5 py-0.5",
                      "font-mono text-2xs",
                      capability.status === "failed"
                        ? "border-server-degraded-border bg-server-degraded-fill text-server-degraded-text"
                        : "border-border bg-sunken text-secondary",
                    )}
                  >
                    {capability.status === "passed" && (
                      <CheckIcon className="size-3 text-server-conformant-text" aria-hidden />
                    )}
                    {capability.status === "failed" && <XIcon className="size-3" aria-hidden />}
                    {capability.name}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {server.effective_config && Object.keys(server.effective_config).length > 0 && (
          <div className="flex flex-col gap-1">
            <span className="text-2xs tracking-wide text-muted uppercase">
              Effective config
            </span>
            {/* Never secrets: this is stored in the registry and rendered
                here, which is exactly why describe.schema.json forbids them. */}
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 font-mono text-2xs">
              {Object.entries(server.effective_config).map(([key, value]) => (
                <React.Fragment key={key}>
                  <dt className="text-muted">{key}</dt>
                  <dd className="truncate text-secondary">{value}</dd>
                </React.Fragment>
              ))}
            </dl>
          </div>
        )}
      </CardContent>

      {needsAuthorize && (
        <CardFooter>
          <Button variant="needs-you" size="sm" onClick={() => onAuthorize?.(server)}>
            <PlugIcon /> Authorize
          </Button>
          <span className="text-2xs text-muted">
            The factory holds the credentials for built-in connectors.
          </span>
        </CardFooter>
      )}
    </Card>
  );
}

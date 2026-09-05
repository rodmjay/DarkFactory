# ADR-0025: Identity, tenancy, and deployment modes

## Status
Accepted — extends [ADR-0014](0014-local-mode-is-the-dev-loop.md)

## Context
[ADR-0014](0014-local-mode-is-the-dev-loop.md) established local Compose as
the dev loop, explicitly not the hosted product. The hosted product now
needs an actual identity and tenancy model, and some customers' project
servers will never be reachable from a multi-tenant cloud, which local
Compose alone doesn't address either.

## Decision
A token (PAT or OAuth) carries **user and org**, never a workspace — the
workspace/project is selected per connection or explicitly via
`df.projects.select`, matching decision 13's project-scoping precedent
([ADR-0011](0011-project-isolation.md)). Orgs own subscriptions and
projects; roles govern who may approve gates and who may install servers.

**Hosted is the default product** ([ADR-0026](0026-hosting.md)). A
**self-hosted image** — the same image Compose already builds — exists for
customers whose project servers cannot be reached from our cloud at all
(fully air-gapped or firewalled environments). It validates entitlement
against our licensing endpoint, caches that entitlement for a generous
window (approximately 7 days), runs normally offline within that window,
and degrades once the window lapses without renewal. Self-hosted customers
keep their spec graph on their own Postgres; we explicitly accept the loss
of telemetry that comes with that rather than requiring phone-home for
every operation.

## Consequences
- `org_id` ([ADR-0010](0010-single-postgres-schema-with-org-id.md)) finally
  gets a real populating mechanism (the token) instead of a constant —
  though the auth layer that issues such tokens is still out of scope for
  this slice.
- The self-hosted image is genuinely the *same* image as Compose, not a
  fork — the only difference is the licensing check at startup and the
  degrade-after-window behavior, both additive.
- "We accept the loss of telemetry" for self-hosted customers is a
  deliberate trade explicitly recorded here so it isn't quietly re-litigated
  later as a bug — a self-hosted deployment that phones home constantly
  defeats the point of choosing it.

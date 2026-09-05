# ADR-0010: One Postgres schema, `org_id` present from day one

## Status
Accepted

## Context
Dark Factory will eventually run as a hosted, multi-tenant service, but v1
is local-only and single-org. Retrofitting multi-tenancy into a schema that
was never designed for it is expensive and error-prone (backfills, missed
filters, subtle cross-tenant leaks).

## Decision
There is one Postgres database and one schema, used identically whether the
factory is running locally or hosted. Every tenant-scoped table carries an
`org_id` column from day one, even though v1 only ever writes a single,
fixed `org_id` (a well-known default org created by the initial migration).

## Consequences
- No schema migration is required to "turn on" multi-org later — only a
  change in how `org_id` is populated (from auth context instead of a
  constant) and, eventually, row-level security policies keyed on it.
- Every query written against these tables should filter by `org_id` even
  in v1, so that habit and the query patterns are already correct once
  multi-org lands.
- `project_id` (ADR-0011) is the isolation boundary that actually matters
  operationally in v1; `org_id` is a forward-looking column, not yet an
  enforced boundary.

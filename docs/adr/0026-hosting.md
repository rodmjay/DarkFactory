# ADR-0026: Hosting

## Status
Accepted

## Context
[ADR-0025](0025-identity-tenancy-deployment-modes.md) establishes hosted as
the default product. That needs an actual target platform, chosen with
enough conviction to stop relitigating it per service.

## Decision
`factory`, `worker`, and Postgres run on Azure Container Apps —
scale-to-zero with wake-on-request suits the factory's bursty load (a
customer's activity is conversational and occasional, not constant
background traffic). The Next.js web UI (step 4) runs on Vercel. Local
development stays Docker Compose, using the same images the hosted
deployment runs — there is no separate "hosted Dockerfile."

## Consequences
- Nothing about `DarkFactory.Mcp`/`DarkFactory.Engine`'s code should assume
  it's always running (long-lived in-memory caches spanning requests are
  suspect) given scale-to-zero; state that must survive belongs in Postgres,
  consistent with [ADR-0008](0008-durable-orchestration.md) already.
- The dashboard being a separately-hosted Vercel app means its calls to the
  factory API and SignalR hub cross an origin boundary in production that
  local Compose's same-network setup doesn't currently exercise — a gap to
  close when step 4's dashboard actually needs to run against a hosted
  factory.
- Choosing Azure Container Apps over, say, Kubernetes is a bet that this
  product's load shape (bursty, per-org, not high-QPS) doesn't need
  Kubernetes-level control; revisit only if that load shape assumption
  turns out wrong.

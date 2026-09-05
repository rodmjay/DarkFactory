# ADR-0014: Local mode is the dev loop, not the product

## Status
Accepted

## Context
We need a concrete, runnable target for this first slice, but we don't want
"make local Docker Compose nice" to be mistaken for the actual product,
which is a hosted, multi-project factory reachable over the network by many
hosts and many repos.

## Decision
"Local mode" means: Postgres, the factory, the worker, and the dashboard run
via `docker compose up` on a developer's own machine. Other repositories on
that same machine run their own reference workspace server (not a Compose
service — see the README) and register with the locally running factory
over `host.docker.internal`. This is the inner dev loop for building and
testing Dark Factory itself, and the fastest way for an early adopter to try
it against their own repo. It is explicitly **not** the hosted product:
there is no auth, no multi-machine reachability, no relay/tunnel, and no
notion of "your organization's factory" — just one factory, one machine,
one developer.

## Consequences
- Local mode can stay simple (no auth, no TLS, no tunnel) without that
  simplicity leaking into decisions that matter for the hosted product —
  those are explicitly deferred (see the "out of scope" list in the setup
  prompt / this README), not designed away.
- The reference workspace server must run comfortably outside Docker,
  directly against a developer's working tree, since that's the whole
  point of local mode: point the factory at *this* repo, not a container
  copy of it.
- When hosted mode is eventually built, it should be able to reuse the same
  factory image and schema — the difference is deployment topology (auth,
  ingress, tunnel) layered on top, not a different core.

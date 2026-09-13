# ADR-0038: Connections are watched and heal; specifications are pulled from corpus servers

## Status
Accepted. Extends [ADR-0018](0018-df-namespace-and-describe-handshake.md)
(server status), [ADR-0007](0007-failure-classes.md) (what a run does when
a dependency is down) and [ADR-0037](0037-corpus-intake.md) (where an
intake's documents come from). Adds the `corpus` domain to
[describe.md](../conventions/describe.md) and defines
[corpus.md](../conventions/corpus.md).

## Context
Rod's instruction, 2026-09-13, recorded as the current north star:

> we want to make sure the process of connecting to the workspace MCP
> server is seamless, if the dark factory detects the connection is broken
> it needs to heal it, it needs to be able to extract specs from MCP into
> its own system

Measured against that, on the day it was written:

- **Nothing watched a connection.** A server's status was whatever
  registration observed. Both Moonbeam servers read `Conformant` four days
  after anyone had last asked them anything; had either died, the factory
  would have said so only when a run failed against it.
- **A broken connection cost work.** A stage whose workspace call failed
  was retried as a `retryable` failure and, once attempts ran out, the run
  failed — so a server restart could fail a run that had done nothing wrong.
- **Nothing could heal.** Once a server came back, nothing re-established
  what registration had: its handshake, its manifest diff, its conformance.
- **Connecting was hand work.** Moonbeam's servers were started with
  `docker run` and restart policy `no`.
- **Specs arrived by copy and paste.** ADR-0037's intake took documents
  inline; the specifications were already served over MCP by Moonbeam, and
  the factory had no way to read them from there.

## Decision

**Every registered server is checked on a schedule.** A check is one
`df.describe` with a five-second deadline, every thirty seconds, run by one
monitor in the factory (not per worker). Registration counts as the first.

**One miss is not an outage; two are.** The second consecutive miss sets a
new status, `Unreachable`, dated from the first miss. A dead server is
asked less often — 15s, 30s, 60s … capped at five minutes — so a restart is
ridden out and nothing is hammered.

**Healing is re-verification, not reconnection.** When an unreachable
server answers, the factory re-runs what registration established — schema
validation, manifest/live diff, conformance — and sets whatever status that
earns. The same happens, without any outage, when a server starts saying
something new about itself. Nobody re-registers anything. A server
registered without a manifest has no claim to drift from, so its recorded
describe moves with it rather than marking every upgrade degraded.

**The factory heals its connection, not the customer's machine.** Keeping
the server *process* alive is the server's host's job: Moonbeam's servers
become managed services with `restart: unless-stopped` and a healthcheck.
The factory never gets Docker access. The alternative — the factory
restarting containers itself — heals more locally, but gives it root on the
host and cannot carry to the hosted product, where servers run on customer
machines.

**A run waits for an unreachable workspace instead of failing against it.**
Before a stage runs, the engine checks the project's workspace server. If
it is unreachable the stage is not attempted: the run stays claimed until
the server's next check, using the lease as the clock exactly as retry
backoff does. No checkpoint, no attempt spent, one `run.waiting_for_server`
event per wait.

**Registration rides out a momentary miss.** A `retryable` describe failure
is asked again twice (1s, 2s) before registration is refused; a permanent
one is not.

**Conformance stops littering.** It re-runs on every heal now, and the
workspace convention has no delete, so the probe writes one fixed file,
`.df-conformance/probe/probe.txt`, instead of a directory per pass.

**Specifications are pulled from corpus servers.** A new domain, `corpus`
([corpus.md](../conventions/corpus.md)): `df.corpus.list` (complete, with a
SHA-256 per document) and `df.corpus.get` (the text as authored).
`df.intake.pull` imports a server's documents into an intake, leaving
superseded and rejected ones out by default, and refuses any document whose
served text does not hash to what was listed. `df.intake.drift` reports what
has changed at the source since — a report, because after import the
factory is authoritative (ADR-0037). `corpus` is its own domain rather than
`knowledge` because retrieval and import are different relationships:
standards stay where they are and are read forever; a corpus is read once
and then owned.

## Consequences
- `servers` gains health columns and `ServerStatus.Unreachable`;
  `df.servers.list` returns them, and `df.servers.check` checks on demand.
- The dashboard's Servers screen now has live health to show; wiring it is
  the next piece of the "seamless" half.
- Only the workspace server gates a run. A standards or corpus server being
  down degrades retrieval or blocks a pull, with a message saying since when
  and that the factory is retrying; it does not hold runs.
- `ISpokeClient` is still unimplemented: pulls go through the same
  `IServerProbe` the registry uses, with one retry per call. A pooled
  runtime client is worth having when calls are hot; imports are not.
- A server that answers but whose handshake no longer validates is
  `Failed`, checked on the normal cadence, and treated as returning from an
  outage when it validates again.

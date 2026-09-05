# Dark Factory

A hosted, multi-project software factory that is itself an MCP server. Hosts
(Claude Code, Cursor, CI) connect to it as MCP clients; it in turn acts as an
MCP client to workspace, theme, and plugin servers that follow the published
conventions in [`docs/conventions/`](docs/conventions/). It never holds
customer code — only orchestration state and metadata. See
[`docs/adr/`](docs/adr/) for the full set of foundational decisions.

**Status:** repo skeleton (ADRs, conventions, schemas, Compose) plus a real
data model, migrations, and engine: a Postgres-backed state machine with
leased claiming, a transactional outbox, an artifact store, and a retry
policy, all driving the six-stage pipeline with its two v1 gates — proven by
an integration test that kills a real worker process mid-stage and resumes
it in a second, independent process from durable state alone (see
`tests/DarkFactory.Engine.Tests/CrashResumeTests.cs`). The front MCP surface
(`projects.*`, `work.*`) and the reference workspace server are next — see
each area's code comments for what's real versus stubbed.

## 1. `docker compose up`

```sh
cp .env.example .env   # set ANTHROPIC_API_KEY if you want stage agents to run
docker compose up --build
```

This brings up:

| Service     | What it is                                              | Where            |
|-------------|----------------------------------------------------------|------------------|
| `postgres`  | Postgres 16, the factory's own state (never customer code) | `localhost:5432` |
| `migrate`   | One-shot: applies pending `.sql` migrations (`DarkFactory.Migrate`), then exits | runs once, no ports |
| `factory`   | The MCP server + API + SignalR hub + webhook ingress (`DarkFactory.Mcp`) | `localhost:5100`, MCP endpoint at `http://localhost:5100/mcp` |
| `worker`    | The background engine (`DarkFactory.Engine`) — same image as `factory`, different entrypoint | internal only |
| `dashboard` | The Next.js dashboard | `localhost:3000` |

`factory` and `worker` both depend on `migrate` completing successfully
first, and each refuses to start on its own if the database's applied
migrations don't cover everything this build ships (`SchemaGuard`) — neither
one ever migrates the schema itself.

They also connect as a **different database role** than `migrate` does.
`migrate` owns the tables; `factory` and `worker` connect as
`darkfactory_app`, which owns nothing, cannot create anything, and has no
`UPDATE` grant on the append-only spec tables. This is not belt-and-braces:
a Postgres table owner bypasses its own `GRANT`/`REVOKE`, so
"`spec_revisions` is immutable" would mean nothing at all if the
application connected as the owner. `migrate` provisions the role's
password from `APP_DB_PASSWORD` (see `.env.example` and
`src/DarkFactory.Data/AppRole.cs`).

`postgres`, `factory`, `worker`, and
`dashboard` all report a Docker health check; `docker compose ps` should
show `healthy` for each once they're up. `factory`/`worker` expose
`GET /health` (liveness) and `GET /health/ready` (readiness — checks
Postgres connectivity); `dashboard` exposes `GET /api/health`.

The reference workspace server (`reference/workspace-mcp`) is **not** a
Compose service — see step 2.

## 2. From another repo

The factory needs a Workspace MCP server ([convention
doc](docs/conventions/workspace.md)) running against the repo you want it to
work on. Run the reference implementation directly on your host machine,
inside that repo:

```sh
cd /path/to/your/repo
npx @dark-factory/workspace-mcp --http 5200
```

Then, from a host that speaks MCP (e.g. Claude Code):

```sh
claude mcp add --transport http dark-factory http://localhost:5100/mcp
```

and, once the front MCP surface (`projects.*`, `work.*`) exists:

```
projects.register("http://host.docker.internal:5200/mcp")
work.submit(project_id, "…")
```

`host.docker.internal` is how the `factory` container (inside Docker) reaches
the workspace server (running directly on your host) — see
[ADR-0014](docs/adr/0014-local-mode-is-the-dev-loop.md).

Then open `http://localhost:3000` to watch the run move through
`intake → spec → plan → implement → verify → ship` on the dashboard.

## 3. The demo

Once steps 1–2 above are wired end-to-end (front MCP surface + reference
workspace server + engine + dashboard), the intended demo is: register
**this very repo** as the factory's first project and submit:

> add a `GET /health/detailed` endpoint that reports database connectivity

The factory should carry that request to an opened PR, with the spec gate
approved from the dashboard along the way — see
[ADR-0003](docs/adr/0003-pipeline-skeleton.md) for the gate and
[ADR-0004](docs/adr/0004-typed-artifacts-by-reference.md) for the artifacts
(`Spec`, `Plan`, `ChangeSet`, `TestReport`) produced along the way.

## Repository layout

```
docs/adr/                     ADR-0001..0014 — every foundational decision, why, and its consequences
docs/conventions/              Workspace (v0.1, implemented), Theme/Plugin (v0.1, mostly TODO), the call envelope
contracts/schemas/             JSON Schema for Envelope, HookResult, Spec, Plan, ChangeSet, TestReport
src/DarkFactory.Mcp/           ASP.NET Core host: MCP server, SignalR hub, outbox publisher, webhook ingress, health
src/DarkFactory.Core/          Domain: Project, WorkItem, Run, Stage, Artifact, Gate, Event, AuditEntry, PipelineStages
src/DarkFactory.Engine/        RunStateMachine (checkpoint + outbox + lease release, one transaction), GateService,
                                retry policy, default stage handlers, EngineWorker (claim/poll loop)
src/DarkFactory.Data/          EF Core DbContext (queries only), plain-.sql MigrationRunner, SchemaGuard,
                                RunLeaseStore (leased claiming), ArtifactStore, OutboxDrain
src/DarkFactory.Data/Migrations/ Hand-written, versioned .sql — the schema's source of truth (no EF Migrations)
src/DarkFactory.Migrate/       One-shot console app: applies pending Migrations/*.sql; the only thing that migrates
src/DarkFactory.Contracts/     C# types matching contracts/schemas
src/DarkFactory.Client/        MCP client wrapper: envelope, deadlines, failure classification
dashboard/                     Next.js dashboard
reference/workspace-mcp/       TypeScript reference implementation of the Workspace convention
tests/DarkFactory.Engine.Tests/ Lease/outbox/retry/happy-path tests + CrashResumeTests (real process kill + resume)
tests/DarkFactory.Mcp.Tests/   Project naming (docs/adr/0013)
```

## What's out of scope for now

Auth/OAuth, orgs/roles, billing/metering, the plugin registry and install
flow, the relay/tunnel for hosted mode, Temporal (see
[ADR-0008](docs/adr/0008-durable-orchestration.md) for the criteria that
would trigger adopting it), real theme/plugin servers, the IDE extension,
and deploy hooks.

## Development

```sh
# .NET solution (Core, Contracts, Data, Migrate, Client, Mcp, Engine, and both test projects)
dotnet build DarkFactory.sln
dotnet test DarkFactory.sln   # DarkFactory.Engine.Tests needs Docker: it spins up a real
                               # Postgres via Testcontainers and, for the crash/resume test,
                               # a real second `dotnet` process — see CrashResumeTests.cs

# Apply migrations to a local Postgres without Docker Compose
dotnet run --project src/DarkFactory.Migrate -- # reads ConnectionStrings:DarkFactory from
                                                  # appsettings.json / env, same as factory/worker

# Dashboard
cd dashboard && npm install && npm run dev

# Reference workspace server
cd reference/workspace-mcp && npm install && npm run build
node dist/index.js --http 5200 --root /path/to/a/repo   # or --root . for stdio use
```

### Changing the schema

There's no EF Core Migrations codegen here — `src/DarkFactory.Data/Migrations/`
is hand-written, versioned `.sql`, applied in order by `MigrationRunner`
(tracked in a `schema_migrations` table) and embedded into
`DarkFactory.Data.dll` so the `migrate` image needs no extra file-copy step.
To add a schema change: write a new `NNNN_description.sql` file there (next
version number, zero-padded), update `DarkFactoryDbContext`'s
`OnModelCreating` to match, and run `DarkFactory.Migrate` to apply it.
`factory`/`worker` will refuse to start until you do (`SchemaGuard`).

New tables inherit the application role's grants automatically (0002 sets
`ALTER DEFAULT PRIVILEGES`), but **not** the revocations. A new append-only
table has to `REVOKE UPDATE, DELETE ... FROM darkfactory_app` explicitly, in
the same migration that creates it.

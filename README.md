# Dark Factory

A hosted, multi-project software factory that is itself an MCP server. Hosts
(Claude Code, Cursor, CI) connect to it as MCP clients; it in turn acts as an
MCP client to workspace, theme, and plugin servers that follow the published
conventions in [`docs/conventions/`](docs/conventions/). It never holds
customer code — only orchestration state and metadata. See
[`docs/adr/`](docs/adr/) for the full set of foundational decisions.

**Status:** early scaffolding. The repo skeleton, ADRs, conventions, schemas,
and a Compose stack that boots with health checks exist today. The data
model/engine, the front MCP surface, the reference workspace server, and the
dashboard land in the steps that follow — see each area's code comments for
what's real versus stubbed.

## 1. `docker compose up`

```sh
cp .env.example .env   # set ANTHROPIC_API_KEY if you want stage agents to run
docker compose up --build
```

This brings up:

| Service     | What it is                                              | Where            |
|-------------|----------------------------------------------------------|------------------|
| `postgres`  | Postgres 16, the factory's own state (never customer code) | `localhost:5432` |
| `factory`   | The MCP server + API + SignalR hub + webhook ingress (`DarkFactory.Mcp`) | `localhost:5100`, MCP endpoint at `http://localhost:5100/mcp` |
| `worker`    | The background engine (`DarkFactory.Engine`) — same image as `factory`, different entrypoint | internal only |
| `dashboard` | The Next.js dashboard | `localhost:3000` |

All four report a Docker health check; `docker compose ps` should show
`healthy` for each once they're up. `factory`/`worker` expose `GET /health`
(liveness) and `GET /health/ready` (readiness — checks Postgres
connectivity); `dashboard` exposes `GET /api/health`.

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
src/DarkFactory.Mcp/           ASP.NET Core host: MCP server, SignalR hub, webhook ingress, health
src/DarkFactory.Core/          Domain: Project, WorkItem, Run, Stage, Artifact, Gate, Event, AuditEntry
src/DarkFactory.Engine/        Background worker: state machine, hook dispatcher, retry policy, gates
src/DarkFactory.Data/          EF Core DbContext, migrations, state store, artifact store
src/DarkFactory.Contracts/     C# types matching contracts/schemas
src/DarkFactory.Client/        MCP client wrapper: envelope, deadlines, failure classification
dashboard/                     Next.js dashboard
reference/workspace-mcp/       TypeScript reference implementation of the Workspace convention
tests/                         DarkFactory.Engine.Tests, DarkFactory.Mcp.Tests
```

## What's out of scope for now

Auth/OAuth, orgs/roles, billing/metering, the plugin registry and install
flow, the relay/tunnel for hosted mode, Temporal (see
[ADR-0008](docs/adr/0008-durable-orchestration.md) for the criteria that
would trigger adopting it), real theme/plugin servers, the IDE extension,
and deploy hooks.

## Development

```sh
# .NET solution (Core, Contracts, Data, Client, Mcp, Engine, and both test projects)
dotnet build DarkFactory.sln
dotnet test DarkFactory.sln

# Dashboard
cd dashboard && npm install && npm run dev

# Reference workspace server
cd reference/workspace-mcp && npm install && npm run build
node dist/index.js --http 5200 --root /path/to/a/repo   # or --root . for stdio use
```

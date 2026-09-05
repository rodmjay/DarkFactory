# Workspace Convention — v0.2

Status: **v0.2, implemented by the reference server** (`reference/workspace-mcp`).

**v0.1 → v0.2:** every tool moved under the `df.` root and
[`df.describe`](describe.md) replaced `workspace.describe` as a mandatory,
schema-validated handshake ([ADR-0018](../adr/0018-df-namespace-and-describe-handshake.md)).
A v0.1 server has no handshake and cannot be registered.

A Workspace server gives the factory hands and eyes on exactly one
customer repository/working tree. It is an MCP server; the factory is its
only client (see [ADR-0001](../adr/0001-hub-and-spoke-topology.md)). Every
tool call described here accepts the [call envelope](envelope.md) and
returns a `failure_class` on failure.

Design principle: **batch-first**. Prefer one round trip per intent
(`df.files.read_many` over N calls to `df.files.read`) — MCP round trips are not
free, and stage agents commonly need "all of these files" or "write all of
these files" as a unit.

## `df.describe()`

The mandatory handshake — see [describe.md](describe.md) for the full
contract and [`contracts/schemas/describe.schema.json`](../../contracts/schemas/describe.schema.json)
for the schema it is validated against. A workspace server answers with
`domain: "workspace"` and puts its identity in `effective_config`, which is
what `projects.register` reads to derive a project's name and stack hints
(see [ADR-0013](../adr/0013-automatic-project-naming.md)):

```json
{
  "name": "dark-factory-workspace-mcp",
  "convention_version": "0.2.0",
  "domain": "workspace",
  "capabilities": ["df.describe", "df.files.list", "..."],
  "requires": [],
  "effective_config": {
    "name": "darkfactory",
    "root": "/home/user/dev/darkfactory",
    "vcs": { "provider": "git", "default_branch": "main" },
    "stack_hints": ["dotnet", "typescript", "nextjs"]
  }
}
```

Unlike every other tool here, `df.describe` does **not** echo the envelope
back: the published schema is closed, so an extra property fails
registration.

## Files

- `df.files.list(globs: string[])` → `{ paths: string[] }`
- `df.files.read_many(paths: string[])` → `{ files: [{ path, content, truncated }] }`
- `df.files.search(query: string, globs?: string[])` → structured matches:
  `{ matches: [{ path, line, column, preview }] }`
- `df.files.write_many(files: [{ path, content }])` → `{ written: string[] }`

## Exec

- `df.exec.run(command: string, cwd?: string, timeout: number)` →

```json
{
  "exit_code": 0,
  "stdout_ref": "artifact_ref or inline for short output",
  "stderr_ref": "artifact_ref or inline for short output",
  "parsed": { "tests": { "passed": 42, "failed": 0, "skipped": 1 } }
}
```

`parsed.tests` is populated on a best-effort basis when the server
recognizes the test runner's output format; it is optional and may be
omitted entirely.

**Async threshold:** if `df.exec.run` is expected to (or does) exceed an
inline threshold (default: 30s), the server returns `{ job_id }` per the
async job channel in [envelope.md](envelope.md#async-jobs) instead of
blocking the MCP call, and later `POST`s the same result shape shown above
to the factory's webhook ingress.

## VCS

- `df.vcs.branch(name: string)` → `{ ok: true }`
- `df.vcs.apply_patch(patch_ref: string)` → `{ ok: true, files_changed: string[] }`
- `df.vcs.commit(message: string)` → `{ ok: true, sha: string }`
- `df.vcs.push()` → `{ ok: true }`
- `df.vcs.open_pr(title: string, body: string)` → `{ url: string }`
  - When no remote is configured (e.g. a local-only demo repo), the server
    may stub this and return a fake, clearly-marked URL
    (`https://example.invalid/pr/stub/1`) rather than failing — see the
    reference implementation.

## Deploy (declared, not implemented in v1)

- `df.deploy.plan(...)`, `df.deploy.apply(...)`, `df.deploy.status(...)` — reserved.
  v1 servers may omit these tools entirely; the factory does not call them
  in this slice. Declared here so the shape is settled before anyone
  implements it.

## Listing and dotfiles

`df.files.list` matches dotfiles (`dot: true`). A workspace server that
cannot see `.github/workflows`, `.env.example` or `.dark-factory/` when
explicitly asked for them is not much use for maintaining a repository —
those are exactly the files a build stage needs to reason about. `.git/**`
and `node_modules/**` are excluded by name instead.

## Versioning

This is v0.2, and `df.describe()` reports it as `convention_version`.
Backwards-incompatible changes bump the minor version while v0.x. The
factory supports at least two versions concurrently and adapts per server
using the version from the handshake — see
[ADR-0020](../adr/0020-convention-versioning.md) and
[describe.md](describe.md).

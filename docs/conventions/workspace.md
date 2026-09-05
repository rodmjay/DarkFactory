# Workspace Convention — v0.1

Status: **v0.1, implemented by the reference server** (`reference/workspace-mcp`).

A Workspace server gives the factory hands and eyes on exactly one
customer repository/working tree. It is an MCP server; the factory is its
only client (see [ADR-0001](../adr/0001-hub-and-spoke-topology.md)). Every
tool call described here accepts the [call envelope](envelope.md) and
returns a `failure_class` on failure.

Design principle: **batch-first**. Prefer one round trip per intent
(`files.read_many` over N calls to `files.read`) — MCP round trips are not
free, and stage agents commonly need "all of these files" or "write all of
these files" as a unit.

## `workspace.describe()`

Returns identity and capability info used by `projects.register` (see
[ADR-0013](../adr/0013-automatic-project-naming.md)).

```json
{
  "name": "darkfactory",
  "root": "/home/user/dev/darkfactory",
  "vcs": { "provider": "git", "default_branch": "main" },
  "stack_hints": ["dotnet", "typescript", "nextjs"]
}
```

## Files

- `files.list(globs: string[])` → `{ paths: string[] }`
- `files.read_many(paths: string[])` → `{ files: [{ path, content, truncated }] }`
- `files.search(query: string, globs?: string[])` → structured matches:
  `{ matches: [{ path, line, column, preview }] }`
- `files.write_many(files: [{ path, content }])` → `{ written: string[] }`

## Exec

- `exec.run(command: string, cwd?: string, timeout: number)` →

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

**Async threshold:** if `exec.run` is expected to (or does) exceed an
inline threshold (default: 30s), the server returns `{ job_id }` per the
async job channel in [envelope.md](envelope.md#async-jobs) instead of
blocking the MCP call, and later `POST`s the same result shape shown above
to the factory's webhook ingress.

## VCS

- `vcs.branch(name: string)` → `{ ok: true }`
- `vcs.apply_patch(patch_ref: string)` → `{ ok: true, files_changed: string[] }`
- `vcs.commit(message: string)` → `{ ok: true, sha: string }`
- `vcs.push()` → `{ ok: true }`
- `vcs.open_pr(title: string, body: string)` → `{ url: string }`
  - When no remote is configured (e.g. a local-only demo repo), the server
    may stub this and return a fake, clearly-marked URL
    (`https://example.invalid/pr/stub/1`) rather than failing — see the
    reference implementation.

## Deploy (declared, not implemented in v1)

- `deploy.plan(...)`, `deploy.apply(...)`, `deploy.status(...)` — reserved.
  v1 servers may omit these tools entirely; the factory does not call them
  in this slice. Declared here so the shape is settled before anyone
  implements it.

## Versioning

This is v0.1. Backwards-incompatible changes bump the minor version while
v0.x; `workspace.describe()` will grow a `convention_version` field before
v1.0 so the factory can content-negotiate across versions.

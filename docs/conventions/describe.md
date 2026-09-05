# The `df.describe` Handshake — v0.2

Status: **v0.2, implemented by the factory and by the reference workspace
server** (`reference/workspace-mcp`).

See [ADR-0018](../adr/0018-df-namespace-and-describe-handshake.md) for why
this exists, and [ADR-0020](../adr/0020-convention-versioning.md) for how
versions move.

## The namespace rule

All convention tools live under the dotted root **`df.`** —
`df.files.read_many`, `df.exec.run`, `df.vcs.open_pr`. Anything outside
`df.` is the server's own business, so a server can be Dark
Factory-compliant *and* useful standalone to a human in Claude Code
without the two surfaces colliding.

Only `df.*` names appear in `capabilities[]`.

## `df.describe()`

Mandatory. A server that cannot answer it is not registered — there is no
fallback and no inference from `tools/list`.

```json
{
  "name": "dark-factory-workspace-mcp",
  "convention_version": "0.2.0",
  "domain": "workspace",
  "capabilities": [
    "df.describe",
    "df.files.list",
    "df.files.read_many",
    "df.files.search",
    "df.files.write_many",
    "df.exec.run",
    "df.vcs.branch",
    "df.vcs.apply_patch",
    "df.vcs.commit",
    "df.vcs.push",
    "df.vcs.open_pr"
  ],
  "requires": [],
  "effective_config": {
    "root": "/home/user/dev/darkfactory",
    "vcs_provider": "git",
    "default_branch": "main",
    "stack_hints": ["dotnet", "typescript"]
  }
}
```

The response is a **published contract**:
[`contracts/schemas/describe.schema.json`](../../contracts/schemas/describe.schema.json).
The factory validates every describe response against it before storing
anything. A server that answers but does not validate fails registration,
and the validation errors come back in the error message — "your server
answered, here is exactly what was wrong with the answer" is a far better
developer experience than "registration failed."

### Fields

| Field | Required | Notes |
|---|---|---|
| `name` | yes | Shown in the registry and dashboard. |
| `convention_version` | yes | Semver. The version this server prefers to speak. |
| `convention_versions` | no | Every version it *can* speak, when more than one. Must contain `convention_version`. |
| `domain` | yes | `factory`, `workspace`, `vcs`, `deploy`, `standards`, `qa`, `knowledge`, `observability`, `ticketing`, `agent`. |
| `capabilities` | yes | Dotted `df.*` names implemented. |
| `requires` | yes | Dotted `df.*` names needed from elsewhere. Empty array if none. |
| `effective_config` | yes | What this instance is pointed at. **Never secrets** — see below. |

### `effective_config` carries no secrets

It is stored in the registry, returned by `df.servers.list`, and rendered
in the dashboard. Put the repo, branch, environment name, or subscription
*name* in it. Never a token, connection string, or key. The factory's own
`df.describe` holds itself to this: it reports its environment name and
Foundry resource name and nothing else.

## Versioning, and why `extensions` exists

`convention_version` is semver and is the value the factory content-negotiates
on. The factory supports **at least two versions concurrently**
([ADR-0020](../adr/0020-convention-versioning.md)) and adapts per server,
shimming renames and added fields where it can.

The schema rejects unknown top-level properties. That is deliberate and it
is what catches the failure that actually happens in practice: a server
sending `capabilties` would otherwise register cleanly with an empty
capability list and look fine until a run needed one of them.

But strictness alone would break the overlap window in the other direction
— a server built against 1.1 that adds a field would fail validation on a
factory that still only knows 1.0, which is exactly backwards. So:

> **A minor version may add capability only under `extensions`, or as an
> optional top-level field this schema declares.**

`extensions` is an open object. The factory ignores keys it does not
recognise. A 1.1 server therefore validates on a 1.0 factory as long as its
additions live there, and a 1.0 server keeps validating on a 1.1 factory
because everything 1.1 added is optional.

Promoting an `extensions` key to a real top-level field is a normal
convention change: the new schema declares it as optional, both versions
validate during the overlap window, and it becomes required no earlier than
the next major.

```json
{
  "name": "some-server",
  "convention_version": "1.1.0",
  "domain": "vcs",
  "capabilities": ["df.describe", "df.vcs.open_pr"],
  "requires": [],
  "effective_config": { "repo": "acme/widgets" },
  "extensions": {
    "rate_limit_per_minute": 600
  }
}
```

**v0.1 → v0.2** renamed every convention tool into the `df.` root and made
`df.describe` mandatory. v0.1's `workspace.describe` is not a `df.*` name
and is therefore not a capability; a v0.1 server has no handshake and
cannot be registered.

## Registration

`df.servers.register(url, manifest?)` performs, in order:

1. **Describe.** Call `df.describe()`. Unreachable or unparseable → the
   registration fails.
2. **Validate.** Check the response against the published schema. Invalid →
   the registration fails, with the schema violations in the message.
3. **Store.** Upsert by `(org_id, url)` — re-registering the same URL for
   the same org refreshes `live_describe` and `last_conformance_at`, and
   never creates a second row.
4. **Diff manifest against live.** The disagreement is *stored*, not
   recomputed on read. A server that claims `df.deploy.preview` in its
   manifest and does not report it live is **degraded**, not rejected: it
   stays usable for the capabilities that do work, and the dashboard shows
   what disagreed.
5. **Conformance.** Exercise capabilities against a scratch directory (see
   below), recording **one row per declared capability** so the UI can say
   *which* capability failed rather than just that something did.

Status after registration is `conformant` when the manifest agrees and
every probed capability passed, `degraded` when some part disagrees or
fails while others work, and `failed` when nothing that was probed worked.

## Conformance never runs the server's suggestions

The conformance check invokes a **fixed** set of calls with **fixed**
arguments:

- `df.files.write_many` writes a marker file into a scratch directory the
  factory names and owns (`.df-conformance/{id}/`).
- `df.files.list` lists that directory and must find the marker.
- `df.exec.run` runs exactly `echo df-conformance` with the scratch
  directory as `cwd`.

Every call carries a short deadline in the [envelope](envelope.md). The
factory never executes a command the server proposes, and never uses a path
the server suggests — a registration probe is the *last* place to hand an
unverified third party the ability to choose what runs.

Capabilities with no probe in this slice are recorded as `not_probed`,
which is not a failure; it is an honest statement that the factory did not
check.

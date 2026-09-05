# ADR-0030: Claude Code hooks map mechanically to factory commands under the `df:` namespace

## Status
Accepted — scheduled **after step 4**, not part of step 3. Recorded now so
the session data model and the `df.*` surface leave room for it.

## Context
[ADR-0014](0014-local-mode-is-the-dev-loop.md) treats local mode as the dev
loop, and the v2 brief makes the web UI the primary interface. Both leave a
gap: a developer working in a Claude Code session is editing the same
project the factory maintains specifications for, and today the factory
learns nothing about it until a commit shows up.

The obvious fix — give the session a `df.*` MCP server and hope the model
calls it — is not a fix. Anything that depends on a model *choosing* to
report is a source of silent, uneven data, which is the opposite of what an
audit trail and a drift detector need.
[ADR-0024](0024-stable-spec-ids-drift-detected-not-prevented.md) already
commits us to detecting drift rather than preventing it; detection that
fires only when the model remembers is not detection.

## Decision
A developer's Claude Code session is part of the factory whether or not the
web UI is open, and it participates through **hooks**, which fire
deterministically, rather than through tools, which fire at the model's
discretion.

A **`df-hook` CLI** is installed with the project. Every Claude Code hook
entry invokes `df-hook <event>`, and `.dark-factory/hooks.json` maps
lifecycle events to factory commands **as data**, per project — so changing
what a project does on `PreToolUse` is a config edit, not a release.

Default mapping:

| Claude Code event | Factory command | Purpose |
|---|---|---|
| `SessionStart` | `df.context.pull` | Inject spec neighborhood and standards summary into session context |
| `UserPromptSubmit` | `df.specs.query` | Attach related spec nodes |
| `PreToolUse[Write\|Edit]` | `df.specs.check` | Block or warn on edits to code whose spec reference has no approved amendment |
| `PostToolUse[Bash git commit]` | `df.specs.reconcile` | Record drift |
| `Stop` / `SessionEnd` | `df.sessions.record` | Audit |
| `WorktreeCreate` | `df.projects.select` | Bind the worktree to a project |
| `TaskCompleted` | `df.work.report` | Report completion |

Rules that make this safe to leave switched on:

- **Hooks stay fast.** They read a local cache and post to the factory
  asynchronously. A developer's keystroke latency is not allowed to depend
  on the factory being reachable.
- **Only one hook may block.** `df.specs.check` on `PreToolUse`, and only
  when the project's gate config enables it. Everything else observes.
- **The Claude session id is the idempotency key** on every call, so a
  replayed or duplicated hook is free.
- **Determinism over discretion.** Hooks fire on the event; nothing here
  depends on the model deciding to call a tool.

The hook pack, the factory MCP server entry, and the `df` skills ship
together as **a single installable Claude Code plugin**.

**Data model:** `sessions(id, project_id, claude_session_id, started_at,
ended_at)` and `session_events(session_id, event, command, payload_ref,
at)`.

## Consequences
- The factory sees hand-editing as it happens rather than inferring it from
  a commit, which is what makes
  [ADR-0024](0024-stable-spec-ids-drift-detected-not-prevented.md)'s
  reconcile useful instead of archaeological.
- `df.context.pull`, `df.specs.check`, `df.sessions.record` and
  `df.work.report` are new surface area, and they are consumed by a CLI
  rather than by an agent. They must stay cheap and non-interactive.
- The blocking hook is a genuine risk: a factory outage plus a
  misconfigured gate could stop a developer from editing a file. Hence
  blocking being opt-in per project, restricted to one event, and served
  from a local cache.
- `.dark-factory/hooks.json` is per-project config that lives in the
  customer's repo, which makes it the first piece of factory configuration
  a developer can edit without the factory's involvement. That is
  deliberate and consistent with "a developer can always bypass it."
- Out of scope for step 3. Step 3b builds the `df.describe` handshake and
  registry this would eventually register against; the hook plugin is
  scheduled after step 4.

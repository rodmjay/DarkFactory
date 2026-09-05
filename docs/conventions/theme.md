# Theme Convention — v0.1 (draft)

Status: **Draft. Mostly TODO.** v1 uses a single hardcoded default theme
resource inside the factory (see the "out of scope" list in the project
README) rather than a real external theme server. This document sketches
the intended shape so a real theme server can be built against it later.

A Theme server exposes standards and stack opinions as MCP **resources**
(not tools) — a theme is consulted for context, not commanded to act. It
never mutates anything and never has side effects.

## Intended resources

- `theme://standards` — coding standards / style guidance, as text or
  structured markdown, injected into stage-agent prompts (`spec`, `plan`,
  `implement`).
- `theme://test-command` — the command `verify` should hand to the
  workspace server's `exec.run` to run this project's test suite, plus how
  to interpret its exit code.
- `theme://stack` — declared stack conventions (frameworks, package
  manager, lint/format tooling) that stage agents should assume rather than
  re-detect from scratch every run.

## TODO before v1.0

- [ ] Decide whether a theme is one resource tree or several independently
      fetchable resources (leaning: several, so agents fetch only what a
      given stage needs).
- [ ] Decide how a project selects/overrides a theme (today: hardcoded
      default; see `plugins.list` stub in the front MCP surface).
- [ ] Decide versioning/compatibility story, mirroring
      [workspace.md](workspace.md#versioning).
- [ ] Decide whether themes can be composed (a base theme + a project-level
      override) or are strictly one-theme-per-project.

## v1 behavior

The engine reads a single hardcoded default theme's standards and test
command directly from `DarkFactory.Core` (not over MCP) when building stage
agent prompts and when `verify` needs a test command. `plugins.list` in the
front MCP surface reflects this as a resolved theme with a fixed name, and
real theme-server installation is stubbed to return `not_implemented`.

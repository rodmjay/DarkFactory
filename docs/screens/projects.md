# Screen: Projects
Route: /projects
Reads (local SQLite): projects, amendments, batches, runs, model_calls (spend)
Dispatches: df.servers.authorize
Renders:
  project                → table row: LayerBadge set, StatusChip, awaiting and deployable counts, spend
  built-in connector     → authorize row (ADR-0019)
States covered: roster, empty org, project with no servers connected, project with a parked run
Proposed additions: none

## Notes

The columns are the four things a lead checks before deciding where to
look: what is running, what is waiting on them, what is ready to ship, and
what it cost. Everything else about a project is one click away.

An org with no projects has no current project either, and therefore no
runs, no amendments and no batches. Emptying only the roster would leave
the header naming a project that does not exist and the ticker reporting
runs against it — worse than either state alone.

The empty state offers the built-in connectors rather than an illustration.
ADR-0019 makes those an authorization rather than a registration, so the
shortest path to a working factory is three buttons.

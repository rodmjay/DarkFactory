# Screen: Conversation
Route: /conversation
Reads (local SQLite): conversations, turns, retrieval, projects, sync
Dispatches: df.conversations.create, df.conversations.send
Renders:
  turn.payloads          → PayloadRenderer (markdown, dependency_graph, table, form, spec_diff, stage_timeline, code_diff)
  approval_card payload  → ApprovalCard (states: awaiting, approved, rejected; reject requires a reason)
  decision payload       → DecisionCard on the newest turn (choose, own words, leave open → sends the next turn); read-only on earlier turns (ADR-0039)
  turn.cost              → Card cost strip (tokens this turn, delta vs last)
  retrieval              → context panel: LayerBadge, SpecId, server health chips, cost figures, skill revisions
  conversation list      → Input (search), buttons
  guide (df.intake.next) → above the thread: progress line, then ONE step — decide (DecisionCard), rebuild, propose, extract, or done (ADR-0039)
Dispatches (guide): df.intake.answer, df.intake.defer, df.intake.extract, df.intake.propose, df.intake.suggest_paths
States covered: empty (no thread), thread, thinking (mid-turn), run parked and posted here, optimistic write in flight, offline, context panel open/closed; guide: each step, running (spinner + what is running), result with time, hidden when nothing is imported
Proposed additions: none

## Notes

The thread is not a chat log with special cases. Every turn is a list of
ADR-0021 payloads and goes through one renderer — which is what the
vocabulary is for. Writing this screen was the first real test of whether
the nine types cover a working conversation, and they did: prose, a
retrieved neighbourhood, a table, a clarifying form, a spec diff, an
approval gate, a parked run's timeline and a patch, with no tenth type and
no escape hatch.

The one payload this screen does not hand to `PayloadRenderer` is
`approval_card`, because approving is a `df.*` command rather than a visual
state. The design system draws the card; the app decides what pressing it
means.

A conversation opens at its newest turn. Landing at the top means the thing
that just happened is the one thing the reader has to go looking for.

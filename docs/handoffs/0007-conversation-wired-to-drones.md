# 0007 — The conversation screen reads the factory

## Instruction (verbatim)

> it still shows billing platform, and then i cant switch to the drones project

then, with a screenshot of `/conversation` showing "Conversation is not
wired to the factory yet":

> next we need to address this

## Commit hash

This report is in the same commit as the work, on branch `drones-intake`.

## What changed

**"Still shows billing platform" was the old build.** 0006's deploy had
been blocked; the dashboard on 13000 was the pre-0006 image. Deployed
(`--no-deps`, migrate first), hard refresh, gone.

**"Can't switch to drones" had no switcher to fix** — drones is the only
project and the header now names it — but clicking it led to the
conversation, which had never read the factory. That is what this wires.

**Two factory read tools**, because the screen had nothing to read with:

- `df.conversations.list(project_id)` — most recently active first, with
  turn count, amendment and awaiting counts, and `kind: intake` for an
  import's thread (ADR-0037).
- `df.conversations.get(conversation_id)` — every turn with its ADR-0021
  payloads **returned as stored**, not rebuilt, plus each amendment's
  current status and, if refused, why.

**The screen, without `?state=`:** list and thread from those tools; send
is a real `df.conversations.turn`, approve and reject real
`df.specs.approve` / `reject`, each followed by a re-read of what the
factory saved. A new conversation is created by its first message, so the
list never fills with empty threads. The pending human turn and the wait
are the only things drawn before the factory answers. Each spec diff gets
an approval card carrying the amendment's live status. Fixture copy ("412
spec nodes", "2 standards servers") is gone from the product; the context
panel and its toggle are hidden until the factory returns retrieval. Under
`?state=` the prototype is unchanged. `/` opens the conversation again.

**Bug fixed, not handed back: a rejection's reason was thrown away.**
`SpecGraphService.RejectAsync` took `rejectedBy` and `reason` and saved
neither — while the approval card refuses to reject without a reason. Both
approve and reject now write the `approvals` row the table was created for.

**Transport:** `lib/factory/mcp.ts` posts over `node:http`. Node's `fetch`
abandons a response whose headers take over five minutes regardless of the
abort signal; an architect turn can, and that limit is what lost a turn in
the earlier seed. Turns get fifteen minutes, matching the gateway.

## Verified how

| Command | Result |
|---|---|
| `dotnet build DarkFactory.sln` | exit 0, 0 warnings |
| `dotnet test --filter ConversationReadTests\|IntakeServiceTests` | 13 passed |
| planted: rejection `Reason` not written | 1 failed — `ARejectionKeepsItsReason`; restored, `sha256sum` identical |
| `dotnet test DarkFactory.sln` | exit 0 — 207 passed, 2 skipped |
| `pnpm check` | exit 0 (5 factory commands registered) |
| `pnpm test:visual` | exit 0 — 14 passed, no baseline change |
| live `tools/list` | `df.conversations.get` and `.list` present |
| live browser, `/` | lands on `/conversation`; drones empty state; `billing-platform` 0 |
| live browser, send a question | pending turn and wait shown; reply in 7s; no error; listed; survives reload |
| screenshots | `conv-2-sending.png`, `conv-3-reply.png` (scratchpad; the two label defects they showed are fixed and redeployed) |
| DB after the check | the test conversation deleted — conversations, turns, artifacts, model_calls all 0 |

Not run: `./scripts/check-stack.sh` — not a commit to `main`, and its
`down -v` would delete the drones registration (see 0006).

## Decisions I made that weren't specified

- **Reads are new tools, not MCP resources.** The brief names
  `factory://conversations/{id}`; the dashboard's client only speaks
  `tools/call`, and a resource would have needed a second code path for one
  screen. Worth revisiting when a non-dashboard host wants it.
- **A conversation is created by its first message.** The `+` clears the
  thread; the prototype still dispatches `df.conversations.start`.
- **Approval status is the amendment's, read back** — not the prototype's
  single local `decision`, which would make two cards in one thread agree.
- **`required_approvers: []`** on product approval cards. Per-project
  approvers (ADR-0017) are not modelled; an empty list is the truth.
- **The test conversation was deleted** after the live check, to keep the
  drones instance free of test data as instructed in 0006.

## Things I was wrong about

- **The header said "architect · architect".** Deployments are role-named
  (ADR-0027), so prefixing the role repeated it. Caught on the screenshot,
  fixed before commit.
- **The list said "No conversations yet" while the first turn was in
  flight** — I re-read it only after the reply. It is now re-read as soon
  as the conversation exists.

## What I did not do and why

- **Intake questions on this screen.** An intake's conversation is listed
  and marked, but its questions are not rendered in it yet. That is the next
  piece, and it is what makes extract → holes → answer usable without me.
- **The drones intake itself** — still not started; waiting on the go-ahead
  asked for in 0006 (roughly $13 for 43 documents; pilot first).
- **Cost per turn.** `model_calls` has it; the thread does not show it yet.
- **The context panel** needs the factory to return a turn's retrieval.
- **Header "Approve" badge** still has no factory query behind it.

## Next

1. Render intake questions in the intake conversation — the `form`
   payload is already in the vocabulary — answering through
   `df.intake.answer`, with re-extract and propose as actions.
2. Start the drones intake and pilot two documents.
3. Wire Amendments and Spec graph, so approved imports have somewhere to
   be seen.

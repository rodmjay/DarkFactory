# 0009 — The architect is told what the project is connected to

## Instruction (verbatim)

Rod pasted the architect's reply from the dashboard, 14 Sept 00:02 UTC:

> I can see the spec context I was handed for this project, and right now it's empty — no specification nodes exist yet. […] If you expected existing specs to be visible here and they aren't, that's worth checking on your side before we start adding nodes […] What are we building?

and wrote:

> The moonbeam should have the MCP server running and it should be detectable

## Commit hash

This report is in the same commit as the work, branch `drones-intake`.

## What changed

**The servers were running and detected; the architect was not told.**
At 00:03 UTC all three Moonbeam servers were `Conformant`, checked every
thirty seconds since the 0008 deploy, last answer 00:03:43. The 43 drones
documents had been in an intake since 21:08. The architect's context pack
held two things: the graph's spec nodes (none — nothing extracted yet) and
one built-in authoring standard. It answered correctly about what it was
shown.

**The context pack now carries both:**

- `connections` — the project's servers (its workspace by URL, and every
  server registered for the project) with the health monitor's status, last
  answer, and — if down — since when and why.
- `imports` — each intake's documents: source ref, title, one-line summary
  (the document's first blockquote), status, plus extracted / proposed /
  open-question counts. Summaries, not text: about 4k tokens rather than
  the corpus's 100k on every turn.

The prompt renders them as *Connected servers* and *Imported, not yet in
the graph*, and says "no nodes in the graph yet — but 43 imported
document(s) are awaiting extraction" instead of "this project has no
specifications". The architect skill (0.2.0) is told never to call a
project empty when it has imports, and not to propose nodes that restate an
imported document — intake's job. Template `architect/2`.

## Verified how

| Command | Result |
|---|---|
| `dotnet test DarkFactory.sln` | exit 0 — 227 passed, 2 skipped |
| planted: servers not rendered | 1 failed (`TheArchitectIsToldWhatTheProjectIsConnectedTo…`) · restored identical |
| planted: imports not rendered | 1 failed (`ImportedDocumentsAreNamed…`) · restored identical |
| deploy | factory and worker rebuilt, healthy |
| live: same question, fresh conversation | "Yes — the `moonbeam-specs` corpus server is connected and answering (last check 00:08 UTC), and its "drones" import has landed **43 documents**… imported, not yet in the graph", grouped by area, deferred documents called out, pointed at intake; no amendment |
| live: pack | connections: moonbeam-specs, moonbeam-standards, dark-factory-workspace-mcp (all Conformant); imports: drones from moonbeam-specs, 43 |
| cleanup | the verification conversation deleted; count 0 |

## Decisions I made that weren't specified

- **Another project's servers are excluded** (tested). A workspace is
  matched by URL because it is registered without a project id.
- **Standards servers are listed as connected but their content is not in
  the pack.** ADR-0023's ingest is still unbuilt; the architect can say the
  server is there, not what its rules are.
- **Up to 200 documents per import** are listed; beyond that the count is
  still right.

## Things I was wrong about

- **0008 said the architect would find the specs through intake.** Nothing
  connected the two: the architect never looked at intakes or servers.
  "Pulled into the factory" was true of the database and false of the one
  agent a user talks to.

## What I did not do and why

- **The dashboard opens the newest conversation, which was the intake's**,
  so Rod's second question went into "Intake: drones". Harmless, but the
  intake thread should not be the default; next screen work.
- **No Servers screen** — health is visible to the architect and through
  `df.servers.list`, not yet on a page.
- **Full-text retrieval of a document** on request — needs a tool or
  retrieval step; summaries answer "what is there".
- Still open from 0008: swapping `mb-drones`/`mb-standards` onto compose,
  and moonbeam-standards `43493a7`.

## Next

1. Wire the Servers screen, and stop the conversation list defaulting to an
   intake thread.
2. Extraction of the 43 documents (pilot two), with its questions shown in
   the intake conversation.

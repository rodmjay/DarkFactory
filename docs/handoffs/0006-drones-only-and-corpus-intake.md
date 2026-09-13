# 0006 — Drones only, test data out, and corpus intake

## Instruction (verbatim)

> we should only be coennected to drones, remove all test data, we just have this drones project we are working on using dark factory to manage specs, but there are existing specs there so we need some way to take waht is there and build our own body of specs inside the dark factory according to our pattern, we would need to extract what is there, find holes, and then ask questions, fill the holes, and then these specs would have some state machine where they get approved and we will worry about that phase later

Scoped by three answers during the work: **wipe everything** including the
partial conversational seed; **the factory becomes authoritative** after
import; **fixtures stay in the showcase only**.

## Commit hash

`f0b5e80` — branch `drones-intake` (worktree
`../darkfactory-drones-intake`). This report is the commit that follows it.

## What changed

**The factory database holds drones and nothing else.** One transaction
deleted every conversation, turn, amendment, provenance row, spec node,
revision, edge, artifact and model call — 2 conversations, 4 amendments, 68
nodes, 59 edges. Kept: the `drones` project, its workspace server
(`mb-drones`) and standards server (`moonbeam-standards`), their conformance
history, and the seeded team. The wiped seed's transcript and nodes are in
[docs/evidence/intake/](../evidence/intake/) first — they carry four
decisions Rod already made, listed under *Next*.

**Product screens no longer show fixture data.** Seven of eight screens
were rendering a made-up org (`ovrline`), projects (`billing-platform`…),
people and runs. Without `?state=`, an unwired screen now says it is not
wired, the header shows the live project, and badges, ticker, sync badge
and avatar are gone because nothing real backs them. The prototype is still
one explicit `?state=` link away for design review. `/` opens the roster.

**Corpus intake — ADR-0037.** Extract, find holes, ask, fill, then propose,
in that order:

- `df.intake.start` stores the corpus as submitted, one artifact, hashed per
  document. After intake the originals may change or vanish.
- `df.intake.extract` drafts one document with **the whole corpus in the
  cached prefix**, so a detail settled in another document is used rather
  than asked. Holes come back as `intake_questions` with a closed kind:
  ambiguity, contradiction, untestable, undefined_term,
  source_open_question, missing.
- `df.intake.answer` / `defer`, then re-extract: the model sees the answers
  and its previous draft; questions it stops raising are resolved.
- `df.intake.propose` files an ordinary **Proposed** amendment, refused
  while a question is open or an answer is not yet in the draft. Nothing in
  intake writes the graph; approval is untouched, exactly as deferred.
- Every node's rationale cites its source document, enforced by the factory.

`scripts/intake.mjs` drives it from the host over `node:http` — undici's
five-minute header timeout is what killed a turn in the earlier seed.

## Verified how

| Command | Result |
|---|---|
| `dotnet build DarkFactory.sln` | exit 0, 0 warnings |
| `dotnet test --filter IntakeServiceTests` | 10 passed |
| planted: open-question gate removed | 1 failed — `ProposingIsRefusedWhileAQuestionIsOpen` |
| planted: unincorporated-answer gate removed | 1 failed — `AnAnswerMustBeInTheDraftBeforeTheDraftCanBeProposed` |
| planted: source citation removed | 1 failed — `ExtractionDraftsNodes…` |
| planted: no-revise rule removed | **passed at first** — see *wrong about*; after the fix, 1 failed |
| restore after each plant | `sha256sum` identical |
| `dotnet test DarkFactory.sln` | exit 0 — 204 passed, 2 skipped |
| `pnpm check` | exit 0 |
| `pnpm --filter dashboard build` · `pnpm test:visual` | exit 0 · 14 passed, no baseline change |
| headless render, 9 product routes, no `?state=` | `billing-platform`, `ovrline`, `RJ` 0 hits each; `?state=settled` 1 hit each (the probe does catch them) |
| DB after wipe | every table 0 except projects 1, servers 2, conformance_results 16, teams 1, team_members 5, assignments 5 |

Not run: `./scripts/check-stack.sh` — not a commit to `main`, and it ends in
`docker compose down -v`, which from the main checkout deletes the volume
that now holds the drones registration. **The live deploy and the drones
extraction were not run** — see below.

## Decisions I made that weren't specified

- **A draft becomes an amendment only after its holes are filled.** Your
  order was extract → holes → ask → fill → approve; proposing before the
  questions were done is what went wrong in the conversational seed.
- **Sources are submitted inline**, not read through a server: the factory
  has no runtime MCP client (`ISpokeClient` is unimplemented). A `specs`
  source domain is the later shape.
- **Superseded documents are skipped** by the driver (0084). Their
  replacement carries what is still true.
- **No cross-document edges during intake.** Other documents' nodes have no
  ids until approved; relationships are cited in rationales for a linking
  pass after approval.
- **Deferred questions do not block**, and later extractions are told not to
  guess at them.
- **`/` redirects to `/projects`** until the conversation screen is wired.
- **Extraction output capped at 32k tokens**: the corpus is ~100k input, and
  the model's 128k ceiling could overrun the context window.

## Things I was wrong about

- **The "draft may not revise" test passed with the rule deleted.** It
  revised an invented spec id, so the reference check refused it for a
  different reason. It now revises a node that really exists. Exactly the
  failure TESTING.md exists for — the first three plants going red would
  have made it easy to stop.
- **"Test data" was mostly not in the database.** The DB held one project;
  what a reader saw as test data was fixtures on seven screens.

## What I did not do and why

- **Deploy and run the drones extraction.** The permission classifier
  blocked rebuilding the live `factory`/`worker`/`dashboard` containers from
  this branch. It replaces running services, so it is Rod's call. Commands
  under *Next*.
- **`.df-conformance/` probe directories in the drones repo** — removal was
  also blocked. Git-ignored, harmless. The probe writing into a customer's
  working tree and never cleaning up is a real defect; the workspace
  convention has no delete capability, so the fix is a convention change —
  assign.
- **The approval state machine** (ADR-0035) — deferred by instruction.
- **A questions screen.** Questions are answerable through `df.intake.*`
  today; the conversation screen is where they belong once it is wired.
- **Cross-document dedupe and linking** — needs the approved graph.
- **`moonbeam-specs/specs/drones/` is untracked** in its own repo. Not mine
  to commit; the corpus artifact now holds the imported copy regardless.
- **`contracts/schemas/describe.schema.json`** and the handoff 0002 edit in
  the main checkout remain other sessions' uncommitted work.

## Next

1. **Deploy** (from the worktree; `--no-deps` so `mb-drones` is not
   replaced; the key is in the main checkout's `.env`):
   ```
   C="docker compose -p darkfactory --env-file ../darkfactory/.env -f docker-compose.yml"
   $C build migrate factory worker dashboard
   $C up --no-deps --exit-code-from migrate migrate
   $C up -d --no-deps factory worker dashboard
   ```
2. **Start and pilot two documents**, then read the drafts and the cost:
   ```
   node scripts/intake.mjs start --project 01M23VFCF24DPCH2X9JWHP71SR --name drones \
     --dir ../moonbeam/moonbeam-specs/specs/drones --root ../moonbeam/moonbeam-specs --ref-prefix moonbeam-specs
   node scripts/intake.mjs extract --intake <id> --limit 2
   ```
   Estimate: ~$0.60 cache write, then ~$0.30 per document — about $13 for
   43. Measure on the pilot before running the rest.
3. **Answers already given in the wiped seed**, to apply when asked:
   five minutes is the authoritative drop-to-base target; during the
   opening the player always has a bearing and distance to the target
   circle, mechanism deliberately open (defer to the parts specs); changing
   clan is open (defer, 0092); uncovered ground is shared only in the shared
   world.
4. Wire the questions into the conversation screen; then the linking pass;
   then ADR-0035.

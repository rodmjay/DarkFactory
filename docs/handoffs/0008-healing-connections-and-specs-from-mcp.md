# 0008 — Connections heal, and specifications come from MCP

## Instruction (verbatim)

> we want to make sure the process of connecting to the workspace MCP server is seamless, if the dark factory detects the connection is broken it needs to heal it, it needs to be able to extract spects from MCP into its own system, this is the current north star

Scoped by two answers: **the factory heals its connection; server
processes get restart policies** (the factory never gets Docker control),
and **specs come through a spec-source convention on Moonbeam**.

## Commit hash

This report is in the same commit as the factory work, branch
`drones-intake`. Moonbeam: `b393c17`, `6edb104`, `6f74705` on
`moonbeam-mcp` `main` (not pushed).

## What changed

**Nothing used to watch a connection.** Both Moonbeam servers read
`Conformant` four days after anyone had asked them anything. Now a monitor
in the factory checks every registered server every thirty seconds (one
`df.describe`, five-second deadline). One miss is noted; the second makes it
`Unreachable`, dated from the first; a dead server is asked less often (15s
→ 5 min). **Healing is re-verification:** when it answers again the factory
re-runs the handshake, manifest diff and conformance and sets the status
that earns — nobody re-registers. `df.servers.check` does it on demand;
`df.servers.list` now carries the health fields.

**A server restart no longer costs a run.** A stage whose workspace is
unreachable is not attempted: the run waits until the next check, spends no
attempt, and posts one `run.waiting_for_server`.

**Registration rides out a restart** — a retryable describe miss is asked
twice more (1s, 2s) before refusing.

**Specifications are extracted from MCP.** New `corpus` domain
([corpus.md](../conventions/corpus.md)): `df.corpus.list` with a SHA-256 per
document, `df.corpus.get` with the text as authored. `df.intake.pull`
imports a corpus server's documents, skipping superseded/rejected, and
refuses any document whose served text does not hash to what was listed.
`df.intake.drift` reports what changed at the source since. Conformance
now probes corpus servers by fetching a document and checking its hash.

**Moonbeam (by a sub-agent, reviewed):** `mb-specs` serves the corpus
convention from the same `df/` package (`--domain corpus`); a
`df/compose.yaml` defines `mb-standards`, `mb-specs` and `mb-drones` with
`restart: unless-stopped` and healthchecks.

**Found and fixed on the way:**
- **Moonbeam's `df/` surface had been orphaned.** 0004's commit `7414531`
  was followed in the reflog by `reset: moving to HEAD~1`; `df/` sat
  untracked. Restored byte-identical as `b393c17`. moonbeam-standards'
  `43493a7` has the same pattern and was **not** touched — see below.
- **Conformance littered the customer's repo** with a directory per pass,
  which healing would have multiplied. It now writes one fixed file.
- **`mb-drones` could not run git** ("dubious ownership"); the compose
  service sets `safe.directory`.

## Verified how

| Command | Result |
|---|---|
| `dotnet test DarkFactory.sln` | exit 0 — 224 passed, 2 skipped (live-model tests) |
| planted: heal without re-verifying | 3 failed (`AnsweringAgainIsNotEnough…` and two more) · restored identical |
| planted: unreachable after one miss | 2 failed (`OneMissIsNotAnOutage`, `ASecondMiss…`) · restored identical |
| planted: pull skips the hash check | 1 failed (`APullRefusesABody…`) · restored identical |
| planted: conformance skips the corpus hash | 1 failed (`…DifferentBodyThanItListedIsDegraded`) · restored identical |
| planted: a scratch directory per pass again | 2 failed · restored identical |
| planted: runs do not wait | 1 failed (`A_run_whose_workspace_is_unreachable…`) · restored identical |
| Moonbeam `npm test` (df/) | 13 passed; sha256 and describe-schema tests shown red against planted breakage |
| deploy | migrate exit 0 (`0011_server_health_and_corpus_pull`); factory healthy |
| live: monitor | first pass checked both existing servers at 21:07:53, next at +30s, unprompted |
| live: `df.servers.register mb-specs` | `Conformant`; `df.corpus.list` Passed, `df.corpus.get` Passed |
| live: `df.intake.pull` drones | intake `01M2E9KZ15X0RAR2YWT8T1P84W`: 43 of 44 documents, 0084 (superseded) left out, 43/43 with origin hash |
| live: `docker stop mb-specs` | 41s later `Unreachable`, 2 failures, dated from the first miss; factory log: "is unreachable … Next check at 21:10:48Z" |
| live: `docker start mb-specs` | 31s later, on the backed-off check: "answered again and was re-verified: Conformant", `heal_count` 1 — nobody re-registered |
| live: `docker kill mb-specs` (restart policy) | **not demonstrated** — Docker treats `kill` as a manual stop, so `unless-stopped` did not restart it (restarts=0); started by hand. A process that exits on its own is what the policy covers, and that was not simulated |

Not run: `./scripts/check-stack.sh` — not a commit to `main`, and its
`down -v` would delete the drones registration.

## Decisions I made that weren't specified

- **`corpus`, not `knowledge`.** `knowledge` is declared and undefined;
  retrieval (standards, forever) and import (corpus, once, then owned) have
  different failure modes, so they get different domains.
- **Only the workspace server holds runs.** A standards or corpus server
  down degrades retrieval or blocks a pull, with a message; it does not hold
  a run.
- **The monitor runs in the factory, not the worker** — one factory, N
  workers, and N pings buy no better answer.
- **A server registered without a manifest has no claim to drift from**;
  its recorded describe moves with it on re-verify, or every upgrade would
  read as degraded.
- **Pulls go through `IServerProbe`** with one retry per call.
  `ISpokeClient` stays unimplemented; imports are not hot.
- **`df.intake.pull` took the drones area only** and nothing is extracted —
  extraction is model spend still awaiting Rod's go-ahead (0006).

## Things I was wrong about

- **0004 said `7414531` was committed. It was — and then reset away**, and
  nothing I ran afterwards noticed. A commit hash in a report is not
  evidence it is on a branch; `git branch --contains` is.
- **My engine test left a run behind** that failed two existing retry tests
  on the shared database. It now cleans up and tolerates others' leftovers.
- **An existing test asserted the old per-pass scratch directory.** Changed
  deliberately, with the reason in the test.

## What I did not do and why

- **Swap `mb-drones` and `mb-standards` onto the compose services.** It
  removes two running containers; the classifier refused. Until swapped they
  keep restart policy `no` — the factory will still detect and heal their
  connection, but will not bring a dead process back:
  `docker rm -f mb-drones mb-standards && docker compose -f moonbeam-mcp/df/compose.yaml up -d`.
- **moonbeam-standards `43493a7`** (the `dark-factory` standard) is
  unreachable after a `reset HEAD~1`; the file is untracked, and the
  sub-agent amended it for the corpus split without committing. Whoever
  owns that repo should decide whether the reset was deliberate.
- **The Servers screen** still renders its not-wired panel; health is only
  visible through `df.servers.list`.
- **`mb-drones` runs as root** and leaves root-owned files in the drones
  repo; moving it to uid 1000 needs a chown and a writable home for `dotnet`.
- **No moonbeam-specs spec for the `df` surface** — that repo has other
  sessions' uncommitted work.

## Next

1. Swap the two remaining Moonbeam servers onto compose (command above).
2. Wire the Servers screen to `df.servers.list`: status, last seen,
   unreachable since, "check now".
3. Extraction of the 43 pulled documents — pilot two first.
4. Surface intake questions in the intake conversation.

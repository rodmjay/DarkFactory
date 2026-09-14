# 0010 — Spec generation strategies, and proving a spec is built

## Instruction (verbatim)

> we need really innovative spec generation abilities, with ability to have different strategy swappable on demand demending on user choice, or agent choice, we need to support various strategies, depending on the project but make sure this part of the system is very much extensible

and, mid-turn:

> we want to make sure every spec has a pointer like spec id, whatever the industry standard is but we need to be able to prove that this spec is built

## Commit hash

The commit that adds this file, on branch `spec-strategies`, which is off
`drones-intake` at `c7c3b9b`, in its own worktree
(`../darkfactory-spec-strategies`).

## What changed

Two ADRs, design only, no code.

**[ADR-0039](../adr/0039-spec-generation-strategies.md) — spec generation is
a swappable strategy; the gate is not.** A strategy produces a *candidate*
(reply, diffs, questions, verification per node) and never writes the graph.
The gate is the same for every strategy. Strategies are compositions of six
closed steps (`gather`, `draft`, `critique`, `refine`, `judge`, `ask`), each
bound to a team seat. Selection: turn → conversation pin → project default →
`auto`; the agent may choose but never invisibly. Three extension tiers:
built-in C#, declarative JSON, and MCP strategy servers that use sampling.
Nine built-ins, including `adversarial` (a critic on a different model, plus a
premortem), `tournament` (N decompositions, a judge picks two, the person
picks one), `example_first` (boundary cases the person classifies become the
tests) and `reverse` (specs recovered from existing code). ADR-0037 intake
becomes one strategy with no change to its behaviour.

**[ADR-0040](../adr/0040-every-spec-is-addressable-and-proven.md) — every
spec is addressable, and "built" is a proof.** The ULID stays in code, and a
human key `DRN-142` plus a revision form `DRN-142@a1b2c3d` are added.
`[Verifies]` / `df:verifies` tags on tests, separate from `[Spec]` on code.
Every node must carry its verification condition. verify reads per-test JUnit
results. `spec_proofs` is append-only, with states implemented → verified →
deployed / stale, and proof belongs to a revision. Reconcile runs against HEAD.
Each deployed batch gets an in-toto attestation.

## Verified how

Docs only; no code changed, so no suite was run for this commit.
Every relative link in both ADRs and in this file resolves:
`grep -o '([0-9a-z./-]*\.md)' … | test -f` over each target, exit 0.

## Decisions I made that weren't specified

- **Two ADRs, not one.** Different decisions, different owners. They meet at
  exactly one point: the gate requires `verification` per node, because a
  spec nobody could test can never be proven built.
- **The ULID stays in code; the human key never goes there.** Keys collide
  across projects and change with renames.
- **The default strategy stays `conversational`** until ADR-0032's rows show
  `auto` choosing well. Landing the seam changes nothing.
- **Numbered 0039/0040 and handoff 0010** after reading `drones-intake`,
  which already holds 0037, 0038 and handoff 0009. A session committed there
  minutes before this; if it also claims 0039 or 0010, one of us renumbers.
- **Status "Proposed", not "Accepted".** These are for the architect to rule
  on.

## Things I was wrong about

Nothing yet. I had assumed ADR-0024 gave us proof of build. It gives proof
of *reference within a run's changeset*, which is much less. That gap is
ADR-0040's context section.

## What I did not do and why

- **No code.** `drones-intake` has a live session editing
  `ConversationService`, `ArchitectPrompt` and `ContextPack`, which is exactly
  where the strategy seam cuts. Refactoring there now guarantees a conflict.
  The seam goes in once that branch lands or the other session hands off.
- **The brief is not yet amended** with 0039/0040 entries. It follows in the
  same branch once the ADRs are ruled on.
- **No screen work.** The strategy chip, the candidates view and the coverage
  states go through the showcase first (SCREEN-CONTRACT).

## Next

1. Architect rules on 0039/0040.
2. Human keys (ADR-0040, step 1): migration, allocation, `df.specs.get` by
   key, keys in PR bodies. Small, visible, and it touches nothing the other
   session is editing.
3. The strategy seam with `conversational` only, after `drones-intake` merges;
   the existing conversation tests must pass unmodified.
4. `adversarial`, as the first strategy that behaves observably differently.

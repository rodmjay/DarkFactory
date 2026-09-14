# ADR-0040: Every spec is addressable, and "built" is a proof, not a claim

## Status
Proposed, 2026-09-13. Amends
[ADR-0024](0024-stable-spec-ids-drift-detected-not-prevented.md):
`df.specs.reconcile` is no longer deferred, and proof replaces "referenced"
as what done means.

## Context
Rod: every spec needs a pointer, the industry-standard kind, and we must be
able to *prove* a spec is built.

What exists proves less than it appears to. ADR-0024 gave every node a stable
ULID, and `implement` refuses a changeset unless each spec in the run is
referenced from code (`[Spec("…")]`, or a `df:spec` comment). The PR body
lists the ids. That shows a spec was *referenced*, not that it was *built*:

- **No revision in the chain.** A node revised after the code was written
  still reads as referenced. The code implements a rule that no longer
  exists.
- **No test is tied to any spec.** `verify` runs the team's test command and
  records pass and fail counts. A green suite says nothing about which specs
  it exercised.
- **The check sees the run, not the repository.** References are verified
  in the run's own changeset. A developer deleting the attribute after merge
  is invisible.
- **Nobody can say the pointer.** `01J9ZQ…` is correct and unspeakable. It
  never appears in a standup, a ticket or a commit message.

Industry practice is consistent on the shape.
ISO/IEC/IEEE 29148 requires unique identifiers and traceability. DO-178C,
ISO 26262 and IEC 62304 require bidirectional traceability from requirement
to code to test. DOORS, Jama and Polarion give requirements project-prefixed
keys. OpenFastTrace tags implementations and tests *separately* and carries
revisions in the id. JUnit XML is the de-facto interchange for test results,
and in-toto/SLSA is the standard for attesting what a build did. We adopt the
shape, not any of the toolchains.

## Decision

**Two identifiers, one identity.** `spec_id` (ULID) stays canonical, and it
is what code carries: globally unique, stable, already validated. Each node
also gets a human key, `<PROJECT_KEY>-<n>` (e.g. `DRN-142`), allocated once
at creation, monotonic, and never reused or reassigned, even after
retirement. Both resolve through `df.specs.get`. Code carries the ULID, not
the key, because keys can collide across projects and change with a project
rename, and code must survive both. The key is for people: UI, PR bodies,
commit messages, conversation. A *revision* is addressed as `DRN-142@a1b2c3d`
(the first seven characters of the revision hash). That form is used in
evidence, never in code, which would otherwise churn on every revision.

**Implementation and verification are tagged separately.** Code carries
`[Spec("…")]` / `df:spec` as now. Tests carry `[Verifies("…")]` /
`df:verifies <spec_id>`. A spec with implementing references and no
verifying test is `implemented`, not built. The `implement` check extends:
every spec in the run needs at least one implementing reference and at least
one verifying test, in the changeset or already in the repository.

**Every node carries its verification.** A proposed node must state the
observable condition that would show it holds. The spec diff gains
`verification` on created and revised nodes, and the gate
([ADR-0039](0039-spec-generation-strategies.md)) requires it. A node nobody
could test cannot be proven, so it is refused at proposal, not discovered
unprovable after deployment. This is
[ADR-0022](0022-model-assignment-is-policy.md)'s verifiability criterion
applied to the spec itself. Existing nodes without one are reported, not
retroactively rejected.

**Per-test results.** `verify` asks the test command for machine-readable
results as JUnit XML, which `dotnet test`, pytest, jest and go test all
emit. `TestReport` gains `cases[]` (`id`, `name`, `outcome`, `verifies[]`).
The factory maps a case to spec ids by reading the `df:verifies` / `[Verifies]`
tags in the test source; the source is the truth, not the test's name. A
command that emits no machine-readable results yields counts only, and every
spec in that run stays `implemented`. That is honest; nothing is assumed.

**Proof is a row, and rows are append-only.**

```
spec_proofs   id, project_id, spec_id, revision_hash, state, commit_sha, run_id?, snapshot_id?,
              test_case_ids[], test_report_ref?, batch_id?, reason?, created_at
```

`state ∈ {implemented, verified, deployed, stale}`. `stale` is written when
a newer revision exists, or when reconcile no longer finds the reference or
the test at HEAD. It is a new row, never an update. **"Is DRN-142 built?"**
means: the latest proof for its *current* revision is `verified` or
`deployed`, with no later `stale`. A revision change makes the spec unproven
until it is re-verified, because proof belongs to a revision, not a name.

**Reconcile reads the repository, not the run.** `df.specs.reconcile`
scans the workspace at HEAD through `df.files.*` for both tag kinds. It
writes `stale` rows where a proof no longer holds and reports specs with no
implementation, specs with no verifying test, and tags pointing at nothing.
It runs after every ship, on a schedule, and from
[ADR-0030](0030-claude-code-hooks-as-factory-commands.md)'s post-commit hook,
so hand edits outside the factory are caught
(ADR-0024: detected, not prevented).

**Each deployed batch is attested.** On batch deploy
([ADR-0029](0029-ordered-batches-and-batch-level-deploy.md)) the factory
emits an in-toto Statement: the subject is the commit(s), the predicate type
is `https://darkfactory.dev/attestations/spec-verification/v1`, and the
predicate carries the snapshot and each spec's key, id, revision, state, test
cases and report refs. It is stored as a content-hashed artifact and linked
from the PR and the release notes. Signing (Sigstore) is the next step, not
this ADR's. The format is chosen so that signing adds a signature, not a
redesign.

**The traceability matrix is a query.** Spec ↔ code ↔ test ↔ commit ↔
deploy is a view over `spec_proofs` and the tags:
`df.specs.trace(spec_id | key)` for one spec,
`df.specs.coverage(project_id, snapshot_id?)` for all of them. The spec
graph screen shows each node's state. ReqIF is a later export format of
`df.specs.export`.

## Consequences
- `SpecReferences` grows a second tag kind, the implementer's guidance
  changes, and implementers must write tagged tests. Runs get slower. That
  is the price of proof, and it is paid where a test can check it.
- A project whose test command cannot emit JUnit never reaches `verified`.
  The coverage screen says so; nothing is hidden.
- Human keys need a project key. It is derived from the project name at
  creation ([ADR-0013](0013-automatic-project-naming.md)) and editable only
  until the first node exists.
- Adding `verification` to the spec diff changes the model's contract. The
  architect's template version bumps, and ADR-0032's rows separate
  before from after.
- The PR body lists keys, revisions and proof state, not bare ids.
- Build order: human keys (small, visible at once) → `Verifies` tags and the
  extended check → `verification` in the spec diff and the gate → JUnit
  cases in `TestReport` → `spec_proofs` with trace and coverage → reconcile
  at HEAD → attestation.

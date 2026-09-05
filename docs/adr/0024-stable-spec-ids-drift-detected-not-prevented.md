# ADR-0024: Stable spec identifiers are referenced from code; drift is detected, not prevented

## Status
Accepted

## Context
A spec graph that has no relationship to the code it describes is
documentation, not architecture — it will drift the moment anyone touches
code outside the factory, and nobody will notice until it's badly wrong.
The factory also explicitly does not try to be the only way to touch a
project (see the product paragraph in the architecture brief), so drift
prevention by lockout isn't an option even if it were desirable.

## Decision
Every spec node's `spec_id` (a stable ULID,
[ADR-0016](0016-spec-graph-content-addressed-append-only.md)) is referenced
from the code that implements it, however that layer can carry a reference:
a C# attribute (`[Spec("01J…")]`), a structured comment, a manifest entry
for Terraform or SQL. Each standards server declares how its layer carries
the reference as part of its `df.describe()` capabilities
([ADR-0018](0018-df-namespace-and-describe-handshake.md)). Because the
factory is the one generating the code in the normal path, it applies these
references itself as part of `implement` — a human never has to remember to
tag anything.

This buys bidirectional traceability (spec → implementing symbols, symbol →
justifying spec) and computable drift: `df.specs.reconcile` reports specs
with no implementation and code with no spec, and can generate diagrams
from real structure rather than hand-maintained ones. A developer editing
code by hand outside the factory is tolerated, not blocked — reconcile
surfaces the resulting divergence and offers to update whichever side
turns out to be wrong, rather than the factory asserting it must always be
the code.

## Consequences
- `implement`'s stage handler needs to know, per file/layer it touches,
  how to attach a spec reference — this is a concrete new responsibility
  once real workspace-connected implementation lands (step 3 wiring), not
  yet exercised by this slice's stub handler.
- `df.specs.reconcile` is explicitly deferred (see "still out of scope");
  its shape is anticipated by this ADR so the reference format chosen now
  doesn't have to be revisited to make reconcile possible later.
- Drift is a report, not a gate — the factory never refuses to proceed
  because code and spec disagree; it surfaces the disagreement and lets a
  human or agent decide which side is authoritative.

# Screen contracts

The shared language between Claude Design and Claude Code. A screen arrives
as a contract and is built from one. **Pixels are reference, never spec.**

A mockup shows one state of one screen at one width with one set of data.
The contract says what the screen reads, what it dispatches, which component
renders which payload, and which states must hold — which is the part that
survives a redesign and the part a reviewer can actually check.

---

## The template

```
# Screen: <name>
Route: <path>
Reads (local SQLite): <synced tables>
Dispatches: <df.* commands>
Renders:
  <payload or region> → <Component> (states: ...)
States covered: <empty, syncing, offline, ...>
Proposed additions: none | <Component/state/token not in DESIGN.md, one line each>
```

Contracts live in `docs/screens/<name>.md` and are updated in the same
commit as the screen they describe. A contract that has drifted from its
screen is worse than no contract, because it is read as true.

---

## The two rules

**For Claude Design** — goes in every design prompt:

> Every screen is expressed as a screen contract using only components and
> payloads named in `packages/ui/DESIGN.md`. Anything missing goes under
> *Proposed additions*, never inline.

Claude Design reads `packages/ui/DESIGN.md` and the tokens from GitHub. It
may not invent colours, fonts, or component variants. Something the system
lacks is a proposal, exported as a note — not a fait accompli in a PNG.

**For Claude Code:**

> Implement from the contract. If it references a component or state not on
> the showcase, add it to `packages/ui` first — token, states, showcase
> entry, screenshot baseline — and only then build the screen.

The showcase at `dashboard/app/design` is the living spec. If a screen uses
something that is not on the showcase, the screen is wrong. This is the
direction of flow, and it only works in one direction: a component earns its
place in the system by being specified, shown, and baselined, and screens
compose from what is there.

---

## Why the fields are these fields

**Reads (local SQLite)** — not "data" or "API". Per ADR-0031 a page never
fetches; it renders the synced mirror. Naming the tables is how a reviewer
checks that what the screen needs is actually in the sync rules, before the
screen is built and the gap shows up as an empty page.

**Dispatches** — every write is a `df.*` command through the PowerSync
backend connector. There are no raw table writes from a client. Listing the
commands makes an unimplemented one visible at contract time.

**Renders: payload → Component** — the binding is to the ADR-0021 rendering
vocabulary, not to prose. A payload type that has no component, or a
component invented for a payload that does not exist, is caught by reading
this line.

**States covered** — a mockup shows the populated state because that is the
one worth drawing. Empty, syncing, offline, error and over-budget are the
states that decide whether a screen is usable, and they are the ones that
get skipped unless something asks for them by name.

**Proposed additions** — the pressure valve. Design will need things the
system lacks; the rule is not that it never happens but that it never
happens *inline*. A proposal is visible, costed, and lands in `packages/ui`
as a real component before a screen depends on it.

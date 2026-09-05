# ADR-0033: The design system is a workspace package, and colour is tokens only

## Status
Accepted

## Context
Step 4 makes the web UI the primary interface. It has to render the ADR-0021
vocabulary ([spec diffs](0021-rendering-vocabulary.md), dependency graphs,
stage timelines, approval cards), the [team roster](0028-project-agent-teams.md),
[ordered batches](0029-ordered-batches-and-batch-level-deploy.md), server
health, and per-member cost — all of it dense, all of it in one product.

Building that surface a screen at a time produces a system by accretion:
three greens that disagree about what "passed" means, a status colour chosen
because it was to hand, and no answer to "is this readable" beyond someone
squinting at it. It also leaves nothing to hand a designer. Claude Design
needs a source of truth to prototype against, and prose describing an
intention is not one.

The dashboard shells from step 1 are scaffolding. This is the point at which
they stop being scaffolding.

## Decision

**Tailwind CSS v4 and shadcn/ui.** shadcn components are copied into the
repository, not installed as a dependency. That is the model: when upstream
fixes a focus-management bug we take the fix, and when we disagree with a
visual decision we simply do not.

**`packages/ui` is the sole home of visual code.** A pnpm workspace package,
`@dark-factory/ui`, consumed by `dashboard/`. It holds tokens, components and
their styles and nothing else — no data fetching, no routing, no app state.
Components take props; the app wires them. The package exports TypeScript
source and the app compiles it (`transpilePackages`), because a build step
between a design system and the single app that consumes it only buys stale
artifacts.

This retires the web surface's npm lockfile in favour of a pnpm workspace at
the repository root.

**Colour is tokens only, enforced by the compiler.** Every colour used
anywhere in `dashboard/` or `packages/ui` is a semantic CSS variable —
surfaces, text, one accent, stage status, spec layer, diff kind, server
health. `tokens.css` declares `--color-*: initial`, which deletes Tailwind's
default palette outright: `bg-gray-800` is not a class that exists, and the
build produces nothing for it. A grep check and a lint rule follow as a
second net, but the primary defence is that the class cannot be built.

**The accent is spent only on "needs you".** One accent hue, used for the
actions that stop the factory until a person acts — approve an amendment,
answer a parked agent, deploy a batch — and for the two statuses that mean
the same thing (`parked`, `over-budget`). It is never the generic "primary
button" colour.

**Contrast is a test, not a claim.** `packages/ui/scripts/check-contrast.mjs`
parses `tokens.css`, converts OKLCH to sRGB, and asserts every text-on-surface
and label-on-chip pair against WCAG AA in both themes, with WCAG 1.4.11's 3:1
for graphics that are the sole carrier of a meaning. It parses the stylesheet
rather than a duplicated table, so there is no second copy of the palette to
drift.

**The showcase is the living specification.** `dashboard/app/design` renders
every token and every component in every state, in both themes. If a token or
a state is not on that page, it is not in the system. A Playwright screenshot
of it in both themes makes visual drift reviewable in a pull request.

Dark is the primary theme; light is equally finished, not an afterthought.
The reasoning behind the specific choices — Instrument Sans, IBM Plex Mono, a
warm neutral ramp, the hue assignments for each status — is recorded in
[`packages/ui/DESIGN.md`](../../packages/ui/DESIGN.md), which is the second
artifact handed to Claude Design alongside `tokens.css`.

## Consequences

- A new screen is assembled from existing tokens and components, or it is a
  deliberate addition to the system. There is no third option where someone
  picks a colour.
- Adding a stage status, a diff kind or a server health state is a change in
  `tokens.css` and `src/lib/vocabulary.ts`, and it appears on the showcase
  without anyone remembering to add it there.
- `layer` is an open string (`specdiff.schema.json`) because customers'
  standards servers declare their own layers (ADR-0023). The system ships
  five layer hues plus a neutral `other`, so an unrecognised layer renders
  legibly instead of being dropped or given a colour nobody chose.
- Deleting Tailwind's palette means third-party components pasted in without
  restyling render colourless rather than nearly-right. That is the intended
  failure mode: nearly-right is how a design system dies.
- The repository now has a pnpm workspace root, and the web surface builds
  with pnpm rather than npm. `dashboard/package-lock.json` is removed.
- Rendering the ADR-0021 vocabulary is a component contract (`PayloadRenderer`
  and the components under it), so a plugin proposing a new component type
  through the convention process (ADR-0020) is proposing a change to a named,
  reviewable surface rather than to "the dashboard".

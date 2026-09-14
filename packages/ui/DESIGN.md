# Dark Factory — Design system

The shared visual language for the web app, and the source of truth handed
to Claude Design for prototyping. Everything visual lives in this package;
`dashboard/` imports from `@dark-factory/ui` and wires components to data.

Decisions recorded in [ADR-0033](../../docs/adr/0033-design-system.md).

**Status:** steps 1 and 2 are complete — tokens, themes, typography, the
shadcn base set, the seventeen domain components, the showcase, the visual
regression suite, the tokens-only check, and this document. Step 3 is the
export bundle for Claude Design.

---

## 1. What this is for

Dark Factory is a tool a lead engineer lives in. Not a chatbot, not a
marketing site, not a dashboard someone glances at once a week. The person
using it has a spec graph with hundreds of nodes, a backlog of amendments,
several teams of agents spending real money, and a batch mid-flight. They
need to find one thing among many, quickly, without the interface competing
for their attention.

That produces three rules the whole system answers to:

**Restrained.** The default state of every surface is quiet. Colour is
information, never decoration. If everything is emphasised, the one stage
that is actually parked does not stand out.

**Editorial.** Text is the product — a spec node *is* a sentence. The type
is set to be read, with a real scale, generous measure on prose, and a
monospace that does not read as a terminal dump.

**Dense but calm.** 13px is the working size and rows are tight, because a
lead needs forty stages on one screen. Density comes from tight spacing and
a small type scale, not from shrinking whitespace to nothing or removing the
rules that group things.

---

## 2. The three recorded choices

### Display and body: **Instrument Sans**

A grotesque with slightly narrow proportions and tight default spacing, so a
dense table stays dense without tracking it down until it hurts to read.

*Why not Inter:* Inter is the shadcn default and the default of roughly
every developer tool shipped since 2021. It is excellent and it is invisible
— which is the problem, because it makes the product look like a template.
Instrument Sans is close enough in metrics that nothing about the density
argument changes, but its higher-contrast terminals, single-storey `g` and
narrower `a` give headings a voice without needing a second family.

*Why one family and not a display/body pairing:* an editorial serif for
headings was the obvious move and it is the wrong one here. Headings in this
product are wayfinding — "Batch 14", "Stage timeline", "Team" — not voice.
A serif would make section labels feel like article titles in an app where
the reader is scanning for a spec id, and it would cost a second webfont on
every page. One family, worked hard across weights and sizes.

Loaded by the app (`dashboard/app/layout.tsx`) via `next/font/google` as
`--font-instrument-sans`.

### Monospace: **IBM Plex Mono**

Chosen for the volume of it. Spec ids (26-character ULIDs), content hashes,
`df.*` capability names, deployment names, diffs, token counts and costs are
on almost every screen — mono is not an accent in this product, it is a
second body face.

Plex Mono is humanist rather than mechanical, so a screen full of it reads as
prose-adjacent rather than as terminal output, and it sits with a grotesque
without either looking borrowed. It has a slashed zero and an unambiguous
`1`. (Crockford base32 already excludes `I`, `L`, `O` and `U` from ULIDs, so
the remaining risk is digits against letters, which is exactly where Plex is
careful.)

*Why not JetBrains Mono or Geist Mono:* the same objection as Inter — they
are what a developer tool looks like by default. Plex is also narrower, which
matters when a 26-character id has to fit in a table cell.

Loaded as `--font-ibm-plex-mono`.

### Neutral ramp temperature: **warm** (OKLCH hue 70, chroma 0.004–0.010)

Every neutral in the system carries a small amount of warm chroma. Never
zero.

A pure-gray interface reads as unfinished — as a screen someone has not
themed yet. A warm ramp reads as ink on paper, which is the right metaphor
for a product whose subject is a body of written specification. It also does
real work for the palette: the accent is a cool iris, and a cool accent on a
warm ground registers as a *signal* rather than as one more colour, in a way
a cool accent on a cool-gray ground never quite does.

The chroma is small enough (≤0.010) that nothing reads as tinted; it reads as
"not quite gray", which is the intent.

---

## 3. Rules

These are enforced in code where enforcement is possible, and the enforcement
mechanism is named for each one.

### Tokens only — no raw colour, anywhere

No `bg-gray-800`, no `text-red-500`, no hex, no inline colour. Every colour in
`dashboard/` and `packages/ui` is a semantic token.

*Enforced by the compiler.* `tokens.css` opens its theme block with
`--color-*: initial`, which deletes Tailwind's default palette. `bg-gray-800`
is not a class that exists; the build produces nothing for it and the colour
simply does not appear. A grep-based CI check and an ESLint rule land in step
2 as a second net, but the primary defence is that the class cannot be built.

### One accent, spent only on "needs you"

`--accent` (iris) marks actions the user must take: approve an amendment,
answer a parked agent, deploy a batch. It is the `needs-you` Button variant
and the `parked` / `over-budget` status family, and nothing else. It is never
"the primary button colour" in the generic sense — the everyday button is
neutral.

This is the one rule most likely to erode, and the one whose erosion costs
the most: the moment the accent means "clickable", the interface loses its
ability to say "you, specifically, right now".

### Contrast is checked, not asserted

`scripts/check-contrast.mjs` parses `tokens.css`, converts OKLCH to sRGB, and
asserts 162 pairs across both themes:

- Every text token on every surface: **4.5:1** (AA body).
- Every status / layer / diff / health label on its own chip: **4.5:1**. Small
  labels do not get the large-text exemption.
- Graphics that are the *sole* carrier of a meaning — the focus ring, a form
  control's boundary, a diff gutter marker, the conflict band's edge:
  **3:1** (WCAG 1.4.11).
- Chip and band edges: perceivable only. They reinforce a label that is
  already checked at 4.5:1; holding them to 3:1 would force every status chip
  to wear a hard outline, which is the exact noise a quiet palette exists to
  avoid.

Run with `pnpm check:contrast`. It parses the stylesheet rather than a
duplicated table of values, so there is no second copy of the palette to
drift: a token edited to a prettier colour that fails AA fails the check in
the commit that made it pretty.

### One focus ring

`FOCUS_RING` in `src/lib/styles.ts`, spread by every interactive component:
a 2px `--accent-ring` outline at a 2px offset, on `:focus-visible` only. A
focus ring that differs by a pixel between a Button and a Tab is a focus ring
nobody trusts.

### Motion has two speeds and honours the OS

`--motion-fast` (120ms) is state: hover, press, check, colour. `--motion-slow`
(260ms) is layout: a sheet arriving, a section opening. There is nothing in
between, so there is nothing to argue about.

`prefers-reduced-motion: reduce` collapses both to 1ms globally, in
`tokens.css`, unconditionally — no component has to remember. It collapses to
1ms rather than to `none` so `transitionend` handlers still fire.

### The package holds no data, no routes, no app state

No fetching, no routing, no stores. Components take props; the app wires them.
The only exception is `React.useState` internal to a component's own
interaction (a Radix primitive's open state).

### Class names are written out, never interpolated

Tailwind scans source text. `` bg-status-${status}-fill `` is a class Tailwind
never generates. Every family keyed by a database value lives as an explicit
map in `src/lib/vocabulary.ts`.

---

## 4. Tokens

All in `src/tokens.css`. Authored in OKLCH: perceptual lightness is the axis
contrast lives on, so holding `L` fixed across a row of hues gives status
colours that genuinely read as equally loud.

Light lives on `:root, .light`; dark on `.dark`. Both are addressable as
classes, which is what lets the showcase render them side by side.

### Surfaces

| Token | Role |
|---|---|
| `--surface-page` | the page ground |
| `--surface-card` | a card or panel |
| `--surface-raised` | a control sitting on a card |
| `--surface-overlay` | dialog, sheet, popover, menu |
| `--surface-sunken` | a well: hover fills, code blocks, inset areas |
| `--scrim` | the dim behind a modal |
| `--border` | hairline, for grouping |
| `--border-strong` | an emphasised edge |
| `--border-control` | a form control's boundary — meets 3:1, unlike the hairlines |

Elevation is lightness, not shadow. Dark climbs the ramp; light descends it.
There are two shadows (`--shadow-raised`, `--shadow-overlay`) and in dark they
are nearly invisible because the ramp already does the work.

### Text

`--text-primary` · `--text-secondary` · `--text-muted` · `--text-inverse`

### Accent

`--accent` · `--accent-hover` · `--accent-text` · `--accent-fill` ·
`--accent-border` · `--accent-fg` · `--accent-ring`

Plus `--destructive` / `--destructive-foreground` for genuinely destructive
actions, which is a different idea from "needs you".

### Stage status

`--status-{pending,running,passed,parked,failed,over-budget}-{fill,border,text}`

| Status | Hue | Reasoning |
|---|---|---|
| `pending` | neutral | nothing has happened yet; it should recede |
| `running` | 245, low chroma | alive but not demanding — this is the state a timeline is in most of the time. The only motion in the system is its `breathe` pulse |
| `passed` | 150 | calm green; a passed stage is not an achievement to celebrate, it is the expected case |
| `parked` | 285 — **the accent itself** | the agent is waiting on a person |
| `failed` | 27 | red, and deliberately nothing like parked. "The machine broke" and "the agent is waiting on you" are different days |
| `over-budget` | 330 | the accent's hot neighbour — needs a person, like parked, but is not the same problem. 45° from parked, so the two never blur |

### Spec layers

`--layer-{product,api,data,infra,ui,other}-{fill,border,text}`

Five hues (15 / 70 / 150 / 215 / 295) at roughly a third of the status chroma,
so a row of layer badges reads as a legend rather than as five competing
alerts.

`other` is not decoration: `layer` is an **open string** in
[`specdiff.schema.json`](../../contracts/schemas/specdiff.schema.json) —
customers' standards servers declare their own layers (ADR-0023). Anything
outside the built-in five renders in the neutral `other` slot rather than
being dropped or given a colour nobody chose. `layerKey()` does the mapping.

### Diff

`--diff-{added,removed,changed,conflict}-{fill,border,text,marker}`

`conflict` is the loudest thing the system can draw, and the only place we
spend maximum chroma (hue 340, C 0.21–0.25) plus a load-bearing border. "This
contradicts the rule you set in March" is the single most valuable sentence
the product says; it does not get to be a subtle tint.

`conflict` and `over-budget` are both in the magenta family, 10° apart. That
is deliberate and safe: they never appear in the same view (one is a spec
diff, the other is a cost or member view), and both mean *stop, a person is
required*. Within a diff, `conflict` is 47° from `removed` and further from
everything else.

### Server health

`--server-{conformant,degraded,unreachable}-{fill,border,text}`

`unreachable` is **neutral**, not red. A server we cannot reach is usually a
network fact rather than a fault, and colouring it red trains people to ignore
red.

### Shape, motion, type scale

- Radii: `--radius-control` (6px) · `--radius-card` (10px) · `--radius-pill`.
  Three, and no argument about a fourth.
- Motion: `--motion-fast` 120ms · `--motion-slow` 260ms · `--motion-ease`
  `cubic-bezier(0.22, 1, 0.36, 1)`.
- Spacing: Tailwind's `--spacing` base of `0.25rem`. Prefer the 1 / 1.5 / 2 /
  3 / 4 / 6 / 8 / 10 steps; anything above 10 is a layout decision, not a gap.
- Type scale: `2xs` 11px · `xs` 12px · `sm` 13px (**the working size**) ·
  `base` 14px · `md` 16px · `lg` 18px · `xl` 22px · `2xl` 28px · `3xl` 36px.
  Line-height and tracking are bound per step; display sizes are tracked in.

### Utilities defined by the system

`motion-fast` · `motion-slow` — transition duration and easing by intent.
`tnum` — tabular figures, for anything compared down a column.
`breathe` — the running-stage pulse.
`anim-overlay` / `anim-pop` / `anim-sheet-{right,left,bottom}` — enter and
exit, keyed off Radix `data-state`.

---

## 5. Components

Copied into the repo (`src/components/ui/`) rather than installed. That is the
shadcn model and it is the right one here: when upstream fixes a
focus-management bug we take the fix, and when we disagree with a visual
decision we simply do not.

Every component below renders in every listed state on the showcase at
`/design`.

### Base set

| Component | Props of note | States shown |
|---|---|---|
| `Button` | `variant`: `default` · **`needs-you`** · `outline` · `ghost` · `danger` · `link`; `size`: `sm` · `md` · `lg` · `icon`; `asChild` | every variant × size, disabled, with leading and trailing icons, icon-only |
| `Badge` | `variant`: `default` · `outline` · `accent` · `solid`; `asChild` | all four, with and without an icon |
| `Card` | `CardHeader` · `CardTitle` · `CardDescription` · `CardAction` · `CardContent` · `CardFooter` | populated, and a skeleton equivalent |
| `Dialog` | Radix Dialog; `DialogContent` takes `showCloseButton` | trigger-driven |
| `Sheet` | `side`: `right` · `left` · `bottom` | trigger-driven |
| `Tabs` | Radix Tabs; underline rather than pill — a filled pill row competes with dense content, a rule does not | active, inactive, disabled |
| `Table` | `TableHeader` · `TableBody` · `TableFooter` · `TableRow` (`data-state="selected"`) · `TableHead` · `TableCell` · `TableCaption` | header, body, selected row, hover, footer total, tabular figures |
| `Tooltip` | Radix Tooltip + `TooltipProvider` | rendered open, and hover-driven |
| `Popover` | Radix Popover; `align`, `sideOffset` | rendered open |
| `DropdownMenu` | items, checkbox items, radio items, labels, separators, shortcuts, submenus; `DropdownMenuItem` takes `variant: default \| danger` | trigger-driven |
| `Command` | cmdk; `CommandDialog` wraps it for ⌘K | rendered inline, with groups, shortcut, disabled item, empty state |
| `Input` | native props | default, placeholder, filled, `aria-invalid`, disabled |
| `Textarea` | native props; `field-sizing-content` | default, disabled |
| `Select` | Radix Select; `SelectTrigger` takes `size: sm \| md` | value, placeholder, disabled |
| `Switch` | Radix Switch | on, off, disabled on, disabled off |
| `Toast` | Radix Toast; `variant`: `default` · `needs-you` · `danger`. Built on Radix rather than a toast library so the showcase can render every variant statically — an imperative `toast()` has no state a screenshot can catch | all three, with and without an action |
| `Skeleton` | — | text lines, avatar, button |
| `ScrollArea` | Radix ScrollArea | scrolling content, both orientations |
| `Separator` | `orientation` | horizontal, vertical |
| `Avatar` | `AvatarImage` · `AvatarFallback` | image, fallback, several sizes |
| `Label` | native props | with each control |

### Domain primitives

The three smallest ones. The other seventeen are in § 8.

| Component | Props | States |
|---|---|---|
| `StatusChip` | `status: StageStatus`, `compact?: boolean` | all six statuses, full and compact; `running` breathes |
| `LayerBadge` | `layer: string` (open — unrecognised values fall to `other`) | five built-in layers plus an unrecognised one |
| `SpecId` | `id: string`, `truncate?: boolean` | truncated (`01JA…PT6FE2`) and full |

### Vocabulary exports

`src/lib/vocabulary.ts` is the single place a status, layer, diff kind or
health value is turned into classes:

```ts
STAGE_STATUSES, StageStatus, statusChip, statusDot, STATUS_LABEL
NEEDS_YOU, needsYou(status)
SPEC_LAYERS, SpecLayer, LayerKey, layerChip, layerKey(layer)
DIFF_KINDS, DiffKind, diffBand, diffMarker
SERVER_HEALTHS, ServerHealth, serverChip
```

---

## 6. The showcase

`dashboard/app/design` — every token and every component in every state, in
both themes. If a token or a state is not on this page, it is not in the
system.

- `?theme=dark` (default) · `?theme=light` · `?theme=both` (side by side).
- The mode is set on the streamed HTML as well as in an effect, so
  `?theme=light` never flashes dark — this page gets screenshotted.
- The root element carries the theme class, not only the wrapper, because
  Radix portals mount on `document.body`; without that a dialog or tooltip
  would render in the document's theme rather than the one being shown. In
  `both` mode the root must pick one, and overlays follow dark.

---

## 7. Consuming the package

```tsx
import { Button, StatusChip, LayerBadge, cn } from "@dark-factory/ui";
```

- The package exports TypeScript source, compiled by the app via
  `transpilePackages: ["@dark-factory/ui"]`. No build step between the design
  system and the app that consumes it — a build step there only buys stale
  artifacts.
- `dashboard/app/globals.css` imports `@dark-factory/ui/tokens.css` and
  declares `@source "../../packages/ui/src"` so Tailwind scans the package's
  class names.
- The package names the font tokens; the app loads the faces. Loading a font
  is app wiring, not visual design.

---

## 8. Domain components

What the product is made of. Every one renders in every listed state on the
showcase; if a state is not there, it is not in the system.

Typed against `contracts/schemas/` where a schema exists — today that is
`specdiff.schema.json`, `describe.schema.json` and `decision.schema.json` —
and against types in
`packages/ui/src/types` where one does not. Those are marked **proposed**
below and in the source, so writing the schema stays a deliberate act
(ADR-0021's rule that the vocabulary grows on purpose) rather than a
transcription of whatever the first component happened to need.

| Component | Props of note | States shown | Shape |
|---|---|---|---|
| `StageTimeline` | `run_id`, `snapshot_id`, `stages: StageTimelineEntry[]`, `onAnswer` | running, parked, failed, over-budget, completed — every status token appears | proposed `stage_timeline` |
| `SpecNodeCard` | `node: SpecNode`, `selected` | default, selected, retired, drifted | ADR-0016 + ADR-0024 |
| `SpecDiff` | `diff: SpecDiffDocument`, `conflicts`, `summary` | with conflicts, without, empty | `specdiff.schema.json`; `conflicts` proposed |
| `ApprovalCard` | `approval: Approval`, `canDecide`, `onApprove`, `onReject` | awaiting, approved, rejected, waiting-on-others | proposed `approval_card` |
| `DecisionCard` | `decision: Decision`, `state` (`open` · `answered` · `deferred`), `busy`, `onChoose`, `onOther`, `onDefer`, `defaultMode` | open with a recommended option, open without own-words or leave-open, answering in own words, leaving open, answered, deferred | `decision.schema.json`; ADR-0041 |
| `AmendmentRow` | `amendment: Amendment`, `inBatch`, `seq` | default, in-batch, blocked-by-dependency | ADR-0029 |
| `BatchCard` | `batch: Batch`, `onDeploy` | composing, running, blocked-by-verify, deployable, deployed | ADR-0029 |
| `TeamMemberCard` | `member: TeamMember` | native agent, agent server, persona (priced), over-budget | ADR-0028 |
| `PersonaCard` | `persona`, **`modelFamily` (required)**, `speed`, `role`, `onInstall` | free, priced, installed | ADR-0028 (deferred) |
| `ServerCard` | `server: Server`, `onAuthorize` | conformant, degraded, unreachable, built-in-connector | `describe.schema.json` |
| `MetricTile` | `label`, `value`, `unit`, `delta`, `delta_is_good`, `series`, `period` | up, down, flat, no-data | proposed `metric` |
| `CostBar` | `segments: CostSegment[]`, `budget`, `compact` | default, over-budget segment | ADR-0032 `model_calls` |
| `CodeDiff` | `patch`, `specAnnotations`, `filesChanged` | default with spec gutter, with conflict marker | proposed `code_diff` |
| `DependencyGraph` | `nodes`, `edges`, `focus`, `onSelect` | a focused neighbourhood | proposed `dependency_graph` |
| `PayloadRenderer` | `payloads: Payload[]` | one example per payload type, a reply with three, an unknown type | ADR-0021 |
| `SyncStatus` | `state: SyncState`, `pending`, `compact` | synced, syncing, offline, conflict | ADR-0031 |
| `ProvenancePopover` | `provenance: Provenance`, `children`, `defaultOpen` | on a spec node and on a cost figure | ADR-0016 |

### Decisions worth stating

**Conflicts come first in a `SpecDiff`, and are the loudest thing the system
draws.** A reader scanning for what is new will scroll past a conflict placed
below the additions. "This contradicts the rule you set in March" is the one
thing a spec graph knows that a person does not, and burying it wastes the
product's whole argument.

**A parked stage opens with the agent's question inline.** The alternative —
a badge you click to find out what it wants — makes the one state that
requires a person the one that costs an extra step. A run parked overnight
because nobody expanded the row is the failure this exists to prevent.

**Deploy lives on `BatchCard` and nowhere on a run.** Deploy is a batch
action (ADR-0029): a run that verifies clean is *deployable*, not deployed.
The button's placement is the ADR made visible; on a run it would mean the
schema was lying about what a release is.

**Rejection requires a reason; approval does not.** A rejection that says
only "no" sends the proposer back to guess, and the reason is the entire
content of the decision. Approval needs none because the diff already says
what was agreed to.

**A recommended option is marked, never preselected.** A `DecisionCard`
carries the proposer's view as a "Recommended" badge on one option, and
nothing is selected until a person picks. A card that arrived with the
recommendation already chosen would make "accept the default" the path of
least resistance for every question in the list, and ADR-0041 records the
choice as the person's, not the proposer's. Leaving a decision open asks for
a reason for the same reason a rejection does. Read-only (no callbacks, as
`PayloadRenderer` draws it) keeps the options at full contrast rather than
the usual disabled fade — a consequence at half opacity is one nobody reads.

**`PersonaCard` requires `modelFamily` as a prop.** ADR-0028 makes stating
the underlying model a requirement rather than a courtesy — two personas on
one model may differ only by speed preset, and a price with no model is
unreadable. Making it required means a card cannot be built without it.

**`offline` and `unreachable` are neutral, not warnings.** Local-first
(ADR-0031) makes working offline a supported mode, and a server the factory
cannot reach is usually a network fact. Colouring either as a fault spends
the same signal that `conflict` and `parked`, which genuinely need a person,
have to use — and trains people to ignore it.

**`DependencyGraph` is force-free.** A force simulation re-lays-out on every
render, so the same neighbourhood looks different each time and nothing about
the picture is memorable. Focus in the centre, neighbours on a stable ring,
same data always the same picture. Real graph layout is later work.

**`PayloadRenderer` has no escape hatch.** No raw-HTML payload, no `custom`
type. The moment one exists every plugin uses it and the vocabulary stops
meaning anything. An unknown type renders as a named gap rather than as
nothing — a payload silently dropped is indistinguishable from an agent that
said nothing.

**`CodeDiff` line numbers come from the hunk headers.** Counting rendered
rows produces numbers that look authoritative and are wrong by however many
lines the hunk skipped, and a reader will quote them. Removed lines get no
number, because they have none in the new file. Spec annotations are keyed by
file line so they survive re-rendering with more context.

### The two proposed shapes, and what they actually cost

Both were needed to render something the components already have to show,
and neither is as large as "propose a schema" sounds.

**`conflicts` on a spec diff is now a projection over data that exists.** A
conflict is an `edge_add` of kind `conflicts_with`, and rendering "this
contradicts the rule you set in March" needs a sentence and a date that a
bare edge does not carry. Proposing this shape turned up a provenance bug
rather than a modelling gap: `specdiff.schema.json` has `rationale` on every
element, the model answers it, and none of it was readable afterwards — on
`creates` and `revises` it was *buried* inside `ContentJson` and never read
back out; on `retires`, `edge_adds` and `edge_retires` it was *dropped* at
translation, because those internal records had no field for it. Two faults
that rendered identically as `rationale: null`.

Fixed in `bc2834e`, with a two-pass backfill (migration 0009) and ADR-0016
amended to say an amendment records *why* per change, not only who and when.
So the sentence this component needs is now persisted and rendered, and the
date comes from the contradicted node's revision provenance. What remains is
a projection, not a schema to design.

*Method note, since it is the transferable part:* the bug was found by typing
components against the wire and following a gap backwards instead of working
around it. Reading the factory from outside found something that reading it
from inside had not.

**`stage_timeline` is a join nobody owns.** `stages`, `artifacts` and
`model_calls` all exist and none of them knows about the others. The join is
the thing worth rendering — who did the stage, what came out of it, what it
cost — and it has no schema because no single table is its home.

**Markdown is a splitter, not an engine.** It handles paragraphs,
blockquotes, lists, and inline `**bold**` / `*italic*` / `` `code` `` — and
emits React elements, so there is no path by which model-authored text
becomes markup. Anything structural has a typed payload: a response that
wants a table sends a `table`.

---

## 9. Wire shapes

Two things about the ADR-0021 payload wire were found while typing these
components, and both were fixed at the source rather than worked around here.
Recorded because the vocabulary has seven more types to grow into, and
whoever writes those schemas should see the decisions rather than inherit
them.

**One discriminator, named `type`.** Payloads briefly carried two — `$type`
from System.Text.Json's polymorphism and `type` from ADR-0021 — agreeing only
because both derived from the same closed set. Nothing enforced it; a derived
type registered with a mismatched property would have made them disagree
silently, and the failure would have surfaced in whichever renderer read the
other key. The serializer's discriminator is now named `type`, so it is one
field the serializer enforces. `PayloadRenderer` switches on it.

**snake_case throughout, envelope included.** The envelope was camelCase
(`turnId`, `amendmentId`, `tokensUsed`) while schema-backed bodies were
snake_case, so a client destructuring one object needed two conventions with
an invisible boundary between them. All eight schemas in
`contracts/schemas/` are snake_case, so the envelope was not one of two
defensible choices — it was the only thing in the repo not following the
published one, and it got there by inheriting the SDK's default handling of
C# records rather than by anyone deciding. The envelope moved; the
schema-backed shapes did not.

The general rule, for the next seven: **the published contract wins.** Where
a shape has a schema, the schema's names are the wire's names, and explicit
`[JsonPropertyName]` keeps them fixed against any serializer policy.

---

## 10. Enforcement

Four gates, three of which have been shown to fail on purpose. A check that
has never failed is not a check.

| Command | What it does |
|---|---|
| `pnpm check:tokens` | Fails on any raw colour in `packages/ui/src`, `dashboard/app`, `dashboard/lib` |
| `pnpm check:contrast` | 162 token pairs against WCAG AA in both themes |
| `pnpm test:visual` | The showcase, captured per section and full-page, in both themes |
| `pnpm check` | tokens, contrast, lint, typecheck — everything but the visual suite |

**Tokens-only** (`packages/ui/scripts/check-tokens-only.mjs`) is the second
net behind the compiler. `--color-*: initial` already makes `bg-gray-800`
produce no CSS, but a class that compiles to nothing is *invisible*, not
loud: the build stays green and the element renders transparent, which reads
as a layout bug rather than a policy violation. The script also catches hex,
`rgb()` and `hsl()` literals, which never went through Tailwind at all so the
palette reset has no opinion about them. It skips comments — this document's
own rule names `bg-gray-800` more than once, and a checker that cannot read
its own explanation is a checker people disable. Verified against a planted
violation of each of the four kinds.

**The visual suite** captures each section separately as well as the full
page, so a diff points at what changed rather than at a 20,000-pixel image.
Two things about it are deliberate:

- *An absolute pixel budget, not a ratio.* A ratio scales tolerance with the
  image, so a tall section absorbs changes a short one would catch. This is
  not hypothetical: at `maxDiffPixelRatio: 0.002` an accent hue change failed
  three captures and slipped past the 22,000-pixel domain section entirely.
  `maxDiffPixels: 200` catches it, and repeated runs on unchanged code differ
  by zero.
- *Production build, not dev.* Dev-mode rendering differs enough to make a
  baseline captured in one useless against the other, so the config builds
  and serves the standalone output.

The suite also asserts no console or page errors. That earned its place
immediately: it caught a React hydration mismatch (#418) from `at()`
formatting timestamps with `toLocaleString`, whose output depends on the
runtime timezone — so the server and the browser disagreed and React
discarded the server's markup. Timestamps are now formatted in UTC
explicitly, which also makes the screenshots reproducible on a machine in
another zone. Showing a viewer's local time is a per-viewer preference and
belongs in the app, applied after hydration — not in a formatter the server
also calls.

Note what none of these cover: anything that only happens at boot. The
factory side learned this the hard way — 189 green tests and a host that
would not start — and `scripts/check-stack.sh` is the answer there. The
design system's equivalent is that the visual suite runs a real production
build and serves it, rather than rendering components in isolation.

---

## 11. Still to come (step 3)

The export for Claude Design: `DESIGN.md`, `tokens.css`, and a
`design-system.json` listing components, props and states, plus the two
showcase screenshots, under `packages/ui/export/`.

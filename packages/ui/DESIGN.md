# Dark Factory — Design system

The shared visual language for the web app, and the source of truth handed
to Claude Design for prototyping. Everything visual lives in this package;
`dashboard/` imports from `@dark-factory/ui` and wires components to data.

Decisions recorded in [ADR-0033](../../docs/adr/0033-design-system.md).

**Status:** step 1 of 3 is complete — tokens, themes, typography, the shadcn
base set, the showcase route, and this document. The domain components
(`StageTimeline`, `SpecDiff`, `PayloadRenderer`, …) are step 2 and are
specified at the end of this file so the shape they will take is reviewable
now.

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

### Domain primitives (shipped in step 1)

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

## 8. Still to come (step 2)

Domain components, each in every listed state on the showcase. Typed against
`contracts/schemas/` where a shape exists (`spec_diff` today); where it does
not, the TypeScript type is defined in `src/types` and **proposed as a
schema** here, per ADR-0021's rule that the vocabulary grows deliberately.

| Component | Renders | Schema |
|---|---|---|
| `StageTimeline` | plan → implement → verify → ship for one run, with owning member, artifact chip, cost; a parked stage shows the agent's question inline | proposed: `stage_timeline` |
| `SpecNodeCard` | one small-grained node: id, layer badge, text, edge counts | from `spec.schema.json` + ADR-0016 |
| `SpecDiff` | created / revised / retired nodes, edge changes, conflicts | `specdiff.schema.json` (exists) |
| `ApprovalCard` | summary, required approvers, approve/reject with reason | proposed: `approval_card` |
| `AmendmentRow` | backlog item, draggable into a batch | ADR-0029 |
| `BatchCard` | ordered runs, overall status, deploy control | ADR-0029 |
| `TeamMemberCard` | portrait, role, model family, speed preset, skills, budget, spend | ADR-0028 |
| `PersonaCard` | marketplace variant with price and author | ADR-0028 (deferred persona note) |
| `ServerCard` | name, domain, convention version, capabilities, effective config | `describe.schema.json` (exists) |
| `MetricTile` | one number with label, delta, sparkline | proposed: `metric` |
| `CostBar` | stacked cost by stage or member | ADR-0032 `model_calls` |
| `CodeDiff` | unified diff with spec-id gutter annotations | proposed: `code_diff` |
| `DependencyGraph` | small SVG graph, force-free layout | proposed: `dependency_graph` |
| `PayloadRenderer` | maps an ADR-0021 payload to the above | ADR-0021 |
| `SyncStatus` | synced / syncing / offline / conflict | ADR-0031 |
| `ProvenancePopover` | conversation, turn, proposer, approver, skill revisions, model | ADR-0016 provenance |

Also in step 2: the Playwright screenshot test capturing the showcase in both
themes, and the tokens-only lint rule as a second net behind the compiler.

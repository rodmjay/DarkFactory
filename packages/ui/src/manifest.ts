/**
 * The machine-readable description of this design system.
 *
 * This is the source for `export/design-system.json`, which goes to Claude
 * Design alongside DESIGN.md and tokens.css. It is hand-written prose about
 * hand-written components, and the risk with any such file is that it
 * quietly stops describing the thing it claims to.
 *
 * Two things keep it honest rather than hoping:
 *
 * - `component` is typed as a key of the package's own exports, so a
 *   component that is renamed or deleted fails `tsc` here.
 * - `scripts/build-export.mjs` fails if a component *file* has no entry
 *   naming one of its exports, so a component that is added fails too.
 *
 * Neither catches a stale `states` list. That one is caught by the showcase:
 * every state named here has a labelled frame on `/design`, and the visual
 * suite captures it.
 */

import type * as ui from "./index";

/** Anything the package exports. Renaming a component breaks this file. */
type ComponentName = keyof typeof ui;

export interface PropSpec {
  name: string;
  type: string;
  required?: boolean;
  /** Why it exists, where that is not obvious from the name. */
  note?: string;
}

export interface ComponentSpec {
  component: ComponentName;
  group: "base" | "domain";
  summary: string;
  props: PropSpec[];
  states: string[];
  /** The schema or ADR the shape comes from. */
  shape?: string;
  /** A decision a designer should not silently undo. */
  constraint?: string;
}

export interface TokenFamilySpec {
  family: string;
  variants: string[];
  members: string[];
  note: string;
}

export interface DesignSystemManifest {
  name: string;
  adr: string;
  themes: string[];
  /** The three recorded choices, so they travel with the file. */
  choices: { display: string; mono: string; neutral: string };
  typeScale: Record<string, string>;
  radii: Record<string, string>;
  motion: Record<string, string>;
  tokenFamilies: TokenFamilySpec[];
  components: ComponentSpec[];
  rules: string[];
}

const STATUS = ["pending", "running", "passed", "parked", "failed", "over-budget"];
const LAYERS = ["product", "api", "data", "infra", "ui", "other"];

export const manifest: DesignSystemManifest = {
  name: "@dark-factory/ui",
  adr: "docs/adr/0033-design-system.md",
  themes: ["dark (primary)", "light"],

  choices: {
    display:
      "Instrument Sans, for display and body. Narrow enough to keep a dense table dense without tightening tracking. Not Inter, which is invisible in the way that makes a product look like a template. One family, not a display pairing: headings here are wayfinding, not voice.",
    mono: "IBM Plex Mono. Mono is a second body face in this product, not an accent — ULIDs, hashes, df.* names, diffs and costs are on every screen. Humanist rather than mechanical, narrow enough for a 26-character id in a table cell, slashed zero.",
    neutral:
      "Warm, OKLCH hue 70 at chroma 0.004–0.010, never zero. A pure-gray interface reads as unthemed; a warm ramp reads as ink on paper and lets the cool iris accent register as a signal rather than one more colour.",
  },

  typeScale: {
    "2xs": "11px — mono metadata",
    xs: "12px",
    sm: "13px — the working size; this is a console, read leaning in",
    base: "14px",
    md: "16px",
    lg: "18px",
    xl: "22px",
    "2xl": "28px",
    "3xl": "36px",
  },

  radii: { control: "6px", card: "10px", pill: "9999px" },

  motion: {
    fast: "120ms — state: hover, press, check, colour",
    slow: "260ms — layout: a sheet arriving, a section opening",
    ease: "cubic-bezier(0.22, 1, 0.36, 1)",
    reducedMotion: "prefers-reduced-motion collapses both to 1ms globally, in tokens.css",
  },

  tokenFamilies: [
    {
      family: "surface",
      variants: ["page", "card", "raised", "overlay", "sunken", "scrim"],
      members: ["--surface-*", "--scrim"],
      note: "Elevation is lightness, not shadow. Dark climbs the ramp; light descends it. Two shadows exist and in dark they are nearly invisible.",
    },
    {
      family: "border",
      variants: ["border", "border-strong", "border-control"],
      members: ["--border", "--border-strong", "--border-control"],
      note: "border-control is separate because a form control's boundary is the only thing identifying it, so it carries a real 3:1 obligation the hairlines do not.",
    },
    {
      family: "text",
      variants: ["primary", "secondary", "muted", "inverse"],
      members: ["--text-*"],
      note: "Every combination with every surface is checked at 4.5:1 in both themes.",
    },
    {
      family: "accent",
      variants: ["accent", "hover", "text", "fill", "border", "fg", "ring"],
      members: ["--accent-*"],
      note: "Iris. Reserved for actions the user must take — approve, answer a parked agent, deploy. Never 'the primary button colour'. The moment it means 'clickable', the interface loses its one way to say 'you, specifically, right now'.",
    },
    {
      family: "status",
      variants: ["fill", "border", "text"],
      members: STATUS.map((s) => `--status-${s}-*`),
      note: "pending and running are quiet, because that is what a timeline mostly is. parked and over-budget are in the accent's hue family because they are the two that mean a person is required. failed is red and deliberately nothing like parked.",
    },
    {
      family: "layer",
      variants: ["fill", "border", "text"],
      members: LAYERS.map((l) => `--layer-${l}-*`),
      note: "Five hues at roughly a third of the status chroma, so a row reads as a legend and not as five alerts. `layer` is an open string in specdiff.schema.json — customers declare their own — so anything unrecognised falls to `other` rather than being dropped or given a colour nobody chose.",
    },
    {
      family: "diff",
      variants: ["fill", "border", "text", "marker"],
      members: ["added", "removed", "changed", "conflict"].map((d) => `--diff-${d}-*`),
      note: "conflict is the loudest thing the system draws and the only place maximum chroma is spent. 'This contradicts the rule you set in March' is the most valuable sentence the product says.",
    },
    {
      family: "server",
      variants: ["fill", "border", "text"],
      members: ["conformant", "degraded", "unreachable"].map((s) => `--server-${s}-*`),
      note: "unreachable is neutral, not red. A server the factory cannot reach is usually a network fact, and colouring it red trains people to ignore red.",
    },
  ],

  components: [
    // ---- base ----
    {
      component: "Button",
      group: "base",
      summary: "The everyday action. Neutral by default.",
      props: [
        {
          name: "variant",
          type: "'default' | 'needs-you' | 'outline' | 'ghost' | 'danger' | 'link'",
          note: "needs-you is the only variant allowed to wear the accent.",
        },
        { name: "size", type: "'sm' | 'md' | 'lg' | 'icon'" },
        { name: "asChild", type: "boolean" },
      ],
      states: ["default", "hover", "focus-visible", "disabled", "with icon", "icon-only"],
      constraint:
        "Do not add a 'primary' variant. The everyday button is neutral and needs-you is not a synonym for prominent.",
    },
    {
      component: "Badge",
      group: "base",
      summary: "A small label.",
      props: [
        { name: "variant", type: "'default' | 'outline' | 'accent' | 'solid'" },
        { name: "asChild", type: "boolean" },
      ],
      states: ["default", "outline", "accent", "solid", "with icon"],
    },
    {
      component: "Card",
      group: "base",
      summary: "A container. Composed with CardHeader, CardTitle, CardDescription, CardAction, CardContent, CardFooter.",
      props: [],
      states: ["populated", "skeleton"],
    },
    {
      component: "Dialog",
      group: "base",
      summary: "A modal, on Radix Dialog.",
      props: [{ name: "showCloseButton", type: "boolean", note: "On DialogContent." }],
      states: ["closed", "open"],
    },
    {
      component: "Sheet",
      group: "base",
      summary: "A side panel, for when the context behind it should stay visible.",
      props: [{ name: "side", type: "'right' | 'left' | 'bottom'" }],
      states: ["closed", "open"],
    },
    {
      component: "Tabs",
      group: "base",
      summary: "Underline, not pill.",
      props: [],
      states: ["active", "inactive", "disabled"],
      constraint:
        "Underlined rather than filled: tabs here sit above dense content and often several sets deep, and a pill row competes with the data.",
    },
    {
      component: "Table",
      group: "base",
      summary: "Composed with TableHeader, TableBody, TableFooter, TableRow, TableHead, TableCell, TableCaption.",
      props: [
        {
          name: "numeric",
          type: "boolean",
          note: "On TableHead and TableCell. Right-aligns and switches on tabular figures together — a right-aligned column of proportional digits still jitters, and Instrument Sans' digit advances vary by 31%.",
        },
      ],
      states: ["header", "body", "selected row", "hover", "footer total"],
    },
    { component: "Tooltip", group: "base", summary: "On Radix Tooltip, with TooltipProvider.", props: [], states: ["closed", "open"] },
    { component: "Popover", group: "base", summary: "On Radix Popover.", props: [{ name: "align", type: "'start' | 'center' | 'end'" }, { name: "sideOffset", type: "number" }], states: ["closed", "open"] },
    {
      component: "DropdownMenu",
      group: "base",
      summary: "Items, checkbox items, radio items, labels, separators, shortcuts, submenus.",
      props: [{ name: "variant", type: "'default' | 'danger'", note: "On DropdownMenuItem." }],
      states: ["closed", "open", "highlighted", "disabled"],
    },
    {
      component: "Command",
      group: "base",
      summary: "The command palette, on cmdk. CommandDialog wraps it for ⌘K.",
      props: [],
      states: ["inline", "in dialog", "empty", "disabled item", "with shortcut"],
      constraint:
        "This is the product's real navigation — everything in the factory is addressable and there are too many for a sidebar. Keep it quiet; it is for people who know what they are looking for.",
    },
    { component: "Input", group: "base", summary: "Text input.", props: [], states: ["default", "placeholder", "filled", "aria-invalid", "disabled"] },
    { component: "Textarea", group: "base", summary: "Multi-line input, field-sizing-content.", props: [], states: ["default", "filled", "disabled"] },
    { component: "Select", group: "base", summary: "On Radix Select.", props: [{ name: "size", type: "'sm' | 'md'", note: "On SelectTrigger." }], states: ["value", "placeholder", "disabled", "open"] },
    { component: "Switch", group: "base", summary: "On Radix Switch.", props: [], states: ["on", "off", "disabled on", "disabled off"] },
    {
      component: "Toast",
      group: "base",
      summary: "On Radix Toast, with ToastProvider and ToastViewport.",
      props: [{ name: "variant", type: "'default' | 'needs-you' | 'danger'" }],
      states: ["default", "needs-you", "danger", "with action"],
      constraint:
        "Built on Radix rather than a toast library so every variant can be rendered statically — an imperative toast() has no state a screenshot can catch.",
    },
    { component: "Skeleton", group: "base", summary: "Loading placeholder.", props: [], states: ["text line", "avatar", "button"] },
    { component: "ScrollArea", group: "base", summary: "On Radix ScrollArea.", props: [{ name: "orientation", type: "'vertical' | 'horizontal'", note: "On ScrollBar." }], states: ["scrolling"] },
    { component: "Separator", group: "base", summary: "A rule.", props: [{ name: "orientation", type: "'horizontal' | 'vertical'" }], states: ["horizontal", "vertical"] },
    { component: "Avatar", group: "base", summary: "Composed with AvatarImage and AvatarFallback.", props: [], states: ["image", "fallback", "sizes"] },
    { component: "Label", group: "base", summary: "A form label.", props: [], states: ["default"] },

    // ---- domain primitives ----
    {
      component: "StatusChip",
      group: "domain",
      summary: "The one renderer for a stage status.",
      props: [
        { name: "status", type: "StageStatus", required: true },
        { name: "compact", type: "boolean", note: "Dot only, for dense rows. Label goes to a screen reader." },
      ],
      states: STATUS.concat(["compact"]),
      shape: "ADR-0003 as amended, ADR-0029",
      constraint: "running breathes. It is the only motion in a timeline, which is what makes it readable at a glance in a list of forty.",
    },
    {
      component: "LayerBadge",
      group: "domain",
      summary: "A spec layer.",
      props: [{ name: "layer", type: "string", required: true, note: "Open string; unrecognised values render as `other`." }],
      states: LAYERS,
      shape: "contracts/schemas/specdiff.schema.json",
    },
    {
      component: "Rationale",
      group: "domain",
      summary: "Why a change was made. Three states, three renderings.",
      props: [
        { name: "rationale", type: "string | null | undefined", note: "Absent, empty, or a reason — all three render differently." },
        { name: "hideWhenAbsent", type: "boolean" },
      ],
      states: ["given", "absent — 'No reason given.'", "blank — 'Reason left blank.'"],
      shape: "contracts/schemas/specdiff.schema.json; ADR-0016 as amended",
      constraint:
        "Absent and empty must not collapse. Serialization drops nulls, so no reason arrives as a missing key; an empty string means somebody was asked and left it blank, which is the more actionable of the two. Never truncated — the reasons the architect writes run 150–250 characters and the second sentence is usually the half worth reading.",
    },
    {
      component: "SpecId",
      group: "domain",
      summary: "A ULID, truncated to the part that distinguishes it.",
      props: [
        { name: "id", type: "string", required: true },
        { name: "truncate", type: "boolean" },
      ],
      states: ["truncated", "full"],
    },

    // ---- domain ----
    {
      component: "StageTimeline",
      group: "domain",
      summary: "plan → implement → verify → ship for one run, with owning member, artifact chip and cost.",
      props: [
        { name: "run_id", type: "string", required: true },
        { name: "snapshot_id", type: "string" },
        { name: "stages", type: "StageTimelineEntry[]", required: true },
        { name: "onAnswer", type: "(stage: StageTimelineEntry) => void", note: "Shown on a parked stage." },
      ],
      states: ["running", "parked", "failed", "over-budget", "completed"],
      shape: "proposed `stage_timeline` — a join across stages, artifacts and model_calls that has no schema because no single table is its home",
      constraint:
        "A parked stage opens with the agent's question inline. Putting it behind a click makes the one state that needs a person the one that costs an extra step. Ship appears but is a batch action (ADR-0029) — a run that verifies clean is deployable, not deployed.",
    },
    {
      component: "SpecNodeCard",
      group: "domain",
      summary: "One small-grained spec node: id, layer badge, text, edge counts.",
      props: [
        { name: "node", type: "SpecNode", required: true, note: "`revisions` renders the history from df.specs.get, oldest first." },
        { name: "selected", type: "boolean" },
      ],
      states: ["default", "selected", "retired", "drifted", "with revision history"],
      shape: "ADR-0016, ADR-0024; df.specs.get returns { node, revisions }",
      constraint:
        "The text is the subject and gets the size; id, layer and edge counts are apparatus. That ordering is the argument for small grain — a node you can read in one line is a node you can find a contradiction in.",
    },
    {
      component: "SpecDiff",
      group: "domain",
      summary: "An amendment as a diff against the graph: created, revised and retired nodes, edge changes, conflicts.",
      props: [
        { name: "diff", type: "SpecDiffDocument", required: true },
        { name: "conflicts", type: "SpecConflict[]", note: "Proposed; absent means none detected, not none." },
        { name: "summary", type: "string" },
      ],
      states: ["with conflicts", "without conflicts", "empty"],
      shape: "contracts/schemas/specdiff.schema.json; `conflicts` proposed",
      constraint:
        "Conflicts come first and are the loudest thing drawn. A reader scanning for what is new scrolls past a conflict placed under the additions.",
    },
    {
      component: "ApprovalCard",
      group: "domain",
      summary: "Summary, required approvers, approve/reject with reason.",
      props: [
        { name: "approval", type: "Approval", required: true },
        { name: "canDecide", type: "boolean" },
        { name: "onApprove", type: "(reason: string) => void" },
        { name: "onReject", type: "(reason: string) => void" },
      ],
      states: ["awaiting", "approved", "rejected", "waiting-on-others"],
      shape: "proposed `approval_card`; ADR-0017",
      constraint:
        "Rejection requires a reason and approval does not. A rejection that says only 'no' sends the proposer back to guess; approval needs none because the diff already says what was agreed to.",
    },
    {
      component: "DecisionCard",
      group: "domain",
      summary: "Something a person has to decide: the question, why now, 2–4 options with their consequences, and ways to answer in one's own words or leave it open.",
      props: [
        { name: "decision", type: "Decision", required: true, note: "The `decision` payload, with or without its `type`." },
        {
          name: "state",
          type: "{ status: 'open' } | { status: 'answered'; choice: string; by?: string } | { status: 'deferred'; reason: string }",
          note: "`choice` is an option id, or the person's own words when it matches none. Defaults to open.",
        },
        { name: "busy", type: "boolean", note: "An answer is in flight; everything is disabled." },
        { name: "onChoose", type: "(optionId: string) => void" },
        { name: "onOther", type: "(text: string) => void" },
        { name: "onDefer", type: "(reason: string) => void" },
        { name: "defaultMode", type: "'choose' | 'other' | 'defer'", note: "Which way of answering is open on first render." },
      ],
      states: [
        "open with a recommended option",
        "open without own-words or leave-open",
        "answering in own words",
        "leaving open",
        "answered",
        "deferred",
      ],
      shape: "contracts/schemas/decision.schema.json; ADR-0041",
      constraint:
        "A recommended option is marked, never preselected — it is the proposer's view and choosing it is still the person's decision. Leaving a decision open requires a reason. With no callbacks the card is read-only and keeps full contrast: a consequence dimmed to half opacity is one nobody reads.",
    },
    {
      component: "AmendmentRow",
      group: "domain",
      summary: "A backlog item, draggable into a batch.",
      props: [
        { name: "amendment", type: "Amendment", required: true },
        { name: "inBatch", type: "boolean" },
        { name: "seq", type: "number" },
      ],
      states: ["default", "in-batch", "blocked-by-dependency"],
      shape: "ADR-0029",
      constraint:
        "The drag handle is always visible. Ordering is the interaction on this screen, and a control you have to hover to find reads as absent on a list of forty.",
    },
    {
      component: "BatchCard",
      group: "domain",
      summary: "Ordered runs, overall status, deploy control.",
      props: [
        { name: "batch", type: "Batch", required: true },
        { name: "onDeploy", type: "(batch: Batch) => void" },
      ],
      states: ["composing", "running", "blocked-by-verify", "deployable", "deployed"],
      shape: "ADR-0029",
      constraint:
        "Deploy lives here and nowhere on a run. Deploy is a batch action; a button on a run would mean the schema was lying about what a release is.",
    },
    {
      component: "TeamMemberCard",
      group: "domain",
      summary: "Portrait, role, model family, speed preset, skills, budget, spend.",
      props: [{ name: "member", type: "TeamMember", required: true }],
      states: ["native agent", "agent server", "persona (priced)", "over-budget"],
      shape: "ADR-0028",
      constraint:
        "Native agents and agent servers share a seat in the assignment map, so they share a card — the kind is a badge, not a different layout. Skill revisions are shown pinned, because a run is explained by the instructions it had and not the ones the library holds today.",
    },
    {
      component: "PersonaCard",
      group: "domain",
      summary: "The marketplace face of a team member, with price and author.",
      props: [
        { name: "persona", type: "Persona", required: true },
        { name: "modelFamily", type: "string", required: true, note: "Required by ADR-0028: a price without a model is unreadable." },
        { name: "speed", type: "SpeedPreset" },
        { name: "role", type: "string" },
        { name: "onTeam", type: "boolean", note: "Assigned to this project's team, not merely owned." },
        { name: "onInstall", type: "(persona: Persona) => void" },
      ],
      states: ["free", "priced", "installed", "on this team"],
      shape: "ADR-0028 (personas deferred)",
      constraint:
        "Free and priced are the same layout with a different figure. Making the paid variant louder turns a roster into a storefront, and the community tier is a first-class plugin surface. `installed` and `on this team` stay separate: a persona can be bought for the org and used by nobody, and collapsing them makes \"why is this not running my work\" unanswerable from the card.",
    },
    {
      component: "ServerCard",
      group: "domain",
      summary: "Name, domain, convention version, capabilities, effective config.",
      props: [
        { name: "server", type: "Server", required: true },
        { name: "onAuthorize", type: "(server: Server) => void" },
      ],
      states: ["conformant", "degraded", "unreachable", "built-in-connector (not authorized)"],
      shape: "contracts/schemas/describe.schema.json; ADR-0018, ADR-0019",
      constraint:
        "Capabilities carry their conformance result — a server that claims df.vcs.open_pr and one that has demonstrated it are different things. `degraded` names the failing capability: that is the difference between a status and a diagnosis. A built-in connector that has not been authorized shows `not authorized` rather than a health, because nothing has contacted it and `conformant` would assert a check that never ran. effective_config is rendered, which is why the schema forbids secrets in it.",
    },
    {
      component: "MetricTile",
      group: "domain",
      summary: "One number with label, delta and sparkline.",
      props: [
        { name: "label", type: "string", required: true },
        { name: "value", type: "number | string", required: true },
        { name: "unit", type: "string" },
        { name: "delta", type: "number | null" },
        { name: "delta_is_good", type: "boolean", note: "Without it the delta stays neutral rather than guessing." },
        { name: "series", type: "number[]" },
        { name: "period", type: "string" },
      ],
      states: ["up", "down", "flat", "no-data"],
      shape: "proposed `metric`",
      constraint:
        "The delta is coloured by whether it is good, not by its sign — cost rising and throughput rising are both 'up'. no-data renders an em dash, because a tile showing 0 when it means 'nothing reported' teaches people to distrust the ones that mean zero.",
    },
    {
      component: "CostBar",
      group: "domain",
      summary: "Stacked cost by stage or member.",
      props: [
        { name: "segments", type: "CostSegment[]", required: true },
        { name: "budget", type: "number | null", note: "When present, the bar scales to the budget rather than to the total." },
        { name: "compact", type: "boolean" },
      ],
      states: ["default", "over-budget segment"],
      shape: "ADR-0032 model_calls",
      constraint:
        "Segments are sized by token share, not by dollars — the two disagree, because cached input is billed at a fraction of fresh input. With a budget the bar must scale to the budget: scaling to the total while printing '1,132 of 200,000' beside a full bar says the budget is spent when 0.5% of it is.",
    },
    {
      component: "CodeDiff",
      group: "domain",
      summary: "A unified diff with spec ids in the gutter.",
      props: [
        { name: "patch", type: "string", required: true, note: "The factory generates it; a model never authors one." },
        { name: "specAnnotations", type: "Record<number, string>", note: "Keyed by line number in the new file, so it survives re-rendering with more context." },
        { name: "filesChanged", type: "string[]" },
      ],
      states: ["default with spec gutter", "with conflict marker"],
      shape: "proposed `code_diff`; ADR-0024",
      constraint:
        "Line numbers come from the hunk headers, never from the row index. Counting rows gives numbers that look authoritative and are wrong by whatever the hunk skipped, and a reader will quote them. Removed lines get no number.",
    },
    {
      component: "DependencyGraph",
      group: "domain",
      summary: "A small SVG graph of spec nodes and typed edges.",
      props: [
        { name: "nodes", type: "GraphNode[]", required: true },
        { name: "edges", type: "GraphEdge[]", required: true },
        { name: "focus", type: "string" },
        { name: "onSelect", type: "(node: GraphNode) => void" },
      ],
      states: ["focused neighbourhood"],
      shape: "proposed `dependency_graph`; ADR-0016",
      constraint:
        "Force-free. A force simulation re-lays-out every render, so the same neighbourhood looks different each time and nothing about it is memorable. Edges carry their kind as a label because ADR-0016's edges are typed and an unlabelled line loses the only thing distinguishing depends_on from conflicts_with.",
    },
    {
      component: "PayloadRenderer",
      group: "domain",
      summary: "Maps an ADR-0021 payload to the component that draws it.",
      props: [{ name: "payloads", type: "Payload[]", required: true }],
      states: [
        "markdown",
        "spec_diff",
        "dependency_graph",
        "stage_timeline",
        "approval_card",
        "table",
        "code_diff",
        "form",
        "metric",
        "decision",
        "a reply carrying three payloads",
        "unknown type",
      ],
      shape: "ADR-0021",
      constraint:
        "No escape hatch — no raw-HTML payload, no 'custom' type. The moment one exists every plugin uses it and the vocabulary stops meaning anything. An unknown type renders as a named gap, because a payload silently dropped is indistinguishable from an agent that said nothing.",
    },
    {
      component: "SyncStatus",
      group: "domain",
      summary: "The local-first affordance.",
      props: [
        { name: "state", type: "SyncState", required: true },
        { name: "pending", type: "number" },
        { name: "compact", type: "boolean" },
      ],
      states: ["synced", "syncing", "offline", "conflict"],
      shape: "ADR-0031",
      constraint:
        "offline is neutral, not a warning. Local-first makes working offline a supported mode, and colouring it as a fault spends the same signal conflict has to use.",
    },
    {
      component: "ProvenancePopover",
      group: "domain",
      summary: "Why a thing exists: conversation, turn, proposer, approver, skill revisions, model.",
      props: [
        { name: "provenance", type: "Provenance", required: true },
        { name: "children", type: "ReactNode", note: "The thing being explained. Defaults to a small info button." },
        { name: "align", type: "'start' | 'center' | 'end'" },
        { name: "defaultOpen", type: "boolean" },
      ],
      states: ["on a spec node", "on a cost figure"],
      shape: "ADR-0016 provenance; ADR-0028 skill revisions",
      constraint:
        "One component for both, because 'why does this rule exist' and 'why did this cost that' have the same answer shape. Splitting them would be two half-answers to one question.",
    },
  ],

  rules: [
    // Deliberately not written as a literal Tailwind class: this file is
    // scanned by check-tokens-only.mjs, which cannot tell prose in a data
    // string from a class name in a component, and it should not have to —
    // the alternative is a per-file exemption, which is how a policy checker
    // stops being one.
    "Colour is tokens only. tokens.css declares `--color-*: initial`, which deletes Tailwind's default palette, so a class of the `bg-<colour>-<step>` form does not exist at all — a raw colour renders as nothing rather than as something wrong.",
    "One accent, spent only on 'needs you': approve an amendment, answer a parked agent, deploy a batch, and the parked / over-budget statuses. Never the generic 'primary'.",
    "Contrast is a test. 162 token pairs are asserted against WCAG AA in both themes by parsing tokens.css, so there is no second copy of the palette to drift.",
    "One focus ring, spread by every interactive component: 2px --accent-ring at 2px offset, on :focus-visible only.",
    "Motion has two speeds — 120ms for state, 260ms for layout — and prefers-reduced-motion collapses both globally.",
    "The package holds no data fetching, no routing and no app state. Components take props; the app wires them.",
    "Class names are written out, never interpolated: Tailwind scans source text, so `bg-status-${x}-fill` is a class that is never generated.",
    "Dark is the primary theme; light is equally finished, and the visual suite captures both.",
  ],
};

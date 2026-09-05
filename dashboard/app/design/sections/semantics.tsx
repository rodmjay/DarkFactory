import {
  DIFF_KINDS,
  LayerBadge,
  SPEC_LAYERS,
  SERVER_HEALTHS,
  STAGE_STATUSES,
  SpecId,
  StatusChip,
  diffBand,
  diffMarker,
  serverChip,
} from "@dark-factory/ui";

import { Block, Frame, Grid, Row, Section } from "../parts";

/* Every one of these families is keyed by a string that arrives from the
 * database, so the swatch lists are driven by the same exported constants
 * the components use. A token added to the system appears here without
 * anyone remembering to add it. */

const STATUS_SWATCHES: [string, string, string, string][] = [
  ["pending", "bg-status-pending-fill", "bg-status-pending-border", "text-status-pending-text"],
  ["running", "bg-status-running-fill", "bg-status-running-border", "text-status-running-text"],
  ["passed", "bg-status-passed-fill", "bg-status-passed-border", "text-status-passed-text"],
  ["parked", "bg-status-parked-fill", "bg-status-parked-border", "text-status-parked-text"],
  ["failed", "bg-status-failed-fill", "bg-status-failed-border", "text-status-failed-text"],
  [
    "over-budget",
    "bg-status-over-budget-fill",
    "bg-status-over-budget-border",
    "text-status-over-budget-text",
  ],
];

const LAYER_SWATCHES: [string, string, string, string][] = [
  ["product", "bg-layer-product-fill", "bg-layer-product-border", "text-layer-product-text"],
  ["api", "bg-layer-api-fill", "bg-layer-api-border", "text-layer-api-text"],
  ["data", "bg-layer-data-fill", "bg-layer-data-border", "text-layer-data-text"],
  ["infra", "bg-layer-infra-fill", "bg-layer-infra-border", "text-layer-infra-text"],
  ["ui", "bg-layer-ui-fill", "bg-layer-ui-border", "text-layer-ui-text"],
  ["other", "bg-layer-other-fill", "bg-layer-other-border", "text-layer-other-text"],
];

const DIFF_SWATCHES: [string, string, string, string, string][] = [
  [
    "added",
    "bg-diff-added-fill",
    "bg-diff-added-border",
    "text-diff-added-text",
    "bg-diff-added-marker",
  ],
  [
    "removed",
    "bg-diff-removed-fill",
    "bg-diff-removed-border",
    "text-diff-removed-text",
    "bg-diff-removed-marker",
  ],
  [
    "changed",
    "bg-diff-changed-fill",
    "bg-diff-changed-border",
    "text-diff-changed-text",
    "bg-diff-changed-marker",
  ],
  [
    "conflict",
    "bg-diff-conflict-fill",
    "bg-diff-conflict-border",
    "text-diff-conflict-text",
    "bg-diff-conflict-marker",
  ],
];

const SERVER_SWATCHES: [string, string, string, string][] = [
  [
    "conformant",
    "bg-server-conformant-fill",
    "bg-server-conformant-border",
    "text-server-conformant-text",
  ],
  [
    "degraded",
    "bg-server-degraded-fill",
    "bg-server-degraded-border",
    "text-server-degraded-text",
  ],
  [
    "unreachable",
    "bg-server-unreachable-fill",
    "bg-server-unreachable-border",
    "text-server-unreachable-text",
  ],
];

export function Semantics() {
  return (
    <Section
      id="semantics"
      title="Semantic colour"
      note="Four families keyed by values that arrive from the database: stage status, spec layer, diff kind, server health. Every one has fill, border and text variants, and every text-on-fill pair is checked against WCAG AA by packages/ui/scripts/check-contrast.mjs in both themes."
    >
      <Block
        title="Stage status"
        note="pending and running are quiet — they are the states a timeline is in most of the time. parked and over-budget wear the accent's hue family because they are the two that mean a person is required. failed is red and deliberately nothing like parked: 'the machine broke' and 'the agent is waiting on you' are different days."
      >
        <Grid>
          {STATUS_SWATCHES.map(([name, fill, border, text]) => (
            <div key={name} className="flex flex-col gap-1.5">
              <div className={`flex h-14 items-center justify-center rounded-control ${fill}`}>
                <span className={`text-xs font-medium ${text}`}>{name}</span>
              </div>
              <div className="flex gap-1">
                <div className={`h-1.5 flex-1 rounded-pill ${border}`} />
              </div>
              <code className="font-mono text-2xs text-muted">--status-{name}-*</code>
            </div>
          ))}
        </Grid>
        <Frame label="StatusChip">
          <Row>
            {STAGE_STATUSES.map((s) => (
              <StatusChip key={s} status={s} />
            ))}
          </Row>
          <Row className="mt-3">
            {STAGE_STATUSES.map((s) => (
              <StatusChip key={s} status={s} compact />
            ))}
          </Row>
        </Frame>
      </Block>

      <Block
        title="Spec layers"
        note="Five hues at a fraction of the status chroma, so a row of layer badges reads as a legend and not as five competing alerts. `layer` is an open string in specdiff.schema.json — a customer's standards server declares its own — so anything unrecognised falls to `other` rather than being dropped or given a colour nobody chose."
      >
        <Grid>
          {LAYER_SWATCHES.map(([name, fill, border, text]) => (
            <div key={name} className="flex flex-col gap-1.5">
              <div className={`flex h-14 items-center justify-center rounded-control ${fill}`}>
                <span className={`font-mono text-xs ${text}`}>{name}</span>
              </div>
              <div className={`h-1.5 rounded-pill ${border}`} />
              <code className="font-mono text-2xs text-muted">--layer-{name}-*</code>
            </div>
          ))}
        </Grid>
        <Frame label="LayerBadge — including an unrecognised layer">
          <Row>
            {SPEC_LAYERS.map((l) => (
              <LayerBadge key={l} layer={l} />
            ))}
            <LayerBadge layer="billing" />
          </Row>
        </Frame>
      </Block>

      <Block
        title="Diff"
        note="conflict is the loudest thing the system can draw, and it is the only place we spend maximum chroma. 'This contradicts the rule you set in March' is the single most valuable sentence the product says; it does not get to be a subtle tint."
      >
        <Grid>
          {DIFF_SWATCHES.map(([name, fill, border, text, marker]) => (
            <div key={name} className="flex flex-col gap-1.5">
              <div className={`flex h-14 items-center justify-center rounded-control ${fill}`}>
                <span className={`font-mono text-xs ${text}`}>{name}</span>
              </div>
              <div className="flex gap-1">
                <div className={`h-1.5 flex-1 rounded-pill ${border}`} />
                <div className={`h-1.5 flex-1 rounded-pill ${marker}`} />
              </div>
              <code className="font-mono text-2xs text-muted">--diff-{name}-*</code>
            </div>
          ))}
        </Grid>
        <Frame label="diff bands, as SpecDiff and CodeDiff will use them">
          <div className="overflow-hidden rounded-control border border-border font-mono text-xs">
            {DIFF_KINDS.map((kind) => (
              <div
                key={kind}
                className={`flex items-start gap-3 border-b border-border px-3 py-1.5 last:border-b-0 ${diffBand[kind]}`}
              >
                <span className={`w-4 shrink-0 text-center font-semibold ${diffMarker[kind]}`}>
                  {kind === "added" ? "+" : kind === "removed" ? "−" : kind === "changed" ? "~" : "!"}
                </span>
                <span className="min-w-0">
                  {kind === "conflict"
                    ? "conflicts with 01JAV3K…PT6FE2 — “renewal is blocked if the account is delinquent”, decided 14 Mar"
                    : `a spec node was ${kind}`}
                </span>
              </div>
            ))}
          </div>
        </Frame>
      </Block>

      <Block
        title="Server health"
        note="unreachable is neutral on purpose. A server we cannot reach is usually a network fact rather than a fault, and colouring it red trains people to ignore red."
      >
        <Grid>
          {SERVER_SWATCHES.map(([name, fill, border, text]) => (
            <div key={name} className="flex flex-col gap-1.5">
              <div className={`flex h-14 items-center justify-center rounded-control ${fill}`}>
                <span className={`text-xs font-medium ${text}`}>{name}</span>
              </div>
              <div className={`h-1.5 rounded-pill ${border}`} />
              <code className="font-mono text-2xs text-muted">--server-{name}-*</code>
            </div>
          ))}
        </Grid>
        <Frame label="health chips">
          <Row>
            {SERVER_HEALTHS.map((h) => (
              <span
                key={h}
                className={`inline-flex items-center rounded-pill border px-2 py-0.5 text-2xs font-medium ${serverChip[h]}`}
              >
                {h}
              </span>
            ))}
          </Row>
        </Frame>
      </Block>

      <Block title="SpecId" note="A ULID is 26 characters and nobody reads all of them; the tail is the random part, so it is what distinguishes two ids.">
        <Frame>
          <Row>
            <SpecId id="01JAV3K8QW7Z9RXNB4MDPT6FE2" />
            <SpecId id="01JAV3K8QW7Z9RXNB4MDPT6FE2" truncate={false} />
          </Row>
        </Frame>
      </Block>
    </Section>
  );
}

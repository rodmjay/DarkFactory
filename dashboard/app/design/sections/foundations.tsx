import { Separator } from "@dark-factory/ui";

import { Block, Frame, Grid, Row, Section, Swatch } from "../parts";

/* Written out rather than interpolated: Tailwind scans source text, so a
 * class name assembled at runtime is a class name that never gets built. */
const NEUTRALS: [string, string][] = [
  ["--df-n-0", "bg-[var(--df-n-0)]"],
  ["--df-n-25", "bg-[var(--df-n-25)]"],
  ["--df-n-50", "bg-[var(--df-n-50)]"],
  ["--df-n-100", "bg-[var(--df-n-100)]"],
  ["--df-n-200", "bg-[var(--df-n-200)]"],
  ["--df-n-300", "bg-[var(--df-n-300)]"],
  ["--df-n-400", "bg-[var(--df-n-400)]"],
  ["--df-n-500", "bg-[var(--df-n-500)]"],
  ["--df-n-600", "bg-[var(--df-n-600)]"],
  ["--df-n-700", "bg-[var(--df-n-700)]"],
  ["--df-n-800", "bg-[var(--df-n-800)]"],
  ["--df-n-900", "bg-[var(--df-n-900)]"],
  ["--df-n-950", "bg-[var(--df-n-950)]"],
  ["--df-n-1000", "bg-[var(--df-n-1000)]"],
];

const TYPE_SCALE: { name: string; className: string }[] = [
  { name: "text-3xl", className: "text-3xl font-semibold" },
  { name: "text-2xl", className: "text-2xl font-semibold" },
  { name: "text-xl", className: "text-xl font-semibold" },
  { name: "text-lg", className: "text-lg font-semibold" },
  { name: "text-md", className: "text-md" },
  { name: "text-base", className: "text-base" },
  { name: "text-sm", className: "text-sm" },
  { name: "text-xs", className: "text-xs" },
  { name: "text-2xs", className: "text-2xs" },
];

export function Foundations() {
  return (
    <Section
      id="foundations"
      title="Foundations"
      note={
        <>
          Three recorded choices: <strong className="font-medium text-primary">Instrument Sans</strong>{" "}
          for display and body, <strong className="font-medium text-primary">IBM Plex Mono</strong>{" "}
          for code, spec ids and diffs, and a{" "}
          <strong className="font-medium text-primary">warm</strong> neutral ramp. The reasoning is
          in packages/ui/DESIGN.md.
        </>
      }
    >
      <Block
        title="Typeface — Instrument Sans"
        note="A grotesque with slightly narrow proportions, so a dense table stays dense without tightening the tracking until it hurts. Not Inter: the higher-contrast terminals and the single-storey g give headings a voice without a second family."
      >
        <Frame>
          <p className="text-2xl font-semibold tracking-tight">
            Ship at agent speed without losing the architecture.
          </p>
          <p className="mt-3 max-w-2xl text-base text-secondary">
            A conversation settles into an amendment. The amendment is reviewed, approved, and
            queued into an ordered batch. The batch runs plan, implement and verify until every
            item passes, and then deploys as one unit.
          </p>
          <Separator className="my-4" />
          <div className="flex flex-col gap-2">
            {TYPE_SCALE.map((t) => (
              <div key={t.name} className="flex items-baseline gap-4">
                <code className="w-20 shrink-0 font-mono text-2xs text-muted">{t.name}</code>
                <span className={t.className}>Specification graph</span>
              </div>
            ))}
          </div>
          <Separator className="my-4" />
          <Row className="gap-6">
            <span className="text-base font-normal">Regular 400</span>
            <span className="text-base font-medium">Medium 500</span>
            <span className="text-base font-semibold">Semibold 600</span>
            <span className="text-base font-bold">Bold 700</span>
          </Row>
        </Frame>
      </Block>

      <Block
        title="Monospace — IBM Plex Mono"
        note="Chosen for the volume of it: spec ids, hashes, diffs, deployment names and token counts are on almost every screen. Humanist rather than mechanical, so a page of it reads as prose-adjacent rather than as a terminal dump; slashed zero, and a 1 that cannot be mistaken for anything."
      >
        <Frame>
          <div className="flex flex-col gap-2 font-mono text-sm">
            <div>01JAV3K8QW7Z9RXNB4MDPT6FE2 &nbsp; 0123456789 &nbsp; O0 I1 l5 S8 B8 Z2</div>
            <div className="text-secondary">
              sha256:9f1c4a2e7b03d8156ea94c0b7f2d31e8ab5c609374fd2e18
            </div>
            <div className="text-secondary">df.specs.propose(conversation_id, diff)</div>
            <div className="tnum text-secondary">
              1,284,033 tokens &nbsp; $12.40 &nbsp; 4,096 in / 812 out &nbsp; 2,391 ms
            </div>
          </div>
        </Frame>
      </Block>

      <Block
        title="Neutral ramp — warm, hue 70"
        note="Low chroma, never zero. A pure-gray interface reads as unfinished; a warm ramp reads as ink on paper, and it is what lets the cool iris accent register as a signal instead of as one more colour."
      >
        <Grid cols="tight">
          {NEUTRALS.map(([name, className]) => (
            <Swatch key={name} name={name} height="h-10" className={className} />
          ))}
        </Grid>
      </Block>

      <Block title="Surfaces" note="Elevation is lightness, not shadow. Dark mode climbs the ramp; light mode descends it.">
        <Grid>
          <Swatch name="--surface-page" className="bg-page" />
          <Swatch name="--surface-card" className="bg-card" />
          <Swatch name="--surface-raised" className="bg-raised" />
          <Swatch name="--surface-overlay" className="bg-overlay" />
          <Swatch name="--surface-sunken" className="bg-sunken" />
          <Swatch name="--scrim" className="bg-scrim" />
        </Grid>
      </Block>

      <Block title="Borders">
        <Grid>
          <Swatch name="--border" className="bg-border" />
          <Swatch name="--border-strong" className="bg-border-strong" />
          <Swatch name="--border-control" className="bg-border-control" />
        </Grid>
      </Block>

      <Block title="Text">
        <Frame>
          <div className="flex flex-col gap-2 text-base">
            <div className="text-primary">--text-primary — the specification itself</div>
            <div className="text-secondary">--text-secondary — supporting prose and labels</div>
            <div className="text-muted">--text-muted — metadata, timestamps, counts</div>
            <div className="w-fit rounded-control bg-accent px-2 py-1 text-inverse">
              --text-inverse — on a filled surface
            </div>
          </div>
        </Frame>
      </Block>

      <Block
        title="Accent — iris"
        note="Reserved for actions the user must take: approve an amendment, answer a parked agent, deploy a batch. It is never decorative, and it is never used to mean 'primary' in the generic sense."
      >
        <Grid>
          <Swatch name="--accent" className="bg-accent">
            <span className="text-2xs font-medium text-accent-fg">accent-fg</span>
          </Swatch>
          <Swatch name="--accent-hover" className="bg-accent-hover" />
          <Swatch name="--accent-fill" className="bg-accent-fill">
            <span className="text-2xs font-medium text-accent-text">accent-text</span>
          </Swatch>
          <Swatch name="--accent-border" className="bg-accent-border" />
          <Swatch name="--accent-ring" className="bg-accent-ring" />
          <Swatch name="--destructive" className="bg-danger">
            <span className="text-2xs font-medium text-danger-fg">danger-fg</span>
          </Swatch>
        </Grid>
      </Block>

      <Block title="Radius" note="Three, and no argument about a fourth.">
        <Row>
          {[
            ["--radius-control", "rounded-control"],
            ["--radius-card", "rounded-card"],
            ["--radius-pill", "rounded-pill"],
          ].map(([name, cls]) => (
            <div key={name} className="flex flex-col items-center gap-1.5">
              <div className={`size-16 border border-border-strong bg-raised ${cls}`} />
              <code className="font-mono text-2xs text-muted">{name}</code>
            </div>
          ))}
        </Row>
      </Block>

      <Block
        title="Elevation"
        note="Two levels. In dark the shadows are nearly invisible and the surface ramp carries the lift; in light they do the work."
      >
        <Row className="gap-5">
          <div className="flex flex-col items-center gap-1.5">
            <div className="size-24 rounded-card border border-border bg-card shadow-raised" />
            <code className="font-mono text-2xs text-muted">--shadow-raised</code>
          </div>
          <div className="flex flex-col items-center gap-1.5">
            <div className="size-24 rounded-card border border-border bg-overlay shadow-overlay" />
            <code className="font-mono text-2xs text-muted">--shadow-overlay</code>
          </div>
        </Row>
      </Block>

      <Block
        title="Motion"
        note="Fast (120ms) is for state: hover, press, check. Slow (260ms) is for layout: a sheet arriving, a section opening. prefers-reduced-motion collapses both globally, so no component has to remember."
      >
        <Frame>
          <div className="flex flex-col gap-2 font-mono text-xs text-secondary">
            <div>--motion-fast: 120ms — hover, press, focus, check</div>
            <div>--motion-slow: 260ms — dialogs, sheets, disclosure</div>
            <div>--motion-ease: cubic-bezier(0.22, 1, 0.36, 1)</div>
            <div className="text-muted">
              @media (prefers-reduced-motion: reduce) — both collapse to 1ms
            </div>
          </div>
          <div className="mt-4 flex items-center gap-3">
            <div className="size-3 rounded-pill bg-status-running-text breathe" />
            <span className="text-xs text-secondary">
              the `breathe` animation, used only by a running stage
            </span>
          </div>
        </Frame>
      </Block>
    </Section>
  );
}

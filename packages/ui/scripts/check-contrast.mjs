#!/usr/bin/env node
/**
 * Checks every text-on-surface pair the design system claims is readable
 * against WCAG AA, in both themes, by parsing tokens.css directly.
 *
 * Parsing the stylesheet rather than a duplicated table of values is the
 * point: there is no second copy of the palette to drift, and a token
 * edited to a prettier colour that fails AA fails this check in the same
 * commit that made it pretty.
 *
 * Usage: node scripts/check-contrast.mjs [--verbose]
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const css = readFileSync(join(here, "..", "src", "tokens.css"), "utf8");

/* ---- tokens.css -> { light: {name: value}, dark: {name: value} } ---- */

function blocksFor(selector) {
  // Every top-level `selector { ... }` block, concatenated. tokens.css
  // declares `:root` and `.dark` more than once (colour, then shape).
  const out = [];
  const re = new RegExp(`(^|\\n)${selector}\\s*\\{`, "g");
  let m;
  while ((m = re.exec(css)) !== null) {
    let depth = 1;
    let i = re.lastIndex;
    while (i < css.length && depth > 0) {
      if (css[i] === "{") depth++;
      else if (css[i] === "}") depth--;
      i++;
    }
    out.push(css.slice(re.lastIndex, i - 1));
  }
  return out.join("\n");
}

function declarations(text) {
  const vars = {};
  for (const [, name, value] of text.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+);/gi)) {
    vars[name.trim()] = value.trim();
  }
  return vars;
}

const lightVars = declarations(blocksFor("\\:root,\\n\\.light"));
const darkVars = { ...lightVars, ...declarations(blocksFor("\\.dark")) };

/* ---- colour maths: oklch() -> sRGB -> relative luminance ---- */

function resolve(vars, value, seen = 0) {
  if (seen > 12) throw new Error(`variable cycle at ${value}`);
  const ref = value.match(/^var\((--[a-z0-9-]+)\)$/i);
  if (!ref) return value;
  const next = vars[ref[1]];
  if (next === undefined) throw new Error(`undefined variable ${ref[1]}`);
  return resolve(vars, next, seen + 1);
}

function parseOklch(value) {
  const m = value.match(
    /^oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)(?:\s*\/\s*[\d.]+)?\s*\)$/i,
  );
  if (!m) return null;
  return { L: +m[1], C: +m[2], h: +m[3] };
}

function oklchToLinearSrgb({ L, C, h }) {
  const hr = (h * Math.PI) / 180;
  const a = C * Math.cos(hr);
  const b = C * Math.sin(hr);

  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.291485548 * b;

  const l = l_ ** 3;
  const m = m_ ** 3;
  const s = s_ ** 3;

  return [
    +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ];
}

/** WCAG relative luminance, clamped: out-of-gamut components are what a
 *  browser would clip to anyway. */
function luminance(value) {
  const oklch = parseOklch(value);
  if (!oklch) throw new Error(`not an oklch() colour: ${value}`);
  const [r, g, b] = oklchToLinearSrgb(oklch).map((c) => Math.min(1, Math.max(0, c)));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(vars, fg, bg) {
  const a = luminance(resolve(vars, `var(${fg})`));
  const b = luminance(resolve(vars, `var(${bg})`));
  const [hi, lo] = a > b ? [a, b] : [b, a];
  return (hi + 0.05) / (lo + 0.05);
}

/* ---- the pairs the system promises are readable ---- */

const SURFACES = ["--surface-page", "--surface-card", "--surface-raised", "--surface-sunken"];
const STATUSES = ["pending", "running", "passed", "parked", "failed", "over-budget"];
const LAYERS = ["product", "api", "data", "infra", "ui", "other"];
const DIFFS = ["added", "removed", "changed", "conflict"];
const SERVERS = ["conformant", "degraded", "unreachable"];

/** AA body text. Status/layer/diff labels are small, so they get the same
 *  bar rather than the 3:1 large-text exemption. */
const AA = 4.5;
/** WCAG 1.4.11, for the graphics that are the *sole* carrier of a meaning:
 *  a focus ring, a form control's boundary, a diff gutter marker, the
 *  conflict band's edge. */
const AA_NON_TEXT = 3;
/** Chip and band edges are reinforcement, not identification — the label
 *  inside them is checked at AA and carries the state on its own. They only
 *  have to be visible as an edge. Holding them to 3:1 would force every
 *  status chip to wear a hard outline, which is exactly the noise a quiet
 *  palette exists to avoid. */
const PERCEIVABLE = 1.4;

const pairs = [];
const push = (fg, bg, min, note) => pairs.push({ fg, bg, min, note });

for (const surface of SURFACES) {
  push("--text-primary", surface, AA, "body");
  push("--text-secondary", surface, AA, "body");
  push("--text-muted", surface, AA, "body");
}
push("--text-inverse", "--accent", AA, "inverse on accent");
push("--accent-fg", "--accent", AA, "button label");
push("--accent-text", "--surface-card", AA, "accent as text");
push("--accent-text", "--surface-page", AA, "accent as text");
push("--accent-text", "--accent-fill", AA, "accent on its own tint");
push("--accent-ring", "--surface-page", AA_NON_TEXT, "focus ring");
push("--accent-ring", "--surface-card", AA_NON_TEXT, "focus ring");
push("--destructive-foreground", "--destructive", AA, "danger button label");

for (const s of STATUSES) {
  push(`--status-${s}-text`, `--status-${s}-fill`, AA, "status label on its chip");
  push(`--status-${s}-text`, "--surface-card", AA, "status label on card");
  push(`--status-${s}-text`, "--surface-page", AA, "status label on page");
  push(`--status-${s}-border`, "--surface-card", PERCEIVABLE, "chip edge on card");
}
for (const l of LAYERS) {
  push(`--layer-${l}-text`, `--layer-${l}-fill`, AA, "layer badge");
  push(`--layer-${l}-text`, "--surface-card", AA, "layer label on card");
}
for (const d of DIFFS) {
  push(`--diff-${d}-text`, `--diff-${d}-fill`, AA, "diff text on its band");
  push(`--diff-${d}-marker`, `--diff-${d}-fill`, AA_NON_TEXT, "gutter marker");
  push(`--diff-${d}-border`, `--diff-${d}-fill`, PERCEIVABLE, "band edge");
}
// Conflict is the one diff state whose edge is load-bearing: the band has
// to be findable by scanning, not only by reading.
push("--diff-conflict-border", "--surface-card", AA_NON_TEXT, "conflict band is unmissable");
push("--diff-conflict-marker", "--surface-card", AA_NON_TEXT, "conflict marker is unmissable");
for (const s of SERVERS) {
  push(`--server-${s}-text`, `--server-${s}-fill`, AA, "health label");
  push(`--server-${s}-text`, "--surface-card", AA, "health label on card");
}
push("--border", "--surface-card", 1.15, "hairline is visible at all");
push("--border", "--surface-page", 1.15, "hairline is visible at all");
push("--border-strong", "--surface-card", PERCEIVABLE, "emphasised edge");
push("--border-control", "--surface-card", AA_NON_TEXT, "form control boundary");
push("--border-control", "--surface-page", AA_NON_TEXT, "form control boundary");

/* ---- run ---- */

const verbose = process.argv.includes("--verbose");
const failures = [];
let checked = 0;

for (const [theme, vars] of [
  ["light", lightVars],
  ["dark", darkVars],
]) {
  for (const { fg, bg, min, note } of pairs) {
    const ratio = contrast(vars, fg, bg);
    checked++;
    const ok = ratio >= min;
    if (!ok) failures.push({ theme, fg, bg, min, note, ratio });
    if (verbose || !ok) {
      const mark = ok ? "ok  " : "FAIL";
      console.log(
        `${mark} ${theme.padEnd(5)} ${ratio.toFixed(2).padStart(6)}:1 ` +
          `(needs ${min}) ${fg} on ${bg}  — ${note}`,
      );
    }
  }
}

if (failures.length > 0) {
  console.error(`\n${failures.length} of ${checked} token pairs fail contrast.`);
  process.exit(1);
}
console.log(`All ${checked} token pairs pass contrast in both themes.`);

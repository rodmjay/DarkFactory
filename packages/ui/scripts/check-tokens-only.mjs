#!/usr/bin/env node
/**
 * Fails if any source file names a raw colour instead of a token (ADR-0033).
 *
 * The compiler is the primary defence: `tokens.css` deletes Tailwind's
 * palette with `--color-*: initial`, so `bg-gray-800` produces no CSS at all.
 * This exists for the two things that slip past that.
 *
 * 1. A raw colour that compiles to nothing is *invisible*, not loud. The
 *    build stays green and the element renders transparent, which reads as a
 *    layout bug rather than as a policy violation. This names it.
 * 2. Hex, rgb() and hsl() in a className or a style prop never went through
 *    Tailwind at all, so the palette reset has no opinion about them.
 *
 * Comments are skipped. The rule's own documentation says `bg-gray-800` more
 * than once, and a checker that cannot read its own explanation would be a
 * checker people disable.
 *
 * Usage: node scripts/check-tokens-only.mjs [dir...]
 */
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, extname } from "node:path";
import { fileURLToPath } from "node:url";
import { dirname } from "node:path";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const roots = process.argv.slice(2).length
  ? process.argv.slice(2)
  : ["packages/ui/src", "dashboard/app", "dashboard/lib"];

const EXTENSIONS = new Set([".ts", ".tsx", ".js", ".jsx", ".css"]);
const SKIP_DIRS = new Set(["node_modules", ".next", "dist", "export"]);

const PALETTE =
  "slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|" +
  "teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose";
const UTILITIES =
  "bg|text|border|ring|outline|fill|stroke|shadow|divide|placeholder|caret|" +
  "decoration|accent|from|via|to";

const RULES = [
  {
    name: "Tailwind palette class",
    // `text-white/80`, `hover:bg-red-500`, `dark:border-gray-700` included.
    pattern: new RegExp(`\\b(?:${UTILITIES})-(?:${PALETTE})-\\d{2,3}\\b`, "g"),
    hint: "use a semantic token — see packages/ui/DESIGN.md § Tokens",
  },
  {
    name: "black/white utility",
    pattern: new RegExp(`\\b(?:${UTILITIES})-(?:black|white)(?:\\/\\d{1,3})?\\b`, "g"),
    hint: "surfaces and text have tokens; --scrim covers the modal dim",
  },
  {
    name: "hex colour",
    // Skips #RGB in a url() fragment or an id selector by requiring a
    // boundary and 3/4/6/8 hex digits.
    pattern: /#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/g,
    hint: "colours are declared once, in tokens.css",
  },
  {
    name: "rgb()/hsl() literal",
    pattern: /\b(?:rgba?|hsla?)\s*\(/g,
    hint: "tokens are authored in oklch() in tokens.css",
  },
];

/** Strip comments and, in CSS, the token file's own declarations. */
function strip(source, ext) {
  let text = source
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " "))
    .replace(/(^|[^:])\/\/[^\n]*/g, (m) => m.replace(/[^\n]/g, " "));
  if (ext === ".css") {
    // oklch() declarations are the definitions themselves.
    text = text.replace(/oklch\([^)]*\)/g, "");
  }
  return text;
}

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    if (SKIP_DIRS.has(entry)) continue;
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) yield* walk(path);
    else if (EXTENSIONS.has(extname(path))) yield path;
  }
}

const violations = [];
let scanned = 0;

for (const root of roots) {
  const absolute = join(repoRoot, root);
  let exists = true;
  try {
    statSync(absolute);
  } catch {
    exists = false;
  }
  if (!exists) continue;

  for (const file of walk(absolute)) {
    scanned++;
    const ext = extname(file);
    const raw = readFileSync(file, "utf8");

    // tokens.css is where colour is allowed to be a literal.
    if (file.endsWith(join("packages", "ui", "src", "tokens.css"))) continue;

    const text = strip(raw, ext);
    const lines = text.split("\n");

    for (const rule of RULES) {
      for (const [index, line] of lines.entries()) {
        rule.pattern.lastIndex = 0;
        let match;
        while ((match = rule.pattern.exec(line)) !== null) {
          violations.push({
            file: relative(repoRoot, file),
            line: index + 1,
            match: match[0],
            rule: rule.name,
            hint: rule.hint,
          });
        }
      }
    }
  }
}

if (violations.length > 0) {
  console.error(`\nTokens-only policy violated (ADR-0033) — ${violations.length} occurrence(s):\n`);
  for (const violation of violations) {
    console.error(
      `  ${violation.file}:${violation.line}  ${violation.match}` +
        `\n      ${violation.rule} — ${violation.hint}`,
    );
  }
  console.error(
    "\nA raw colour compiles to nothing here, so this would have rendered as an\n" +
      "invisible element rather than a wrong one. That is why it is a build failure.\n",
  );
  process.exit(1);
}

console.log(`Tokens-only: ${scanned} files scanned, no raw colours.`);

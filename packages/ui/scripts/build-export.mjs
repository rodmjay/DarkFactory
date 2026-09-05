#!/usr/bin/env node
/**
 * Builds `packages/ui/export/` — the bundle handed to Claude Design.
 *
 * Generated rather than assembled by hand, for the reason every other check
 * in this repo exists: a hand-copied export is a second copy of the system
 * that starts drifting the moment someone edits the first one, and it drifts
 * silently because nothing renders it.
 *
 * Everything here is either copied verbatim from the source of truth or
 * derived from it. Nothing is retyped.
 *
 *   DESIGN.md            copied from packages/ui/DESIGN.md
 *   tokens.css           copied from packages/ui/src/tokens.css
 *   design-system.json   derived from src/manifest.ts + tokens.css
 *   showcase-*.png       copied from the Playwright baselines, so the
 *                        pictures are the ones the visual suite asserts on
 *
 * Usage: node scripts/build-export.mjs [--check]
 *   --check  write nothing; exit 1 if the bundle on disk is not what this
 *            would produce. That is what makes the export a gate rather
 *            than a chore someone forgets.
 */
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const pkg = join(here, "..");
const repo = join(pkg, "..", "..");
const out = join(pkg, "export");

const check = process.argv.includes("--check");

const SNAPSHOTS = join(pkg, "tests", "__screenshots__", "showcase.spec.ts-snapshots");
const SCREENSHOTS = [
  ["showcase-dark-chromium-linux.png", "showcase-dark.png"],
  ["showcase-light-chromium-linux.png", "showcase-light.png"],
];

/* ---- the token families, read from tokens.css rather than retyped ------- */

function tokenNames() {
  const css = readFileSync(join(pkg, "src", "tokens.css"), "utf8");
  const names = new Set();
  for (const [, name] of css.matchAll(/^\s*(--[a-z0-9-]+)\s*:/gim)) {
    // The Tailwind bindings are a mapping, not a token; `--df-n-*` is the
    // private ramp the semantic tokens are built from.
    if (!name.startsWith("--color-")) names.add(name);
  }
  return [...names].sort();
}

/* ---- design-system.json ------------------------------------------------- */

/**
 * The manifest is TypeScript so that `component:` is typed against the
 * package's own exports and `tsc` fails when one is renamed. Node loads it
 * directly under `--experimental-strip-types` — no compile step, no bundler,
 * no second copy — which works only because its single import is
 * `import type`, so nothing survives erasure to be resolved at runtime.
 *
 * If that import ever stops being type-only this will start trying to load
 * React and TSX from a plain node script, and the error will be loud.
 */
async function buildJson() {
  const { manifest } = await import(join(pkg, "src", "manifest.ts"));
  return manifest;
}

/* ---- completeness: no component without an entry ----------------------- */

/**
 * Every component file must be represented in the manifest by at least one
 * of the names it exports.
 *
 * Per *file* rather than per export, because a file legitimately exports a
 * family — Card also exports CardHeader, CardTitle and four more — and the
 * manifest documents the family under one entry. What it must not do is miss
 * a whole component, which is the drift that actually happens: someone adds
 * `components/domain/foo.tsx`, wires it into the showcase, and the bundle
 * handed to a designer silently does not mention it.
 *
 * `tsc` already catches the other direction — a manifest entry naming a
 * component that no longer exists fails to typecheck, because `component` is
 * `keyof typeof ui`.
 */
function checkCoverage(manifest) {
  const documented = new Set(manifest.components.map((c) => c.component));
  const missing = [];

  for (const group of ["ui", "domain"]) {
    const dir = join(pkg, "src", "components", group);
    for (const file of readdirSync(dir)) {
      if (!file.endsWith(".tsx")) continue;
      const source = readFileSync(join(dir, file), "utf8");
      const exported = [
        ...source.matchAll(/^export\s+(?:function|const)\s+([A-Z]\w*)/gm),
        ...source.matchAll(/^export\s+const\s+([A-Z]\w*)\s*=/gm),
      ].map((m) => m[1]);

      if (exported.length === 0) continue;
      if (!exported.some((name) => documented.has(name))) {
        missing.push(`${group}/${file} — exports ${exported.slice(0, 4).join(", ")}`);
      }
    }
  }

  if (missing.length > 0) {
    console.error("These components have no entry in src/manifest.ts:\n");
    for (const item of missing) console.error(`  ${item}`);
    console.error(
      "\nThe export bundle is what a designer works from. A component missing\n" +
        "from it is a component they will not know exists.\n",
    );
    process.exit(1);
  }
}

/* ---- write, or compare ------------------------------------------------- */

const files = new Map();

function emit(name, contents) {
  files.set(name, contents);
}

function sha(buffer) {
  return createHash("sha256").update(buffer).digest("hex").slice(0, 16);
}

const manifest = await buildJson();
checkCoverage(manifest);

const tokens = readFileSync(join(pkg, "src", "tokens.css"));
const design = readFileSync(join(pkg, "DESIGN.md"));

emit("DESIGN.md", design);
emit("tokens.css", tokens);

const json = {
  $comment:
    "Generated by packages/ui/scripts/build-export.mjs. Do not edit — edit " +
    "packages/ui/src/manifest.ts and re-run `pnpm export:build`.",
  generatedFrom: {
    manifest: "packages/ui/src/manifest.ts",
    tokens: "packages/ui/src/tokens.css",
    design: "packages/ui/DESIGN.md",
  },
  ...manifest,
  tokens: tokenNames(),
};
emit("design-system.json", Buffer.from(JSON.stringify(json, null, 2) + "\n"));

for (const [from, to] of SCREENSHOTS) {
  const path = join(SNAPSHOTS, from);
  if (!existsSync(path)) {
    console.error(
      `Missing ${from}. Run \`pnpm test:visual\` to generate the baselines first —\n` +
        "the export ships the images the visual suite asserts on, not fresh captures.",
    );
    process.exit(1);
  }
  emit(to, readFileSync(path));
}

emit(
  "README.md",
  Buffer.from(
    `# Dark Factory design system — export

Generated by \`pnpm export:build\`. Do not edit anything here; edit the source
and re-run.

| File | What it is |
|---|---|
| \`DESIGN.md\` | The rules, the tokens, the component inventory, and the reasoning. Start here. |
| \`tokens.css\` | Every colour, radius, shadow and motion value in the system, in both themes. Nothing outside this file names a colour. |
| \`design-system.json\` | Machine-readable: components, props, states, constraints, and the full token list. |
| \`showcase-dark.png\` | The showcase in the primary theme, 1280px wide. |
| \`showcase-light.png\` | The same page in light. |

The screenshots are the Playwright baselines themselves, so what you see is
exactly what the visual suite compares every build against.

A \`constraint\` on a component in \`design-system.json\` is a decision with a
reason behind it, not a preference — each one is explained in DESIGN.md. If a
design needs to break one, that is worth a conversation rather than a silent
override.

Live version of this page: \`pnpm dev\`, then \`/design\`. It takes
\`?theme=dark\`, \`?theme=light\` and \`?theme=both\`.
`,
  ),
);

if (check) {
  const problems = [];
  const onDisk = existsSync(out) ? new Set(readdirSync(out)) : new Set();

  for (const [name, contents] of files) {
    const path = join(out, name);
    if (!existsSync(path)) {
      problems.push(`missing: ${name}`);
      continue;
    }
    const actual = readFileSync(path);
    if (sha(actual) !== sha(contents)) problems.push(`stale:   ${name}`);
    onDisk.delete(name);
  }
  for (const extra of onDisk) problems.push(`unexpected: ${extra}`);

  if (problems.length > 0) {
    console.error("The export bundle is out of date:\n");
    for (const problem of problems) console.error(`  ${problem}`);
    console.error("\nRun `pnpm export:build` and commit the result.\n");
    process.exit(1);
  }
  console.log(`Export bundle is current (${files.size} files).`);
} else {
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  for (const [name, contents] of files) writeFileSync(join(out, name), contents);
  console.log(`Wrote ${files.size} files to ${out.replace(repo + "/", "")}/`);
  for (const [name, contents] of files) {
    console.log(`  ${name.padEnd(22)} ${(contents.length / 1024).toFixed(1)} KiB`);
  }
}

#!/usr/bin/env node
/**
 * Every command the dashboard claims is a factory tool must be one.
 *
 * The dashboard's `FACTORY_COMMANDS` once held twelve plausible names of
 * which five were real; the rest were invented against fixtures, and nothing
 * could tell the difference until someone tried to wire a button. This check
 * closes that from both sides:
 *
 *   - a FACTORY command the factory does not register fails — a typo, a
 *     rename on one side only, an invented name;
 *   - a PENDING command the factory *does* now register fails too — the tool
 *     landed, so the name must be promoted, or the button stays wired to the
 *     local no-op forever and nobody notices.
 *
 * It reads source text on both sides rather than asking a running factory,
 * so it needs no stack and runs in `pnpm check`.
 */
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

const root = new URL("..", import.meta.url).pathname;
const store = readFileSync(join(root, "dashboard/lib/local/store.tsx"), "utf8");
const toolsDir = join(root, "src/DarkFactory.Mcp/Tools");

function listed(name) {
  const block = store.match(new RegExp(`export const ${name} = \\[([\\s\\S]*?)\\] as const;`));
  if (!block) {
    console.error(`check:commands — could not find ${name} in dashboard/lib/local/store.tsx.`);
    process.exit(1);
  }
  return [...block[1].matchAll(/"(df\.[a-z0-9_.]+)"/g)].map((m) => m[1]);
}

const registered = new Set(
  readdirSync(toolsDir)
    .filter((f) => f.endsWith(".cs"))
    .flatMap((f) => [...readFileSync(join(toolsDir, f), "utf8").matchAll(/Name = "(df\.[a-z0-9_.]+)"/g)])
    .map((m) => m[1]),
);

const factory = listed("FACTORY_COMMANDS");
const pending = listed("PENDING_COMMANDS");

const invented = factory.filter((c) => !registered.has(c));
const landed = pending.filter((c) => registered.has(c));

if (invented.length === 0 && landed.length === 0) {
  console.log(
    `Commands: ${factory.length} factory commands all registered by the factory; ` +
      `${pending.length} pending, none yet built.`,
  );
  process.exit(0);
}

for (const c of invented) {
  console.error(`  ${c} is in FACTORY_COMMANDS but the factory registers no such tool.`);
}
for (const c of landed) {
  console.error(`  ${c} is in PENDING_COMMANDS but the factory now registers it — move it to FACTORY_COMMANDS.`);
}
console.error("\ncheck:commands failed. Tool names live in src/DarkFactory.Mcp/Tools/*.cs.");
process.exit(1);

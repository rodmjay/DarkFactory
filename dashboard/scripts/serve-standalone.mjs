#!/usr/bin/env node
/**
 * Serve the standalone build locally.
 *
 * `next start` does not work with `output: "standalone"`, and standalone is
 * not optional here — it is what makes the image buildable at all, because
 * pnpm's node_modules is symlinks into a store that does not survive a copy
 * between Docker stages (see dashboard/Dockerfile).
 *
 * Next emits the traced server but leaves `.next/static` and `public` where
 * they were; the Dockerfile copies them into place, and without the same
 * copy here `pnpm start` serves markup with no CSS and no JS — which looks
 * like a broken app rather than a missing step. Doing it in a script rather
 * than in a shell one-liner keeps it working on Windows too.
 */
import { cpSync, existsSync } from "node:fs";
import { join } from "node:path";
import { spawn } from "node:child_process";

const root = process.cwd();
const standalone = join(root, ".next", "standalone", "dashboard");
const server = join(standalone, "server.js");

if (!existsSync(server)) {
  console.error(
    "No standalone build found. Run `pnpm --filter dashboard build` first.\n" +
      `Looked for: ${server}`,
  );
  process.exit(1);
}

cpSync(join(root, ".next", "static"), join(standalone, ".next", "static"), {
  recursive: true,
});
if (existsSync(join(root, "public"))) {
  cpSync(join(root, "public"), join(standalone, "public"), { recursive: true });
}

const child = spawn(process.execPath, [server], {
  stdio: "inherit",
  env: process.env,
});
child.on("exit", (code) => process.exit(code ?? 0));

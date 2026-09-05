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
 *
 * Two things here exist because of a specific failure, not out of caution.
 *
 * **The server runs in this process, not a child.** It used to be spawned,
 * and killing this wrapper then orphaned the spawned `next-server`, which
 * kept the port. The next run could not bind, so its health check, its log
 * check and its page fetch all answered — from the *previous* build. A
 * stylesheet the current build never emitted then 404s, and the failure
 * reads as a broken asset pipeline rather than as a stale process. Four had
 * accumulated before anyone noticed. One process, nothing to orphan.
 *
 * **A busy port is a hard failure.** Refusing is the whole point: a smoke
 * check that quietly measures a server it did not start is worse than one
 * that does not run, because it reports on the wrong thing with confidence.
 */
import { createServer } from "node:net";
import { cpSync, existsSync, rmSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const root = process.cwd();
const standalone = join(root, ".next", "standalone", "dashboard");
const server = join(standalone, "server.js");
const port = Number(process.env.PORT ?? 3000);
const host = process.env.HOSTNAME ?? "0.0.0.0";

if (!existsSync(server)) {
  console.error(
    "No standalone build found. Run `pnpm --filter dashboard build` first.\n" +
      `Looked for: ${server}`,
  );
  process.exit(1);
}

/** Fail before serving rather than letting someone else's server answer. */
async function portIsFree() {
  return new Promise((resolve) => {
    const probe = createServer();
    probe.once("error", () => resolve(false));
    probe.once("listening", () => probe.close(() => resolve(true)));
    probe.listen(port, host);
  });
}

if (!(await portIsFree())) {
  console.error(
    `Port ${port} is already in use, so this build was not served.\n` +
      "Whatever is on that port is a different process — very likely an older\n" +
      "server from a previous run — and anything that measures it now is\n" +
      "measuring the wrong build.\n\n" +
      `  ss -lptn 'sport = :${port}'    # find it\n`,
  );
  process.exit(1);
}

// Merge, but not on top of stale output: chunk names are content-hashed, so
// a plain recursive copy accumulates every build's chunks for ever and the
// directory stops telling you what this build actually produced.
const staticSrc = join(root, ".next", "static");
const staticDst = join(standalone, ".next", "static");
rmSync(staticDst, { recursive: true, force: true });
cpSync(staticSrc, staticDst, { recursive: true });

if (existsSync(join(root, "public"))) {
  cpSync(join(root, "public"), join(standalone, "public"), { recursive: true });
}

// Next's standalone entry chdirs to its own directory and starts listening
// on import. In-process, so there is exactly one thing to kill.
process.chdir(standalone);
await import(pathToFileURL(server).href);

import path from "node:path";
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  /* `@dark-factory/ui` is published as TypeScript source inside the
   * workspace rather than as a built bundle: the design system is edited
   * alongside the app that consumes it, and a build step between the two
   * only buys stale artifacts. Next compiles it with the app. */
  transpilePackages: ["@dark-factory/ui"],

  /* Standalone output traces the files the server actually reaches and
   * emits them with a minimal node_modules. In a pnpm workspace that is not
   * an optimisation but the thing that makes the image buildable at all:
   * pnpm's node_modules is a tree of symlinks into a content-addressed
   * store, and copying it between Docker stages leaves the links dangling. */
  output: "standalone",

  /* Without this, tracing starts at ./dashboard and stops short of
   * packages/ui, which is outside it. Points at the workspace root. */
  outputFileTracingRoot: path.resolve(process.cwd(), ".."),

  /* `next dev` otherwise writes dashboard/AGENTS.md and a dashboard/CLAUDE.md
   * that imports it (`@AGENTS.md`) — which Claude Code then loads as project
   * instructions for everything under dashboard/. The repository's agent
   * contract is the root CLAUDE.md, curated on purpose; a third party's
   * boilerplate joining it because a dev server started is not a decision
   * anybody made. Both files were committed by accident in 5e244b1 and are
   * removed alongside this. Next's own note recommends committing them; if
   * that is ever wanted, it should be a reviewed change, not a side effect. */
  agentRules: false,

  /* `/` sends the reader to step 1 of the four-step flow. (For one commit
   * it opened the roster instead, while the conversation was not yet read
   * from the factory and would have greeted everyone with a "not wired"
   * panel.)
   *
   * A routing-layer redirect rather than a page that calls `redirect()`.
   * A redirecting *page* is still a rendered React route: Next emits a
   * 307 whose body is an error stub referencing that route's own CSS
   * chunk — a chunk which, because the page renders nothing, was never
   * emitted. Anything that reads the body instead of following the
   * redirect then asks for a stylesheet that 500s. This has no body at
   * all, which is what a redirect should be. */
  async redirects() {
    return [{ source: "/", destination: "/conversation", permanent: false }];
  },
};

export default nextConfig;

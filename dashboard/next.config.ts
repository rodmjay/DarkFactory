import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  /* `@dark-factory/ui` is published as TypeScript source inside the
   * workspace rather than as a built bundle: the design system is edited
   * alongside the app that consumes it, and a build step between the two
   * only buys stale artifacts. Next compiles it with the app. */
  transpilePackages: ["@dark-factory/ui"],
};

export default nextConfig;

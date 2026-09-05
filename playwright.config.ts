import { defineConfig, devices } from "@playwright/test";

/**
 * Visual tests for the design system's showcase.
 *
 * The web server is started by the config rather than assumed, so
 * `pnpm test:visual` works from a clean checkout without a second terminal —
 * a test that requires the reader to already have something running is a
 * test that gets skipped.
 *
 * A fixed port well away from 3000: this machine, and any machine running
 * more than one stack, has that taken.
 */

const PORT = Number(process.env.SHOWCASE_PORT ?? 13210);

export default defineConfig({
  testDir: "./packages/ui/tests",
  snapshotDir: "./packages/ui/tests/__screenshots__",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],

  use: {
    baseURL: `http://127.0.0.1:${PORT}`,
    // Screenshots must be comparable between machines, so nothing here is
    // left to the runner's defaults.
    viewport: { width: 1280, height: 900 },
    deviceScaleFactor: 1,
    colorScheme: "no-preference",
    timezoneId: "UTC",
    locale: "en-GB",
  },

  expect: {
    toHaveScreenshot: {
      // Absolute, not a ratio. A ratio scales the tolerance with the image,
      // so a 22,000px-tall section silently absorbs changes that would fail
      // on a short one — which it did: an accent hue shift showed up in
      // three captures and slipped past the domain section entirely. The
      // budget for antialiasing jitter does not grow just because the page
      // is long.
      maxDiffPixels: 200,
      animations: "disabled",
      caret: "hide",
      scale: "css",
    },
  },

  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"], deviceScaleFactor: 1 },
    },
  ],

  webServer: {
    // `build` then `start`, not `dev`: dev-mode rendering differs enough from
    // production to make a baseline captured in one useless against the other.
    // The standalone server takes its port from the environment, not a flag.
    command: `pnpm --filter dashboard build && PORT=${PORT} pnpm --filter dashboard start`,
    port: PORT,
    reuseExistingServer: !process.env.CI,
    timeout: 300_000,
    stdout: "ignore",
    stderr: "pipe",
  },
});

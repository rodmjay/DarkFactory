import { expect, test } from "@playwright/test";

/**
 * The showcase, captured in both themes so visual drift is reviewable in a
 * pull request.
 *
 * Full-page captures of the whole showcase, not per-component snapshots. The
 * point is not to assert that a button is 32px tall — a unit test would do
 * that and would fail on every deliberate change — it is that a reviewer can
 * see what moved. A per-component suite also gives a false sense of coverage:
 * most regressions in a design system are relationships between components,
 * and no component's own snapshot contains a relationship.
 *
 * `?theme=` fixes the mode from the streamed HTML, so nothing here has to
 * click a toggle and wait for a class to land.
 */

const SECTIONS = ["foundations", "semantics", "base", "domain"] as const;

for (const theme of ["dark", "light"] as const) {
  test.describe(`${theme} theme`, () => {
    test.beforeEach(async ({ page }) => {
      await page.goto(`/design?theme=${theme}`, { waitUntil: "networkidle" });

      // Webfonts change metrics, and a capture taken before they land differs
      // from one taken after by more than any real regression would.
      await page.evaluate(() => document.fonts.ready);

      // The sticky header renders at the scroll offset of each tile in a
      // full-page capture, which puts a copy of it partway down the image.
      // Static during capture; its own styling is covered by the header
      // appearing at the top.
      await page.addStyleTag({ content: ".sticky { position: static !important; }" });

      // `breathe` on a running stage and the syncing spinner never settle.
      await page.addStyleTag({
        content: "*, *::before, *::after { animation: none !important; transition: none !important; }",
      });
    });

    test("renders with no console or page errors", async ({ page }) => {
      const errors: string[] = [];
      page.on("pageerror", (error) => errors.push(String(error)));
      page.on("console", (message) => {
        if (message.type() === "error") errors.push(message.text());
      });

      await page.reload({ waitUntil: "networkidle" });
      await expect(page.locator("#domain")).toBeVisible();

      expect(errors, `page errors in the ${theme} showcase`).toEqual([]);
    });

    test("every section is present", async ({ page }) => {
      // The showcase is the living specification (ADR-0033): a section that
      // silently stopped rendering would quietly delete part of the spec.
      for (const section of SECTIONS) {
        await expect(page.locator(`#${section}`)).toBeVisible();
      }
    });

    test(`full page matches the ${theme} baseline`, async ({ page }) => {
      // Thresholds come from playwright.config.ts, where the reasoning for
      // an absolute budget rather than a ratio lives.
      await expect(page).toHaveScreenshot(`showcase-${theme}.png`, {
        fullPage: true,
        animations: "disabled",
      });
    });

    for (const section of SECTIONS) {
      test(`${section} section matches the ${theme} baseline`, async ({ page }) => {
        // Per-section as well as full-page, so a diff points at what changed
        // rather than at a 20,000px image.
        await expect(page.locator(`#${section}`)).toHaveScreenshot(
          `${section}-${theme}.png`,
          { animations: "disabled" },
        );
      });
    }
  });
}

import type { Metadata } from "next";

import { Showcase, type ThemeMode } from "./showcase";

export const metadata: Metadata = {
  title: "Design system — Dark Factory",
  description:
    "Every token and every component in every state, in both themes. The living specification for packages/ui (ADR-0033).",
};

const MODES: ThemeMode[] = ["dark", "light", "both"];

/**
 * `?theme=light|dark|both` fixes the mode without any client interaction,
 * which is what makes the page deterministically screenshottable.
 */
export default async function DesignPage({
  searchParams,
}: {
  searchParams: Promise<{ theme?: string }>;
}) {
  const { theme } = await searchParams;
  const initialMode = MODES.includes(theme as ThemeMode) ? (theme as ThemeMode) : "dark";
  const rootTheme = initialMode === "light" ? "light" : "dark";
  return (
    <>
      {/* The root element carries the theme so portaled overlays land in it
        * too. Setting it from the streamed HTML rather than only from an
        * effect is what keeps `?theme=light` from flashing dark first —
        * which matters, because this page gets screenshotted. */}
      <script
        dangerouslySetInnerHTML={{
          __html:
            `document.documentElement.classList.remove("light","dark");` +
            `document.documentElement.classList.add(${JSON.stringify(rootTheme)});`,
        }}
      />
      <Showcase initialMode={initialMode} />
    </>
  );
}

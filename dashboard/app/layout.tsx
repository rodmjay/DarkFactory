import type { Metadata } from "next";
import type * as React from "react";
import { Instrument_Sans, IBM_Plex_Mono } from "next/font/google";
import "./globals.css";

/* The three type choices are recorded in packages/ui/DESIGN.md. The package
 * names the tokens (--font-sans, --font-mono); the app is what actually
 * loads faces, because loading a font is app wiring, not visual design. */
const instrumentSans = Instrument_Sans({
  variable: "--font-instrument-sans",
  subsets: ["latin"],
  display: "swap",
});

const ibmPlexMono = IBM_Plex_Mono({
  variable: "--font-ibm-plex-mono",
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  display: "swap",
});

export const metadata: Metadata = {
  title: "Dark Factory",
  description: "Hosted, multi-project software factory dashboard.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html
      lang="en"
      /* Dark is the primary theme (ADR-0033). The header's toggle swaps this
       * class for `light`, which is a complete second theme rather than an
       * inversion of this one. */
      className={`dark ${instrumentSans.variable} ${ibmPlexMono.variable} h-full`}
      suppressHydrationWarning
    >
      <body className="min-h-full bg-page text-primary">{children}</body>
    </html>
  );
}

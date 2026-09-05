import type { Metadata } from "next";
import { Instrument_Sans, IBM_Plex_Mono } from "next/font/google";
import Link from "next/link";
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

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      /* Dark is the primary theme (ADR-0033). Light is reachable by removing
       * this class; a persisted per-user preference is step 4 work. */
      className={`dark ${instrumentSans.variable} ${ibmPlexMono.variable} h-full`}
      suppressHydrationWarning
    >
      <body className="flex min-h-full flex-col bg-page text-primary">
        <header className="border-b border-border px-6 py-3">
          <nav className="flex items-center gap-4 text-sm">
            <Link href="/projects" className="font-semibold tracking-tight">
              Dark Factory
            </Link>
            <Link href="/projects" className="text-secondary hover:text-primary">
              Projects
            </Link>
            <Link href="/design" className="ml-auto text-secondary hover:text-primary">
              Design system
            </Link>
          </nav>
        </header>
        <main className="flex-1">{children}</main>
      </body>
    </html>
  );
}

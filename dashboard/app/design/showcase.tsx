"use client";

import * as React from "react";
import { cn } from "@dark-factory/ui";

import { BaseComponents } from "./sections/base";
import { DomainComponents } from "./sections/domain";
import { Foundations } from "./sections/foundations";
import { Semantics } from "./sections/semantics";

export type ThemeMode = "dark" | "light" | "both";

const NAV = [
  ["foundations", "Foundations"],
  ["semantics", "Semantic colour"],
  ["base", "Base components"],
  ["domain", "Domain components"],
] as const;

function Content() {
  return (
    <div className="flex flex-col gap-10 px-6 py-8">
      <Foundations />
      <Semantics />
      <BaseComponents />
      <DomainComponents />
    </div>
  );
}

/**
 * The showcase is the living specification: if a token or a state is not on
 * this page it is not in the system. It renders in either theme, or in both
 * at once, because "light is finished too" is a claim that has to be
 * checkable rather than asserted.
 */
export function Showcase({ initialMode }: { initialMode: ThemeMode }) {
  const [mode, setMode] = React.useState<ThemeMode>(initialMode);

  /* Radix portals mount on document.body, which is outside whichever
   * wrapper below carries the theme class — so a dialog, tooltip or select
   * menu would render in the document's theme rather than the one being
   * shown. Driving the root element too is what keeps overlays honest.
   * In `both` the root has to pick one; overlays follow dark. */
  React.useEffect(() => {
    const root = document.documentElement;
    const wanted = mode === "light" ? "light" : "dark";
    root.classList.remove("light", "dark");
    root.classList.add(wanted);
    return () => {
      root.classList.remove("light", "dark");
      root.classList.add("dark");
    };
  }, [mode]);

  return (
    <div className="flex min-h-screen flex-col">
      <div className="sticky top-0 z-40 border-b border-border bg-page/95 px-6 py-3 backdrop-blur">
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
          <div>
            <h1 className="text-md font-semibold tracking-tight">Dark Factory design system</h1>
            <p className="text-2xs text-muted">
              ADR-0033 · packages/ui · the source of truth for prototyping
            </p>
          </div>
          <nav className="flex items-center gap-4 text-xs">
            {NAV.map(([id, label]) => (
              <a key={id} href={`#${id}`} className="text-secondary hover:text-primary">
                {label}
              </a>
            ))}
            {/* The showcase is not framed by the product's shell — it is the
              * design system's own page — so it carries its own way back. */}
            <a href="/conversation" className="text-secondary hover:text-primary">
              ← Factory
            </a>
          </nav>
          <div
            role="radiogroup"
            aria-label="Theme"
            className="ml-auto flex items-center gap-0.5 rounded-control border border-border p-0.5"
          >
            {(["dark", "light", "both"] as const).map((m) => (
              <button
                key={m}
                type="button"
                role="radio"
                aria-checked={mode === m}
                onClick={() => setMode(m)}
                className={cn(
                  "rounded-control px-2 py-1 text-2xs font-medium motion-fast transition-colors",
                  "outline-none focus-visible:outline-2 focus-visible:outline-offset-2",
                  "focus-visible:outline-accent-ring",
                  mode === m
                    ? "bg-accent text-accent-fg"
                    : "text-secondary hover:bg-sunken hover:text-primary",
                )}
              >
                {m}
              </button>
            ))}
          </div>
        </div>
      </div>

      {mode === "both" ? (
        <div className="grid flex-1 grid-cols-1 lg:grid-cols-2">
          <div className="dark border-border bg-page text-primary lg:border-r">
            <ThemeLabel>dark — primary</ThemeLabel>
            <Content />
          </div>
          <div className="light bg-page text-primary">
            <ThemeLabel>light</ThemeLabel>
            <Content />
          </div>
        </div>
      ) : (
        <div className={cn("flex-1 bg-page text-primary", mode)} data-theme={mode}>
          <Content />
        </div>
      )}
    </div>
  );
}

function ThemeLabel({ children }: { children: React.ReactNode }) {
  return (
    <div className="border-b border-border px-6 py-1.5 font-mono text-2xs tracking-wide text-muted uppercase">
      {children}
    </div>
  );
}

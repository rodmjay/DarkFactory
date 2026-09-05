import * as React from "react";
import { cn } from "@dark-factory/ui";

/** Layout furniture for the showcase itself. Deliberately plain: the page
 *  is a specimen sheet, and anything decorative here would compete with the
 *  thing being specified. */

export function Section({
  id,
  title,
  note,
  children,
}: {
  id: string;
  title: string;
  note?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section id={id} className="scroll-mt-20 border-t border-border pt-8 first:border-t-0 first:pt-0">
      <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
      {note && <p className="mt-1.5 max-w-2xl text-sm text-secondary">{note}</p>}
      <div className="mt-5 flex flex-col gap-7">{children}</div>
    </section>
  );
}

export function Block({
  title,
  note,
  children,
  className,
}: {
  title: string;
  note?: React.ReactNode;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("flex flex-col gap-3", className)}>
      <div>
        <h3 className="text-2xs font-medium tracking-wide text-muted uppercase">{title}</h3>
        {note && <p className="mt-1 max-w-2xl text-xs text-secondary">{note}</p>}
      </div>
      {children}
    </div>
  );
}

/** A framed area for specimens that need a surface under them. */
export function Frame({
  children,
  className,
  label,
}: {
  children: React.ReactNode;
  className?: string;
  label?: string;
}) {
  return (
    <div className={cn("rounded-card border border-border bg-card p-4", className)}>
      {label && (
        <div className="mb-3 font-mono text-2xs text-muted">{label}</div>
      )}
      {children}
    </div>
  );
}

export function Grid({
  children,
  cols = "auto",
  className,
}: {
  children: React.ReactNode;
  cols?: "auto" | "tight";
  className?: string;
}) {
  return (
    <div
      className={cn(
        "grid gap-3",
        cols === "auto"
          ? "grid-cols-[repeat(auto-fill,minmax(11rem,1fr))]"
          : "grid-cols-[repeat(auto-fill,minmax(7rem,1fr))]",
        className,
      )}
    >
      {children}
    </div>
  );
}

/** A single colour token: the swatch, the token name, and — for anything
 *  that carries text — the text token painted on it, so the pairing that
 *  the contrast script checks is the pairing you actually see. */
export function Swatch({
  name,
  className,
  height = "h-12",
  children,
}: {
  name: string;
  className: string;
  height?: string;
  children?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <div
        className={cn(
          "flex items-center justify-center rounded-control border border-border",
          height,
          className,
        )}
      >
        {children}
      </div>
      <code className="font-mono text-2xs break-all text-muted">{name}</code>
    </div>
  );
}

export function Row({ children, className }: { children: React.ReactNode; className?: string }) {
  return <div className={cn("flex flex-wrap items-center gap-3", className)}>{children}</div>;
}

export function StateLabel({ children }: { children: React.ReactNode }) {
  return <span className="w-24 shrink-0 font-mono text-2xs text-muted">{children}</span>;
}

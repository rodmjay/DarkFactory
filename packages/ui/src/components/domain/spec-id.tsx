import * as React from "react";

import { cn } from "../../lib/cn";

export interface SpecIdProps extends React.ComponentProps<"span"> {
  /** A ULID in Crockford base32 (specdiff.schema.json). */
  id: string;
  /** ULIDs are 26 characters and nobody reads all of them. The tail is the
   *  random part, so it is what actually distinguishes two ids. */
  truncate?: boolean;
}

export function SpecId({ id, truncate = true, className, ...props }: SpecIdProps) {
  const shown = truncate && id.length > 12 ? `${id.slice(0, 4)}…${id.slice(-6)}` : id;
  return (
    <span
      data-slot="spec-id"
      title={truncate ? id : undefined}
      className={cn(
        "inline-flex items-center rounded-control bg-sunken px-1 py-px",
        "font-mono text-2xs text-secondary tnum",
        className,
      )}
      {...props}
    >
      {shown}
    </span>
  );
}

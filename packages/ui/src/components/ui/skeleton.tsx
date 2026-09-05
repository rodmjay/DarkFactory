import * as React from "react";

import { cn } from "../../lib/cn";

/**
 * A loading placeholder. The pulse is a token-timed animation rather than
 * Tailwind's default, so `prefers-reduced-motion` flattens it along with
 * everything else instead of leaving one thing still breathing.
 */
export function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      aria-hidden
      className={cn(
        "animate-pulse rounded-control bg-sunken",
        "[animation-duration:1400ms]",
        className,
      )}
      {...props}
    />
  );
}

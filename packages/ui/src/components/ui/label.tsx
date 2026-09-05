import * as React from "react";

import { cn } from "../../lib/cn";

export function Label({ className, ...props }: React.ComponentProps<"label">) {
  return (
    <label
      data-slot="label"
      className={cn(
        "flex items-center gap-1.5 text-xs font-medium text-secondary select-none",
        "peer-disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}

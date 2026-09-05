import * as React from "react";

import { cn } from "../../lib/cn";
import { DISABLED, FOCUS_RING } from "../../lib/styles";

export function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        "flex h-8 w-full min-w-0 rounded-control border border-border-control",
        "bg-card px-2.5 py-1 text-sm text-primary motion-fast transition-colors",
        "placeholder:text-muted",
        "file:mr-2 file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-primary",
        "aria-invalid:border-danger",
        FOCUS_RING,
        DISABLED,
        className,
      )}
      {...props}
    />
  );
}

import * as React from "react";

import { cn } from "../../lib/cn";
import { DISABLED, FOCUS_RING } from "../../lib/styles";

export function Textarea({ className, ...props }: React.ComponentProps<"textarea">) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        "flex min-h-16 w-full rounded-control border border-border-control",
        "bg-card px-2.5 py-2 text-sm text-primary motion-fast transition-colors",
        "field-sizing-content placeholder:text-muted",
        "aria-invalid:border-danger",
        FOCUS_RING,
        DISABLED,
        className,
      )}
      {...props}
    />
  );
}

"use client";

import * as React from "react";
import * as SwitchPrimitive from "@radix-ui/react-switch";

import { cn } from "../../lib/cn";
import { DISABLED, FOCUS_RING } from "../../lib/styles";

export function Switch({ className, ...props }: React.ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      className={cn(
        "peer inline-flex h-4.5 w-8 shrink-0 items-center rounded-pill border",
        "motion-fast transition-colors",
        "data-[state=unchecked]:border-border-control data-[state=unchecked]:bg-sunken",
        "data-[state=checked]:border-transparent data-[state=checked]:bg-accent",
        FOCUS_RING,
        DISABLED,
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className={cn(
          "pointer-events-none block size-3.5 rounded-pill motion-fast transition-transform",
          "data-[state=unchecked]:translate-x-0.5 data-[state=unchecked]:bg-border-control",
          "data-[state=checked]:translate-x-4 data-[state=checked]:bg-accent-fg",
        )}
      />
    </SwitchPrimitive.Root>
  );
}

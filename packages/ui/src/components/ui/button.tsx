import * as React from "react";
import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";

import { cn } from "../../lib/cn";
import { DISABLED, FOCUS_RING } from "../../lib/styles";

/**
 * `needs-you` is the only variant allowed to wear the accent. Approve an
 * amendment, answer a parked agent, deploy a batch — the three things that
 * stop the factory until a person acts. Using it anywhere else spends the
 * one signal the interface has for "you, specifically, right now".
 */
const buttonVariants = cva(
  cn(
    "inline-flex shrink-0 items-center justify-center gap-1.5 whitespace-nowrap",
    "rounded-control border font-medium motion-fast transition-colors",
    "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
    FOCUS_RING,
    DISABLED,
  ),
  {
    variants: {
      variant: {
        default: cn(
          "border-border-strong bg-raised text-primary",
          "hover:bg-sunken active:bg-sunken",
        ),
        "needs-you": cn(
          "border-transparent bg-accent text-accent-fg shadow-raised",
          "hover:bg-accent-hover",
        ),
        outline: cn(
          "border-border-strong bg-transparent text-primary",
          "hover:bg-sunken",
        ),
        ghost: cn(
          "border-transparent bg-transparent text-secondary",
          "hover:bg-sunken hover:text-primary",
        ),
        danger: cn(
          "border-transparent bg-danger text-danger-fg",
          "hover:opacity-90",
        ),
        link: cn(
          "border-transparent bg-transparent text-accent-text underline-offset-4",
          "hover:underline",
        ),
      },
      size: {
        sm: "h-7 px-2.5 text-xs",
        md: "h-8 px-3 text-sm",
        lg: "h-10 px-4 text-base",
        icon: "size-8 p-0",
      },
    },
    defaultVariants: { variant: "default", size: "md" },
  },
);

export interface ButtonProps
  extends React.ComponentProps<"button">,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
}

export function Button({ className, variant, size, asChild = false, ...props }: ButtonProps) {
  const Comp = asChild ? Slot : "button";
  return (
    <Comp
      data-slot="button"
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  );
}

export { buttonVariants };

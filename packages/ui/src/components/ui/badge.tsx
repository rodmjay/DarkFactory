import * as React from "react";
import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";

import { cn } from "../../lib/cn";

const badgeVariants = cva(
  cn(
    "inline-flex w-fit shrink-0 items-center gap-1 whitespace-nowrap",
    "rounded-pill border px-2 py-0.5 text-2xs font-medium",
    "[&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-3",
  ),
  {
    variants: {
      variant: {
        default: "border-border bg-sunken text-secondary",
        outline: "border-border-strong bg-transparent text-secondary",
        accent: "border-accent-border bg-accent-fill text-accent-text",
        solid: "border-transparent bg-accent text-accent-fg",
      },
    },
    defaultVariants: { variant: "default" },
  },
);

export interface BadgeProps
  extends React.ComponentProps<"span">,
    VariantProps<typeof badgeVariants> {
  asChild?: boolean;
}

export function Badge({ className, variant, asChild = false, ...props }: BadgeProps) {
  const Comp = asChild ? Slot : "span";
  return (
    <Comp data-slot="badge" className={cn(badgeVariants({ variant }), className)} {...props} />
  );
}

export { badgeVariants };

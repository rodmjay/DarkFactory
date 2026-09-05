"use client";

import * as React from "react";
import * as TabsPrimitive from "@radix-ui/react-tabs";

import { cn } from "../../lib/cn";
import { FOCUS_RING_INSET } from "../../lib/styles";

export const Tabs = TabsPrimitive.Root;

export function TabsList({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      className={cn("flex items-center gap-4 border-b border-border", className)}
      {...props}
    />
  );
}

/**
 * An underline rather than a pill. Tabs in this product sit above dense
 * content and often several sets deep; a filled pill row competes with the
 * data for attention, a rule does not.
 */
export function TabsTrigger({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(
        "relative -mb-px inline-flex items-center gap-1.5 border-b-2 border-transparent",
        "px-0.5 pb-2 text-sm font-medium text-secondary motion-fast transition-colors",
        "hover:text-primary",
        "data-[state=active]:border-accent data-[state=active]:text-primary",
        "disabled:pointer-events-none disabled:opacity-50",
        "[&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-4",
        FOCUS_RING_INSET,
        className,
      )}
      {...props}
    />
  );
}

export function TabsContent({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn("pt-4 outline-none", className)}
      {...props}
    />
  );
}

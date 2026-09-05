"use client";

import * as React from "react";
import * as SheetPrimitive from "@radix-ui/react-dialog";
import { XIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { FOCUS_RING } from "../../lib/styles";

export const Sheet = SheetPrimitive.Root;
export const SheetTrigger = SheetPrimitive.Trigger;
export const SheetClose = SheetPrimitive.Close;

const SIDES = {
  right: "anim-sheet-right inset-y-0 right-0 h-full w-3/4 border-l sm:max-w-md",
  left: "anim-sheet-left inset-y-0 left-0 h-full w-3/4 border-r sm:max-w-md",
  bottom: "anim-sheet-bottom inset-x-0 bottom-0 h-auto max-h-[85vh] border-t",
} as const;

export function SheetContent({
  className,
  children,
  side = "right",
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Content> & { side?: keyof typeof SIDES }) {
  return (
    <SheetPrimitive.Portal>
      <SheetPrimitive.Overlay
        data-slot="sheet-overlay"
        className="anim-overlay fixed inset-0 z-50 bg-scrim"
      />
      <SheetPrimitive.Content
        data-slot="sheet-content"
        className={cn(
          "fixed z-50 flex flex-col gap-4 border-border bg-overlay p-5 text-primary shadow-overlay",
          SIDES[side],
          className,
        )}
        {...props}
      >
        {children}
        <SheetPrimitive.Close
          className={cn(
            "absolute top-3.5 right-3.5 rounded-control p-1 text-muted",
            "motion-fast transition-colors hover:bg-sunken hover:text-primary",
            FOCUS_RING,
          )}
        >
          <XIcon className="size-4" />
          <span className="sr-only">Close</span>
        </SheetPrimitive.Close>
      </SheetPrimitive.Content>
    </SheetPrimitive.Portal>
  );
}

export function SheetHeader({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="sheet-header" className={cn("flex flex-col gap-1.5", className)} {...props} />;
}

export function SheetFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sheet-footer"
      className={cn("mt-auto flex items-center justify-end gap-2", className)}
      {...props}
    />
  );
}

export function SheetTitle({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Title>) {
  return (
    <SheetPrimitive.Title
      data-slot="sheet-title"
      className={cn("text-lg leading-tight font-semibold", className)}
      {...props}
    />
  );
}

export function SheetDescription({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Description>) {
  return (
    <SheetPrimitive.Description
      data-slot="sheet-description"
      className={cn("text-sm text-secondary", className)}
      {...props}
    />
  );
}

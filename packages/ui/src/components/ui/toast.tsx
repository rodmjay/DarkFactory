"use client";

import * as React from "react";
import * as ToastPrimitive from "@radix-ui/react-toast";
import { cva, type VariantProps } from "class-variance-authority";
import { XIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { FOCUS_RING } from "../../lib/styles";

export const ToastProvider = ToastPrimitive.Provider;

/**
 * Built on Radix Toast rather than a toast library, because the showcase
 * has to be able to render every variant statically for the screenshot
 * test — an imperative `toast()` API has no state a screenshot can catch.
 */
const toastVariants = cva(
  cn(
    "anim-sheet-right group pointer-events-auto relative flex w-full items-start gap-3",
    "rounded-card border p-3 pr-9 shadow-overlay",
  ),
  {
    variants: {
      variant: {
        default: "border-border bg-overlay text-primary",
        "needs-you": "border-accent-border bg-accent-fill text-primary",
        danger: "border-status-failed-border bg-status-failed-fill text-primary",
      },
    },
    defaultVariants: { variant: "default" },
  },
);

export function Toast({
  className,
  variant,
  ...props
}: React.ComponentProps<typeof ToastPrimitive.Root> & VariantProps<typeof toastVariants>) {
  return (
    <ToastPrimitive.Root
      data-slot="toast"
      className={cn(toastVariants({ variant }), className)}
      {...props}
    />
  );
}

export function ToastTitle({
  className,
  ...props
}: React.ComponentProps<typeof ToastPrimitive.Title>) {
  return (
    <ToastPrimitive.Title
      data-slot="toast-title"
      className={cn("text-sm font-medium", className)}
      {...props}
    />
  );
}

export function ToastDescription({
  className,
  ...props
}: React.ComponentProps<typeof ToastPrimitive.Description>) {
  return (
    <ToastPrimitive.Description
      data-slot="toast-description"
      className={cn("text-sm text-secondary", className)}
      {...props}
    />
  );
}

export function ToastAction({
  className,
  ...props
}: React.ComponentProps<typeof ToastPrimitive.Action>) {
  return (
    <ToastPrimitive.Action
      data-slot="toast-action"
      className={cn(
        "inline-flex h-7 shrink-0 items-center rounded-control border border-border-strong",
        "bg-raised px-2.5 text-xs font-medium text-primary motion-fast transition-colors",
        "hover:bg-sunken",
        FOCUS_RING,
        className,
      )}
      {...props}
    />
  );
}

export function ToastClose({
  className,
  ...props
}: React.ComponentProps<typeof ToastPrimitive.Close>) {
  return (
    <ToastPrimitive.Close
      data-slot="toast-close"
      className={cn(
        "absolute top-2.5 right-2.5 rounded-control p-1 text-muted",
        "motion-fast transition-colors hover:bg-sunken hover:text-primary",
        FOCUS_RING,
        className,
      )}
      {...props}
    >
      <XIcon className="size-3.5" />
      <span className="sr-only">Dismiss</span>
    </ToastPrimitive.Close>
  );
}

export function ToastViewport({
  className,
  ...props
}: React.ComponentProps<typeof ToastPrimitive.Viewport>) {
  return (
    <ToastPrimitive.Viewport
      data-slot="toast-viewport"
      className={cn(
        "fixed right-0 bottom-0 z-100 flex max-h-screen w-full flex-col gap-2 p-4 sm:max-w-sm",
        className,
      )}
      {...props}
    />
  );
}

export { toastVariants };

"use client";

import * as React from "react";
import { Command as CommandPrimitive } from "cmdk";
import { SearchIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "./dialog";

/**
 * The command palette is the product's real navigation: a spec id, a run, a
 * batch, an agent — everything in the factory is addressable and there are
 * too many of them for a sidebar. It is styled quietly on purpose; it is a
 * tool for people who already know what they are looking for.
 */
export function Command({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive>) {
  return (
    <CommandPrimitive
      data-slot="command"
      className={cn(
        "flex size-full flex-col overflow-hidden rounded-card bg-overlay text-primary",
        className,
      )}
      {...props}
    />
  );
}

export function CommandDialog({
  title = "Command palette",
  description = "Find a project, run, batch or spec node.",
  children,
  className,
  ...props
}: React.ComponentProps<typeof Dialog> & {
  title?: string;
  description?: string;
  className?: string;
}) {
  return (
    <Dialog {...props}>
      <DialogHeader className="sr-only">
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
      <DialogContent
        showCloseButton={false}
        className={cn("overflow-hidden p-0 sm:max-w-xl", className)}
      >
        <Command className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1.5">
          {children}
        </Command>
      </DialogContent>
    </Dialog>
  );
}

export function CommandInput({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Input>) {
  return (
    <div className="flex items-center gap-2 border-b border-border px-3">
      <SearchIcon className="size-4 shrink-0 text-muted" />
      <CommandPrimitive.Input
        data-slot="command-input"
        className={cn(
          "flex h-10 w-full bg-transparent text-sm text-primary outline-none",
          "placeholder:text-muted disabled:opacity-50",
          className,
        )}
        {...props}
      />
    </div>
  );
}

export function CommandList({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.List>) {
  return (
    <CommandPrimitive.List
      data-slot="command-list"
      className={cn("max-h-72 scroll-py-1 overflow-x-hidden overflow-y-auto p-1", className)}
      {...props}
    />
  );
}

export function CommandEmpty(props: React.ComponentProps<typeof CommandPrimitive.Empty>) {
  return (
    <CommandPrimitive.Empty
      data-slot="command-empty"
      className="py-6 text-center text-sm text-muted"
      {...props}
    />
  );
}

export function CommandGroup({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Group>) {
  return (
    <CommandPrimitive.Group
      data-slot="command-group"
      className={cn(
        "overflow-hidden text-primary",
        "[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1.5",
        "[&_[cmdk-group-heading]]:text-2xs [&_[cmdk-group-heading]]:font-medium",
        "[&_[cmdk-group-heading]]:tracking-wide [&_[cmdk-group-heading]]:text-muted",
        "[&_[cmdk-group-heading]]:uppercase",
        className,
      )}
      {...props}
    />
  );
}

export function CommandSeparator({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Separator>) {
  return (
    <CommandPrimitive.Separator
      data-slot="command-separator"
      className={cn("-mx-1 my-1 h-px bg-border", className)}
      {...props}
    />
  );
}

export function CommandItem({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Item>) {
  return (
    <CommandPrimitive.Item
      data-slot="command-item"
      className={cn(
        "relative flex cursor-default items-center gap-2 rounded-control px-2 py-1.5",
        "text-sm outline-none select-none",
        "data-[selected=true]:bg-sunken",
        "data-[disabled=true]:pointer-events-none data-[disabled=true]:opacity-50",
        "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
        "[&_svg]:text-muted",
        className,
      )}
      {...props}
    />
  );
}

export function CommandShortcut({ className, ...props }: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="command-shortcut"
      className={cn("ml-auto font-mono text-2xs tracking-widest text-muted", className)}
      {...props}
    />
  );
}

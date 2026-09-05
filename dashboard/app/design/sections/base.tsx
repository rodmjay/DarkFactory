"use client";

import * as React from "react";
import {
  ArrowUpRightIcon,
  CheckIcon,
  GitBranchIcon,
  PlayIcon,
  SettingsIcon,
  TrashIcon,
} from "lucide-react";
import {
  Avatar,
  AvatarFallback,
  AvatarImage,
  Badge,
  Button,
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
  CommandShortcut,
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuShortcut,
  DropdownMenuTrigger,
  Input,
  Label,
  Popover,
  PopoverAnchor,
  PopoverContent,
  ScrollArea,
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
  Separator,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
  Skeleton,
  Switch,
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Toast,
  ToastAction,
  ToastClose,
  ToastDescription,
  ToastProvider,
  ToastTitle,
  ToastViewport,
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@dark-factory/ui";

import { Block, Frame, Row, Section, StateLabel } from "../parts";

const VARIANTS = ["default", "needs-you", "outline", "ghost", "danger", "link"] as const;
const SIZES = ["sm", "md", "lg"] as const;

export function BaseComponents() {
  return (
    <Section
      id="base"
      title="Base components"
      note="The shadcn set, copied into packages/ui and restyled onto the tokens. Copied rather than installed: when upstream fixes a focus-management bug we take the fix, and when we disagree with a visual decision we simply do not."
    >
      <Block
        title="Button"
        note="`needs-you` is the only variant allowed to wear the accent — approve, answer, deploy. Using it anywhere else spends the one signal the interface has for 'you, specifically, right now'."
      >
        <Frame>
          <div className="flex flex-col gap-3">
            {VARIANTS.map((variant) => (
              <div key={variant} className="flex items-center gap-3">
                <StateLabel>{variant}</StateLabel>
                {SIZES.map((size) => (
                  <Button key={size} variant={variant} size={size}>
                    Approve amendment
                  </Button>
                ))}
                <Button variant={variant} disabled>
                  Disabled
                </Button>
              </div>
            ))}
            <Separator />
            <div className="flex items-center gap-3">
              <StateLabel>with icon</StateLabel>
              <Button variant="needs-you">
                <CheckIcon /> Approve
              </Button>
              <Button variant="default">
                <PlayIcon /> Run batch
              </Button>
              <Button variant="outline">
                Open PR <ArrowUpRightIcon />
              </Button>
              <Button variant="ghost" size="icon" aria-label="Settings">
                <SettingsIcon />
              </Button>
              <Button variant="danger" size="icon" aria-label="Delete">
                <TrashIcon />
              </Button>
            </div>
            <div className="flex items-center gap-3">
              <StateLabel>focus</StateLabel>
              <span className="text-xs text-secondary">
                Tab through this page — every interactive element in the system shares one focus
                ring, drawn from --accent-ring at a 2px offset.
              </span>
            </div>
          </div>
        </Frame>
      </Block>

      <Block title="Badge">
        <Frame>
          <Row>
            <Badge>default</Badge>
            <Badge variant="outline">outline</Badge>
            <Badge variant="accent">accent</Badge>
            <Badge variant="solid">solid</Badge>
            <Badge variant="accent">
              <CheckIcon /> approved
            </Badge>
          </Row>
        </Frame>
      </Block>

      <Block title="Card">
        <div className="grid gap-3 sm:grid-cols-2">
          <Card>
            <CardHeader>
              <div className="flex items-start gap-2">
                <div>
                  <CardTitle>Batch 14 — billing rules</CardTitle>
                  <CardDescription>Four amendments, ordered by depends_on.</CardDescription>
                </div>
                <CardAction>
                  <Button size="sm" variant="ghost">
                    Reorder
                  </Button>
                </CardAction>
              </div>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-secondary">
                Each run builds against the snapshot the previous run produced, so later items
                genuinely build on earlier ones.
              </p>
            </CardContent>
            <CardFooter>
              <Button size="sm" variant="needs-you">
                Deploy batch
              </Button>
              <Button size="sm" variant="ghost">
                Cancel
              </Button>
            </CardFooter>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Loading</CardTitle>
              <CardDescription>Skeleton state for the same card.</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-2">
              <Skeleton className="h-3 w-full" />
              <Skeleton className="h-3 w-4/5" />
              <Skeleton className="h-3 w-2/3" />
            </CardContent>
          </Card>
        </div>
      </Block>

      <Block title="Tabs">
        <Frame>
          <Tabs defaultValue="timeline">
            <TabsList>
              <TabsTrigger value="timeline">
                <PlayIcon /> Timeline
              </TabsTrigger>
              <TabsTrigger value="artifacts">Artifacts</TabsTrigger>
              <TabsTrigger value="cost">Cost</TabsTrigger>
              <TabsTrigger value="locked" disabled>
                Deploy (disabled)
              </TabsTrigger>
            </TabsList>
            <TabsContent value="timeline">
              <p className="text-sm text-secondary">Active tab content.</p>
            </TabsContent>
            <TabsContent value="artifacts">
              <p className="text-sm text-secondary">Plan, ChangeSet, TestReport.</p>
            </TabsContent>
            <TabsContent value="cost">
              <p className="text-sm text-secondary">Roll-up from model_calls.</p>
            </TabsContent>
          </Tabs>
        </Frame>
      </Block>

      <Block title="Table" note="A selected row and a hover row; costs are tabular so a column can be scanned.">
        <Frame>
          <Table>
            <TableCaption>Model calls for run 01JAV3K8QW.</TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead>Stage</TableHead>
                <TableHead>Member</TableHead>
                <TableHead>Deployment</TableHead>
                <TableHead className="text-right">Tokens</TableHead>
                <TableHead className="text-right">Cost</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow>
                <TableCell>plan</TableCell>
                <TableCell>planner</TableCell>
                <TableCell className="font-mono text-xs">architect</TableCell>
                <TableCell className="tnum text-right">18,204</TableCell>
                <TableCell className="tnum text-right">$0.41</TableCell>
              </TableRow>
              <TableRow data-state="selected">
                <TableCell>implement</TableCell>
                <TableCell>implementer</TableCell>
                <TableCell className="font-mono text-xs">implementer</TableCell>
                <TableCell className="tnum text-right">142,880</TableCell>
                <TableCell className="tnum text-right">$2.18</TableCell>
              </TableRow>
              <TableRow>
                <TableCell>verify</TableCell>
                <TableCell>reviewer</TableCell>
                <TableCell className="font-mono text-xs">reviewer</TableCell>
                <TableCell className="tnum text-right">31,092</TableCell>
                <TableCell className="tnum text-right">$0.66</TableCell>
              </TableRow>
            </TableBody>
            <TableFooter>
              <TableRow>
                <TableCell colSpan={3}>Total</TableCell>
                <TableCell className="tnum text-right">192,176</TableCell>
                <TableCell className="tnum text-right">$3.25</TableCell>
              </TableRow>
            </TableFooter>
          </Table>
        </Frame>
      </Block>

      <Block title="Form controls" note="Every state a control has: default, placeholder, filled, invalid, disabled, read-only.">
        <Frame>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="d-default">Input — default</Label>
              <Input id="d-default" defaultValue="renewal-blocking" />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="d-placeholder">Input — placeholder</Label>
              <Input id="d-placeholder" placeholder="Search specs, runs, batches…" />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="d-invalid">Input — invalid</Label>
              <Input id="d-invalid" aria-invalid defaultValue="not-a-ulid" />
              <p className="text-2xs text-danger">Must be a 26-character ULID.</p>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="d-disabled">Input — disabled</Label>
              <Input id="d-disabled" disabled defaultValue="locked by the gate" />
            </div>
            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="d-textarea">Textarea</Label>
              <Textarea
                id="d-textarea"
                defaultValue="Renewal is blocked if the account is delinquent."
              />
            </div>
            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="d-textarea-disabled">Textarea — disabled</Label>
              <Textarea id="d-textarea-disabled" disabled placeholder="Waiting on approval…" />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Select</Label>
              <Select defaultValue="balanced">
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectLabel>Speed preset</SelectLabel>
                    <SelectItem value="quick">quick</SelectItem>
                    <SelectItem value="balanced">balanced</SelectItem>
                    <SelectItem value="deliberate">deliberate</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Select — placeholder / disabled</Label>
              <Row>
                <Select>
                  <SelectTrigger>
                    <SelectValue placeholder="Choose a member" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="architect">architect</SelectItem>
                    <SelectItem value="planner">planner</SelectItem>
                  </SelectContent>
                </Select>
                <Select disabled>
                  <SelectTrigger>
                    <SelectValue placeholder="Disabled" />
                  </SelectTrigger>
                  <SelectContent />
                </Select>
              </Row>
            </div>
            <div className="flex flex-col gap-2 sm:col-span-2">
              <Label>Switch</Label>
              <Row className="gap-6">
                <label className="flex items-center gap-2 text-sm">
                  <Switch defaultChecked /> on
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <Switch /> off
                </label>
                <label className="flex items-center gap-2 text-sm text-muted">
                  <Switch disabled defaultChecked /> disabled on
                </label>
                <label className="flex items-center gap-2 text-sm text-muted">
                  <Switch disabled /> disabled off
                </label>
              </Row>
            </div>
          </div>
        </Frame>
      </Block>

      <Block title="Avatar">
        <Frame>
          <Row>
            <Avatar>
              <AvatarImage src="/design/portrait-sample.svg" alt="" />
              <AvatarFallback>AR</AvatarFallback>
            </Avatar>
            <Avatar>
              <AvatarFallback>PL</AvatarFallback>
            </Avatar>
            <Avatar className="size-10 rounded-pill">
              <AvatarFallback>IM</AvatarFallback>
            </Avatar>
            <Avatar className="size-12">
              <AvatarFallback>RV</AvatarFallback>
            </Avatar>
          </Row>
        </Frame>
      </Block>

      <Block title="Separator">
        <Frame>
          <div className="flex items-center gap-4">
            <span className="text-sm">plan</span>
            <Separator orientation="vertical" className="h-4" />
            <span className="text-sm">implement</span>
            <Separator orientation="vertical" className="h-4" />
            <span className="text-sm">verify</span>
          </div>
          <Separator className="my-4" />
          <span className="text-sm text-secondary">below a horizontal rule</span>
        </Frame>
      </Block>

      <Block title="ScrollArea">
        <Frame>
          <ScrollArea className="h-32 rounded-control border border-border">
            <div className="flex flex-col gap-1 p-3 font-mono text-xs text-secondary">
              {Array.from({ length: 20 }, (_, i) => (
                <div key={i}>
                  event {String(i + 1).padStart(3, "0")} — stage.started implement attempt 1
                </div>
              ))}
            </div>
          </ScrollArea>
        </Frame>
      </Block>

      <Block title="Skeleton">
        <Frame>
          <div className="flex items-center gap-3">
            <Skeleton className="size-8 rounded-card" />
            <div className="flex flex-1 flex-col gap-2">
              <Skeleton className="h-3 w-40" />
              <Skeleton className="h-3 w-64" />
            </div>
            <Skeleton className="h-8 w-24" />
          </div>
        </Frame>
      </Block>

      <Block
        title="Tooltip and Popover"
        note="Rendered open so the surface is visible in a screenshot; both also work on hover and click."
      >
        <Frame className="min-h-44">
          <TooltipProvider>
            <Row className="gap-10">
              <Tooltip open>
                <TooltipTrigger asChild>
                  <Button variant="outline">
                    <GitBranchIcon /> Snapshot
                  </Button>
                </TooltipTrigger>
                <TooltipContent side="bottom">
                  Built against snapshot 01JAV3K8QW7Z9RXNB4MDPT6FE2, taken when the batch started.
                </TooltipContent>
              </Tooltip>

              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="ghost">Hover me (closed)</Button>
                </TooltipTrigger>
                <TooltipContent>Opens on hover or focus.</TooltipContent>
              </Tooltip>

              <Popover open>
                <PopoverAnchor asChild>
                  <Button variant="outline">Provenance</Button>
                </PopoverAnchor>
                <PopoverContent side="bottom" align="start">
                  <div className="flex flex-col gap-1.5 text-xs">
                    <div className="text-2xs font-medium tracking-wide text-muted uppercase">
                      Why this exists
                    </div>
                    <div className="text-secondary">
                      Proposed by <span className="text-primary">architect</span> in turn 7 of
                      &ldquo;Billing rules&rdquo;, approved by{" "}
                      <span className="text-primary">rod</span> on 14 Mar.
                    </div>
                  </div>
                </PopoverContent>
              </Popover>
            </Row>
          </TooltipProvider>
        </Frame>
      </Block>

      <Block title="DropdownMenu, Dialog and Sheet" note="Trigger-driven: click to open each one.">
        <Frame>
          <Row>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline">Run actions</Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start">
                <DropdownMenuLabel>Run 01JAV3K8QW</DropdownMenuLabel>
                <DropdownMenuItem>
                  <PlayIcon /> Steer
                  <DropdownMenuShortcut>⌘S</DropdownMenuShortcut>
                </DropdownMenuItem>
                <DropdownMenuItem>
                  <GitBranchIcon /> Open PR
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem variant="danger">
                  <TrashIcon /> Cancel run
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>

            <Dialog>
              <DialogTrigger asChild>
                <Button variant="needs-you">Approve amendment</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>Approve amendment</DialogTitle>
                  <DialogDescription>
                    Creates 1 node, revises 2, and conflicts with a decision made on 14 March.
                    Approving seeds a run against a fresh snapshot.
                  </DialogDescription>
                </DialogHeader>
                <Textarea placeholder="Reason (optional)" />
                <DialogFooter>
                  <DialogClose asChild>
                    <Button variant="ghost">Cancel</Button>
                  </DialogClose>
                  <Button variant="needs-you">Approve</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <Sheet>
              <SheetTrigger asChild>
                <Button variant="outline">Open spec neighbourhood</Button>
              </SheetTrigger>
              <SheetContent>
                <SheetHeader>
                  <SheetTitle>Spec neighbourhood</SheetTitle>
                  <SheetDescription>
                    Nodes within one hop of 01JAV3K8QW7Z9RXNB4MDPT6FE2.
                  </SheetDescription>
                </SheetHeader>
                <div className="flex-1 text-sm text-secondary">
                  A sheet is used wherever the context behind the main view should stay visible.
                </div>
                <SheetFooter>
                  <Button variant="ghost">Close</Button>
                </SheetFooter>
              </SheetContent>
            </Sheet>
          </Row>
        </Frame>
      </Block>

      <Block
        title="Command palette"
        note="Rendered inline. In the app it lives in a dialog on ⌘K; it is the real navigation, because everything in the factory is addressable and there are far too many of them for a sidebar."
      >
        <Frame className="p-0">
          <Command className="rounded-card border border-border">
            <CommandInput placeholder="Find a project, run, batch or spec node…" />
            <CommandList>
              <CommandEmpty>Nothing matches.</CommandEmpty>
              <CommandGroup heading="Runs">
                <CommandItem>
                  <PlayIcon /> Run 01JAV3K8QW — implement
                  <CommandShortcut>⌘1</CommandShortcut>
                </CommandItem>
                <CommandItem>
                  <PlayIcon /> Run 01JAV2Z4RT — verify
                </CommandItem>
              </CommandGroup>
              <CommandSeparator />
              <CommandGroup heading="Specs">
                <CommandItem>
                  <CheckIcon /> Renewal is blocked if the account is delinquent
                </CommandItem>
                <CommandItem disabled>
                  <CheckIcon /> Retired: legacy dunning rule
                </CommandItem>
              </CommandGroup>
            </CommandList>
          </Command>
        </Frame>
      </Block>

      <Block title="Toast" note="Rendered in a static viewport so every variant is visible at once.">
        <Frame>
          <ToastProvider duration={Infinity}>
            <ToastViewport className="static flex w-full max-w-none flex-col gap-2 p-0" />
            <Toast open>
              <div className="flex flex-col gap-0.5">
                <ToastTitle>Snapshot taken</ToastTitle>
                <ToastDescription>Batch 14 will build against 01JAV3K8QW.</ToastDescription>
              </div>
              <ToastClose />
            </Toast>
            <Toast open variant="needs-you">
              <div className="flex flex-col gap-0.5">
                <ToastTitle>implementer is parked</ToastTitle>
                <ToastDescription>
                  It asked whether delinquency is evaluated at renewal time or at billing time.
                </ToastDescription>
              </div>
              <ToastAction altText="Answer the agent">Answer</ToastAction>
              <ToastClose />
            </Toast>
            <Toast open variant="danger">
              <div className="flex flex-col gap-0.5">
                <ToastTitle>verify failed</ToastTitle>
                <ToastDescription>3 of 41 tests failed; the batch is halted.</ToastDescription>
              </div>
              <ToastAction altText="View the test report">View report</ToastAction>
              <ToastClose />
            </Toast>
          </ToastProvider>
        </Frame>
      </Block>
    </Section>
  );
}

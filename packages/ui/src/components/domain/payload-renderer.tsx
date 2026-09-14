import * as React from "react";

import { cn } from "../../lib/cn";
import type { FormField, Payload } from "../../types/payload";
import { Button } from "../ui/button";
import { Input } from "../ui/input";
import { Label } from "../ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "../ui/select";
import { Switch } from "../ui/switch";
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "../ui/table";
import { Textarea } from "../ui/textarea";
import { ApprovalCard } from "./approval-card";
import { CodeDiff } from "./code-diff";
import { DecisionCard } from "./decision-card";
import { DependencyGraph } from "./dependency-graph";
import { MetricTile } from "./metric-tile";
import { SpecDiff } from "./spec-diff";
import { StageTimeline } from "./stage-timeline";

export interface PayloadRendererProps extends React.ComponentProps<"div"> {
  payloads: Payload[];
}

/**
 * Maps an ADR-0021 payload to the component that draws it.
 *
 * This is where the ADR stops being a list in a document and becomes a
 * contract: the dashboard has one fixed set of renderers, and a plugin
 * either binds to a type here or proposes a new one through the convention
 * process (ADR-0020). There is deliberately no escape hatch — no raw HTML
 * payload, no "custom" type — because the moment one exists, every plugin
 * uses it and the vocabulary stops meaning anything.
 *
 * It switches on `type`, not on `$type`. Both are on the wire with the same
 * value; `type` is the one ADR-0021 names and the one the factory controls.
 *
 * An unknown type renders as a visible, named gap rather than as nothing.
 * A payload silently dropped is indistinguishable from an agent that said
 * nothing, and that is the wrong thing to be uncertain about.
 */
export function PayloadRenderer({ payloads, className, ...props }: PayloadRendererProps) {
  return (
    <div data-slot="payload-renderer" className={cn("flex flex-col gap-4", className)} {...props}>
      {payloads.map((payload, index) => (
        <One key={index} payload={payload} />
      ))}
    </div>
  );
}

function One({ payload }: { payload: Payload }) {
  switch (payload.type) {
    case "markdown":
      return <Markdown text={payload.text} />;

    case "spec_diff":
      return (
        <SpecDiff diff={payload.diff} conflicts={payload.conflicts} summary={payload.summary} />
      );

    case "stage_timeline":
      return (
        <StageTimeline
          run_id={payload.run_id}
          snapshot_id={payload.snapshot_id}
          stages={payload.stages}
        />
      );

    case "approval_card":
      return <ApprovalCard approval={payload.approval} />;

    case "dependency_graph":
      return (
        <DependencyGraph nodes={payload.nodes} edges={payload.edges} focus={payload.focus} />
      );

    case "code_diff":
      return (
        <CodeDiff
          patch={payload.patch}
          specAnnotations={payload.spec_annotations}
          filesChanged={payload.files_changed}
        />
      );

    case "metric":
      return (
        <MetricTile
          className="w-fit min-w-44"
          label={payload.label}
          value={payload.value}
          unit={payload.unit}
          delta={payload.delta}
          delta_is_good={payload.delta_is_good}
          series={payload.series}
          period={payload.period}
        />
      );

    case "table":
      return (
        <Table>
          {payload.caption && <TableCaption>{payload.caption}</TableCaption>}
          <TableHeader>
            <TableRow>
              {payload.columns.map((column) => (
                <TableHead key={column.key} numeric={column.numeric}>
                  {column.label}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {payload.rows.map((row, index) => (
              <TableRow key={index}>
                {payload.columns.map((column) => (
                  <TableCell key={column.key} numeric={column.numeric}>
                    {row[column.key] ?? "—"}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      );

    case "form":
      return <PayloadForm payload={payload} />;

    // Read-only here: answering is a command the screen owns, so a screen
    // that wants the card live intercepts `decision` the way the
    // conversation screen intercepts `approval_card`.
    case "decision":
      return <DecisionCard decision={payload} />;

    default:
      return <Unknown payload={payload} />;
  }
}

/**
 * Markdown, rendered as text with paragraph and blockquote structure only.
 *
 * Deliberately not a markdown engine. Payload text arrives from a model, so
 * anything that renders raw HTML is an injection surface, and the vocabulary
 * has typed components for everything structural — a payload that wants a
 * table sends a `table`, not a pipe-delimited block. Step 4 can swap in a
 * sanitising renderer behind this same seam.
 */
function Markdown({ text }: { text: string }) {
  const blocks = text.split(/\n{2,}/);

  return (
    <div className="flex flex-col gap-2 text-sm text-primary">
      {blocks.map((block, index) => {
        if (block.startsWith("> ")) {
          return (
            <blockquote
              key={index}
              className="border-l-2 border-accent-border pl-3 text-secondary italic"
            >
              {inline(block.replace(/^> ?/gm, ""))}
            </blockquote>
          );
        }
        if (/^\s*[-*]\s/.test(block)) {
          return (
            <ul key={index} className="ml-4 list-disc text-secondary marker:text-muted">
              {block.split("\n").map((item, i) => (
                <li key={i}>{inline(item.replace(/^\s*[-*]\s/, ""))}</li>
              ))}
            </ul>
          );
        }
        return (
          <p key={index} className="whitespace-pre-wrap">
            {inline(block)}
          </p>
        );
      })}
    </div>
  );
}

/**
 * `**bold**`, `*italic*` and `` `code` ``, and nothing else.
 *
 * Not an engine — a splitter that emits React elements, so there is no path
 * by which model-authored text becomes markup. These three are handled
 * because agents produce them constantly and rendering the asterisks
 * literally reads as a bug in the product rather than a limit of the
 * renderer.
 */
const INLINE = /(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*)/g;

function inline(text: string): React.ReactNode[] {
  return text.split(INLINE).map((part, index) => {
    if (part.startsWith("**") && part.endsWith("**") && part.length > 4) {
      return (
        <strong key={index} className="font-semibold text-primary">
          {part.slice(2, -2)}
        </strong>
      );
    }
    if (part.startsWith("`") && part.endsWith("`") && part.length > 2) {
      return (
        <code
          key={index}
          className="rounded-[3px] bg-sunken px-1 py-px font-mono text-2xs text-secondary"
        >
          {part.slice(1, -1)}
        </code>
      );
    }
    if (part.startsWith("*") && part.endsWith("*") && part.length > 2) {
      return <em key={index}>{part.slice(1, -1)}</em>;
    }
    return <React.Fragment key={index}>{part}</React.Fragment>;
  });
}

function PayloadForm({ payload }: { payload: Extract<Payload, { type: "form" }> }) {
  return (
    <form
      className="flex flex-col gap-3 rounded-card border border-border bg-card p-3.5"
      onSubmit={(event) => event.preventDefault()}
    >
      {payload.title && (
        <span className="text-sm font-medium text-primary">{payload.title}</span>
      )}
      {payload.fields.map((field) => (
        <Field key={field.name} field={field} />
      ))}
      <Button type="submit" variant="needs-you" size="sm" className="w-fit">
        {payload.submit_label ?? "Submit"}
      </Button>
    </form>
  );
}

function Field({ field }: { field: FormField }) {
  const id = `payload-field-${field.name}`;

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>
        {field.label}
        {field.required && <span className="text-danger">*</span>}
      </Label>

      {field.kind === "textarea" && (
        <Textarea id={id} defaultValue={String(field.value ?? "")} required={field.required} />
      )}
      {field.kind === "text" && (
        <Input id={id} defaultValue={String(field.value ?? "")} required={field.required} />
      )}
      {field.kind === "switch" && <Switch id={id} defaultChecked={Boolean(field.value)} />}
      {field.kind === "select" && (
        <Select defaultValue={field.value ? String(field.value) : undefined}>
          <SelectTrigger id={id} className="w-full">
            <SelectValue placeholder="Choose…" />
          </SelectTrigger>
          <SelectContent>
            {(field.options ?? []).map((option) => (
              <SelectItem key={option.value} value={option.value}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}

      {field.help && <p className="text-2xs text-muted">{field.help}</p>}
    </div>
  );
}

function Unknown({ payload }: { payload: Payload }) {
  return (
    <div className="rounded-card border border-dashed border-border-strong bg-sunken px-3.5 py-3">
      <p className="text-sm text-secondary">
        This response used a component type this dashboard does not render:{" "}
        <code className="font-mono text-primary">{String(payload.type)}</code>
      </p>
      <p className="mt-1 text-2xs text-muted">
        The vocabulary is versioned (ADR-0021). Either this factory is newer than this
        dashboard, or a plugin emitted a type that was never added through the convention
        process.
      </p>
    </div>
  );
}

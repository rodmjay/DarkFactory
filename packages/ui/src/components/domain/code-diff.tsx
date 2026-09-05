import * as React from "react";

import { cn } from "../../lib/cn";
import { shortId } from "../../lib/format";
import { diffBand, diffMarker } from "../../lib/vocabulary";

export interface CodeDiffProps extends React.ComponentProps<"div"> {
  /** A unified diff. The factory generates it; a model never authors one. */
  patch: string;
  /**
   * Line number in the new file → the spec id that line implements.
   *
   * Keyed by file line rather than by position in the rendered list, so an
   * annotation survives the diff being re-rendered with more context.
   */
  specAnnotations?: Record<number, string>;
  filesChanged?: string[];
}

type Line = {
  text: string;
  kind: "added" | "removed" | "context" | "meta" | "conflict";
  /** The line's number in the *new* file, or null where it has none. */
  lineNumber: number | null;
};

/**
 * A unified diff with spec ids in the gutter.
 *
 * The annotations are the point. Generated code carries a stable spec
 * reference (ADR-0024) and the gutter is where that becomes readable: a
 * reviewer can see which decision produced which hunk without leaving the
 * diff, which is the bidirectional traceability the ADR promises made
 * visible rather than merely queryable.
 *
 * Conflict markers are detected and drawn in the conflict token rather than
 * treated as ordinary context. A `<<<<<<<` that renders in the same grey as
 * everything around it is a line people scroll past.
 */
export function CodeDiff({
  patch,
  specAnnotations = {},
  filesChanged,
  className,
  ...props
}: CodeDiffProps) {
  const lines = React.useMemo(() => parse(patch), [patch]);

  return (
    <div
      data-slot="code-diff"
      className={cn("overflow-hidden rounded-card border border-border bg-card", className)}
      {...props}
    >
      {filesChanged && filesChanged.length > 0 && (
        <div className="flex flex-wrap gap-x-3 gap-y-1 border-b border-border bg-sunken px-3 py-2">
          {filesChanged.map((file) => (
            <span key={file} className="font-mono text-2xs text-secondary">
              {file}
            </span>
          ))}
        </div>
      )}

      <div className="overflow-x-auto">
        <table className="w-full border-collapse font-mono text-2xs">
          <tbody>
            {lines.map((line, index) => {
              const spec = line.lineNumber === null ? undefined : specAnnotations[line.lineNumber];
              const band =
                line.kind === "added"
                  ? diffBand.added
                  : line.kind === "removed"
                    ? diffBand.removed
                    : line.kind === "conflict"
                      ? diffBand.conflict
                      : "";

              return (
                <tr key={index} className={cn(band, line.kind === "meta" && "bg-sunken")}>
                  {/* The spec gutter. Fixed width so code alignment survives
                      an annotated line appearing halfway down a hunk. */}
                  <td
                    className={cn(
                      "w-24 border-r border-border px-2 align-top select-none",
                      spec ? "text-accent-text" : "text-muted",
                    )}
                    title={spec}
                  >
                    {spec ? shortId(spec) : ""}
                  </td>
                  <td className="tnum w-10 border-r border-border px-2 text-right align-top text-muted select-none">
                    {line.lineNumber ?? ""}
                  </td>
                  <td
                    className={cn(
                      "w-4 px-1 text-center align-top select-none",
                      line.kind === "added" && diffMarker.added,
                      line.kind === "removed" && diffMarker.removed,
                      line.kind === "conflict" && diffMarker.conflict,
                    )}
                  >
                    {line.kind === "added" ? "+" : line.kind === "removed" ? "−" : ""}
                  </td>
                  <td
                    className={cn(
                      "py-px pr-3 align-top whitespace-pre",
                      line.kind === "meta" ? "text-muted" : "text-primary",
                      line.kind === "conflict" && "font-semibold",
                    )}
                  >
                    {line.text || " "}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

const CONFLICT = /^(<{7}|={7}|>{7})/;
const HUNK = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/;

/**
 * Line numbers come from the hunk headers, not from the array index.
 *
 * Counting rendered rows produces numbers that look authoritative and are
 * wrong by however many lines the hunk skipped — which is worse than showing
 * none at all, because a reader will quote them. A removed line gets no
 * number in the new file, so it gets none here.
 */
function parse(patch: string): Line[] {
  let next: number | null = null;

  return patch.split("\n").map((text) => {
    const hunk = HUNK.exec(text);
    if (hunk) {
      next = Number(hunk[1]);
      return { text, kind: "meta" as const, lineNumber: null };
    }

    if (CONFLICT.test(text)) {
      // A conflict marker occupies a line in the working file even though it
      // is not code, so it consumes a number like any other.
      const lineNumber = next;
      if (next !== null) next += 1;
      return { text, kind: "conflict" as const, lineNumber };
    }

    if (
      text.startsWith("+++") ||
      text.startsWith("---") ||
      text.startsWith("diff ")
    ) {
      return { text, kind: "meta" as const, lineNumber: null };
    }

    if (text.startsWith("+")) {
      const lineNumber = next;
      if (next !== null) next += 1;
      return { text: text.slice(1), kind: "added" as const, lineNumber };
    }

    if (text.startsWith("-")) {
      // Removed lines exist only in the old file.
      return { text: text.slice(1), kind: "removed" as const, lineNumber: null };
    }

    const lineNumber = next;
    if (next !== null) next += 1;
    return {
      text: text.startsWith(" ") ? text.slice(1) : text,
      kind: "context" as const,
      lineNumber,
    };
  });
}

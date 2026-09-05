/**
 * Number formatting for figures people compare down a column.
 *
 * Every one of these is meant to be rendered in a `tnum` context. Instrument
 * Sans is proportional by default and its digit advances vary by 31% (38.05
 * to 68.9 at 100px — `1` is barely half the width of `0`), so a column of
 * costs without tabular figures visibly jitters. The utility lives in the
 * token layer; these helpers only decide the digits.
 */

const COMPACT = new Intl.NumberFormat("en-US", {
  notation: "compact",
  maximumFractionDigits: 1,
});

const PLAIN = new Intl.NumberFormat("en-US");

/** Token counts. Grouped below 100k, compact above, because a run summary is scanned. */
export function tokens(value: number): string {
  return value < 100_000 ? PLAIN.format(value) : COMPACT.format(value);
}

/** Exact token count, for a tooltip behind a compact one. */
export function tokensExact(value: number): string {
  return PLAIN.format(value);
}

/**
 * Cost in USD. Sub-cent figures keep four places rather than rounding to
 * $0.00 — a stage that cost $0.0004 did cost something, and showing zero
 * invites the conclusion that metering is broken.
 */
export function usd(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—";
  if (value === 0) return "$0.00";
  return value < 0.01 ? `$${value.toFixed(4)}` : `$${value.toFixed(2)}`;
}

/** A duration, at the resolution a person cares about at that magnitude. */
export function duration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return "—";
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60_000);
  const seconds = Math.round((ms % 60_000) / 1000);
  return `${minutes}m ${String(seconds).padStart(2, "0")}s`;
}

/** A signed percentage, for a metric delta. */
export function delta(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—";
  const pct = value * 100;
  const rounded = Math.abs(pct) < 10 ? pct.toFixed(1) : Math.round(pct).toString();
  return `${value > 0 ? "+" : ""}${rounded}%`;
}

/**
 * A short absolute timestamp, in UTC.
 *
 * Absolute rather than relative, because "2 hours ago" is unusable in an
 * audit trail — provenance has to survive being read tomorrow.
 *
 * UTC rather than the viewer's zone, and pinned rather than left to
 * `toLocaleString`'s defaults, for a reason that is not cosmetic: this
 * renders on the server and again in the browser, and if the two disagree
 * about the zone React throws a hydration mismatch (#418) and silently
 * discards the server's markup. It also makes the showcase screenshots
 * reproducible on a machine in a different zone.
 *
 * Showing a local time is a per-viewer preference and belongs in the app,
 * applied after hydration — not in a formatter the server also calls.
 */
const TIMESTAMP = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "UTC",
  hour12: false,
});

export function at(iso: string | null | undefined): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return `${TIMESTAMP.format(date)} UTC`;
}

/** A ULID, shortened to the part that distinguishes it. */
export function shortId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 4)}…${id.slice(-6)}` : id;
}

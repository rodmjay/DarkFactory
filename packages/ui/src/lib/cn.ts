import { type ClassValue, clsx } from "clsx";
import { extendTailwindMerge } from "tailwind-merge";

/**
 * tailwind-merge has to be told about our theme, because our theme is not
 * Tailwind's. `text-primary` is a colour and `text-sm` is a size, and
 * without this configuration the merger has to guess from the shape of the
 * string — which it gets wrong for names like `muted` and `raised`.
 */
const SURFACES = ["page", "card", "raised", "overlay", "sunken"] as const;
const EDGES = ["border", "border-strong", "border-control", "scrim"] as const;
const TEXTS = ["primary", "secondary", "muted", "inverse"] as const;
const ACCENTS = [
  "accent",
  "accent-hover",
  "accent-text",
  "accent-fill",
  "accent-border",
  "accent-fg",
  "accent-ring",
  "danger",
  "danger-fg",
] as const;

const STATUSES = ["pending", "running", "passed", "parked", "failed", "over-budget"];
const LAYERS = ["product", "api", "data", "infra", "ui", "other"];
const DIFFS = ["added", "removed", "changed", "conflict"];
const SERVERS = ["conformant", "degraded", "unreachable"];

const family = (prefix: string, keys: string[], parts: string[]) =>
  keys.flatMap((key) => parts.map((part) => `${prefix}-${key}-${part}`));

const COLORS: string[] = [
  "transparent",
  "current",
  "inherit",
  ...SURFACES,
  ...EDGES,
  ...TEXTS,
  ...ACCENTS,
  ...family("status", STATUSES, ["fill", "border", "text"]),
  ...family("layer", LAYERS, ["fill", "border", "text"]),
  ...family("diff", DIFFS, ["fill", "border", "text", "marker"]),
  ...family("server", SERVERS, ["fill", "border", "text"]),
];

const TEXT_SIZES = ["2xs", "xs", "sm", "base", "md", "lg", "xl", "2xl", "3xl"];

const twMerge = extendTailwindMerge({
  override: {
    theme: {
      color: COLORS,
      radius: ["control", "card", "pill"],
      shadow: ["raised", "overlay"],
      text: TEXT_SIZES,
    },
  },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

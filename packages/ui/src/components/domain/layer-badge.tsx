import * as React from "react";

import { cn } from "../../lib/cn";
import { layerChip, layerKey } from "../../lib/vocabulary";

export interface LayerBadgeProps extends React.ComponentProps<"span"> {
  /** Open string: `layer` is customer-declared in specdiff.schema.json.
   *  Anything outside the built-in five renders in the neutral `other` slot
   *  rather than being dropped or given a colour nobody chose. */
  layer: string;
}

export function LayerBadge({ layer, className, ...props }: LayerBadgeProps) {
  const key = layerKey(layer);
  return (
    <span
      data-slot="layer-badge"
      data-layer={key}
      title={key === "other" ? `Layer declared by a standards server: ${layer}` : undefined}
      className={cn(
        "inline-flex w-fit shrink-0 items-center rounded-pill border px-1.5 py-0.5",
        "font-mono text-2xs tracking-tight whitespace-nowrap",
        layerChip[key],
        className,
      )}
      {...props}
    >
      {layer}
    </span>
  );
}

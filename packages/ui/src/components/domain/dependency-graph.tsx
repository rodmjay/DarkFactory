import * as React from "react";

import { cn } from "../../lib/cn";
import { layerKey } from "../../lib/vocabulary";
import type { GraphEdge, GraphNode } from "../../types/payload";

export interface DependencyGraphProps
  // `onSelect` is a DOM handler on a div; ours takes a node, so the DOM one
  // is dropped rather than shadowed into something that lies about its type.
  extends Omit<React.ComponentProps<"div">, "onSelect"> {
  nodes: GraphNode[];
  edges: GraphEdge[];
  /** The node the neighbourhood was traversed from. Drawn in the centre. */
  focus?: string;
  onSelect?: (node: GraphNode) => void;
}

/**
 * A small graph of spec nodes and typed edges.
 *
 * Force-free on purpose. A force simulation re-lays-out on every render, so
 * the same neighbourhood looks different each time you open it and nothing
 * about the picture is memorable. Here a focused node sits in the middle and
 * its neighbours go round it in a stable ring, ordered as given — the same
 * data always draws the same picture, which is what makes it worth looking
 * at twice. Real graph rendering, with layout that survives a hundred nodes,
 * is later work.
 *
 * Edges are labelled with their kind because ADR-0016's edges are typed and
 * an unlabelled line between two rules loses the only thing that
 * distinguishes `depends_on` from `conflicts_with`.
 */
export function DependencyGraph({
  nodes,
  edges,
  focus,
  onSelect,
  className,
  ...props
}: DependencyGraphProps) {
  const width = 420;
  const height = 260;

  const positions = React.useMemo(
    () => layout(nodes, focus, width, height),
    [nodes, focus, width, height],
  );

  // Written out, never interpolated — Tailwind scans source text.
  const NODE_FILL: Record<string, string> = {
    product: "fill-layer-product-fill stroke-layer-product-border",
    api: "fill-layer-api-fill stroke-layer-api-border",
    data: "fill-layer-data-fill stroke-layer-data-border",
    infra: "fill-layer-infra-fill stroke-layer-infra-border",
    ui: "fill-layer-ui-fill stroke-layer-ui-border",
    other: "fill-layer-other-fill stroke-layer-other-border",
  };

  return (
    <div
      data-slot="dependency-graph"
      className={cn("overflow-x-auto rounded-card border border-border bg-card", className)}
      {...props}
    >
      <svg
        viewBox={`0 0 ${width} ${height}`}
        width={width}
        height={height}
        role="img"
        aria-label={`${nodes.length} spec nodes and ${edges.length} edges`}
        className="max-w-full"
      >
        <defs>
          <marker
            id="df-arrow"
            viewBox="0 0 8 8"
            refX="7"
            refY="4"
            markerWidth="6"
            markerHeight="6"
            orient="auto-start-reverse"
          >
            <path d="M0,0 L8,4 L0,8 z" className="fill-border-strong" />
          </marker>
        </defs>

        {edges.map((edge, index) => {
          const from = positions[edge.from];
          const to = positions[edge.to];
          if (!from || !to) return null;

          const midX = (from.x + to.x) / 2;
          const midY = (from.y + to.y) / 2;
          const conflict = edge.kind === "conflicts_with";

          return (
            <g key={`${edge.from}-${edge.to}-${index}`}>
              <line
                x1={from.x}
                y1={from.y}
                x2={to.x}
                y2={to.y}
                strokeWidth={conflict ? 2 : 1}
                markerEnd="url(#df-arrow)"
                className={cn(
                  conflict ? "stroke-diff-conflict-border" : "stroke-border-strong",
                )}
                strokeDasharray={conflict ? "4 3" : undefined}
              />
              {/* A halo in the surface colour, so a label crossing a node or
                  another edge stays readable instead of dissolving into it.
                  paint-order puts the stroke behind the glyphs. */}
              <text
                x={midX}
                y={midY - 3}
                textAnchor="middle"
                paintOrder="stroke"
                strokeWidth={3}
                strokeLinejoin="round"
                className={cn(
                  "stroke-card text-[8px]",
                  conflict ? "fill-diff-conflict-text" : "fill-muted",
                )}
              >
                {edge.kind}
              </text>
            </g>
          );
        })}

        {nodes.map((node) => {
          const position = positions[node.spec_id];
          if (!position) return null;
          const key = layerKey(node.layer);
          const focused = node.spec_id === focus;

          return (
            <g
              key={node.spec_id}
              transform={`translate(${position.x}, ${position.y})`}
              className={onSelect ? "cursor-pointer" : undefined}
              onClick={() => onSelect?.(node)}
            >
              <rect
                x={-52}
                y={-16}
                width={104}
                height={32}
                rx={7}
                strokeWidth={focused ? 2 : 1}
                className={cn(
                  NODE_FILL[key],
                  focused && "stroke-accent",
                  node.retired && "opacity-50",
                )}
              />
              <text
                textAnchor="middle"
                y={-2}
                className="fill-primary text-[9px] font-medium"
              >
                {truncate(node.label, 16)}
              </text>
              <text textAnchor="middle" y={9} className="fill-muted text-[8px]">
                {node.layer}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

/** Focus in the middle, everything else on a ring around it. Deterministic. */
function layout(
  nodes: GraphNode[],
  focus: string | undefined,
  width: number,
  height: number,
): Record<string, { x: number; y: number }> {
  const centre = { x: width / 2, y: height / 2 };
  const focused = focus ? nodes.find((n) => n.spec_id === focus) : undefined;
  const others = nodes.filter((n) => n.spec_id !== focused?.spec_id);

  const positions: Record<string, { x: number; y: number }> = {};
  if (focused) positions[focused.spec_id] = centre;

  const radiusX = width / 2 - 62;
  const radiusY = height / 2 - 34;

  others.forEach((node, index) => {
    if (!focused && others.length <= 3) {
      // Too few for a ring to read as one; a row is honest about that.
      positions[node.spec_id] = {
        x: ((index + 1) / (others.length + 1)) * width,
        y: centre.y,
      };
      return;
    }
    // Start at the top rather than at 3 o'clock: the first node given is
    // the one a reader looks for first.
    const angle = (index / others.length) * Math.PI * 2 - Math.PI / 2;
    positions[node.spec_id] = {
      x: centre.x + Math.cos(angle) * radiusX,
      y: centre.y + Math.sin(angle) * radiusY,
    };
  });

  return positions;
}

function truncate(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max - 1)}…` : text;
}

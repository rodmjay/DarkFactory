# Screen: Spec graph
Route: /specs
Reads (local SQLite): spec_nodes, spec_revisions, spec_edges, spec_snapshots, graph summary
Dispatches: none (read-only; changes to the graph are amendments)
Renders:
  graph summary          → layer counts, edge-kind counts, drift and no-implementation health
  spec_node              → SpecNodeCard (states: current, retired, drifted, no implementation, selected)
  node.revisions         → revision list with Rationale (states: given, blank, absent)
  snapshot pair          → diff bands (added, changed, removed) with the previous text
States covered: browse, snapshot compare, drifted node with detail, retired node
Proposed additions: imported, awaiting the graph (ADR-0037) — built from existing components, below

## Imported, awaiting the graph (added 2026-09-14)

A project whose specifications were imported has them long before it has
nodes: extraction drafts them, questions are answered, then they are
proposed and approved. Until then the graph is empty and this screen, built
only for nodes, answered "where are my specs?" with nothing.

Without `?state=` the screen reads the factory and shows three columns:

  imported documents      → list: status pill (pending, extracted, proposed, failed), draft node and open-question counts; search
  graph nodes             → count by layer (LayerBadge), or an honest empty line
  selected document       → draft nodes (LayerBadge, kind, text, rationale) and questions (kind, status, quote, answer)
  its source text         → PayloadRenderer markdown, front matter stripped

The prototype above stays reachable under `?state=`; when approved nodes
are common, the browse/compare layout becomes the default again and this
becomes one of its panes.

## Notes

The right pane leads with revisions because the question this screen exists
to answer is "why does this rule say what it says", and the answer is the
rationale on each revision (ADR-0016 as amended) — not the current text,
which is the part everyone already has.

`Rationale` tells the three states apart: a reason given, a reason left
blank, and no reason at all. The middle one is a person to go and ask; the
last one is a missing prompt. Collapsing them with `if (rationale)` would
lose that.

Revisions are passed to the detail pane only. The browse list renders the
same nodes with `revisions` stripped, because a list that expands every
history is not a list.

The graph is read-only by construction. Changing it is an amendment, which
is the other two screens.

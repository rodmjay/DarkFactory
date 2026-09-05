/**
 * Cross-cutting class strings that must be identical everywhere.
 *
 * A focus ring that differs by a pixel between a Button and a Tab is a
 * focus ring nobody trusts, so there is one definition and every
 * interactive component in the system spreads it.
 */

export const FOCUS_RING =
  "outline-none focus-visible:outline-2 focus-visible:outline-offset-2 " +
  "focus-visible:outline-accent-ring";

/** For controls painted directly on the page, where a 2px offset would sit
 *  on top of a neighbour. */
export const FOCUS_RING_INSET =
  "outline-none focus-visible:outline-2 focus-visible:-outline-offset-2 " +
  "focus-visible:outline-accent-ring";

export const DISABLED = "disabled:pointer-events-none disabled:opacity-50";

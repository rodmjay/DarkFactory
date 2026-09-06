"use client";

/**
 * The seam ADR-0031 will sit behind.
 *
 * Screens never fetch. They read the local mirror through `useQuery` and
 * change it by dispatching a `df.*` command through `useDispatch` — which
 * is exactly the shape PowerSync gives us: reads come from local SQLite,
 * writes go to the backend connector and come back as synced rows.
 *
 * Until the sync service exists, `LocalDbProvider` holds the mirror in
 * memory and applies each command's optimistic effect locally, marking the
 * write pending the way a real checkpoint would. Wiring PowerSync is
 * therefore a change *in this file*: `useQuery` becomes a SQL query and
 * `dispatch` becomes a connector call, and not one screen moves.
 */

import * as React from "react";

import type { DecisionRow, LocalDb, ScreenName } from "./schema";
import { buildDb, type Scenario } from "./fixtures";

/**
 * The write surface. Named commands rather than table writes, because
 * ADR-0031 has no raw client writes: every invariant this system has —
 * append-only revisions, approval gates, budget checks — lives on the
 * other side of one of these.
 */
export const COMMANDS = [
  "df.conversations.create",
  "df.conversations.send",
  "df.amendments.approve",
  "df.amendments.reject",
  "df.runs.answer",
  "df.runs.steer",
  "df.batches.compose",
  "df.batches.reorder",
  "df.batches.deploy",
  "df.servers.authorize",
  "df.servers.recheck",
  "df.team.raise_budget",
] as const;
export type Command = (typeof COMMANDS)[number];

interface LocalDbContextValue {
  db: LocalDb;
  screen: ScreenName;
  dispatch: (command: Command, args?: Record<string, unknown>) => void;
  /** Prototype affordance: swap the scenario the mirror is showing. */
  scenario: Scenario;
  setScenario: (scenario: Scenario) => void;
}

const LocalDbContext = React.createContext<LocalDbContextValue | null>(null);

export function LocalDbProvider({
  screen,
  scenario,
  onScenarioChange,
  children,
}: {
  screen: ScreenName;
  scenario: Scenario;
  onScenarioChange: (scenario: Scenario) => void;
  children: React.ReactNode;
}) {
  /**
   * Local edits layered over the fixture mirror. A real connector would
   * write these through a command and let the row come back down the sync
   * stream; holding them here keeps the call sites identical either way.
   */
  const [overlay, setOverlay] = React.useState<Partial<LocalDb>>({});

  const db = React.useMemo(() => {
    const base = buildDb(scenario);
    return { ...base, ...overlay } as LocalDb;
  }, [scenario, overlay]);

  const dispatch = React.useCallback(
    (command: Command, args: Record<string, unknown> = {}) => {
      setOverlay((current) => applyCommand(command, args, current));
    },
    [],
  );

  // A scenario change is a different mirror, so local edits over the old one
  // are not meaningful against the new one.
  const setScenario = React.useCallback(
    (next: Scenario) => {
      setOverlay({});
      onScenarioChange(next);
    },
    [onScenarioChange],
  );

  const value = React.useMemo(
    () => ({ db, screen, dispatch, scenario, setScenario }),
    [db, screen, dispatch, scenario, setScenario],
  );

  return <LocalDbContext.Provider value={value}>{children}</LocalDbContext.Provider>;
}

function useLocalDb(): LocalDbContextValue {
  const value = React.useContext(LocalDbContext);
  if (!value) throw new Error("useLocalDb must be used inside <LocalDbProvider>.");
  return value;
}

/**
 * Read the local mirror.
 *
 * The selector is where a PowerSync `SELECT` goes. Keeping reads behind a
 * function of the whole db — rather than letting screens reach into the
 * provider — is what makes that substitution mechanical.
 */
export function useQuery<T>(select: (db: LocalDb) => T): T {
  const { db } = useLocalDb();
  return select(db);
}

/** The write surface. Returns nothing: the result arrives as synced rows. */
export function useDispatch() {
  return useLocalDb().dispatch;
}

export function useScreen(): ScreenName {
  return useLocalDb().screen;
}

export function useScenario() {
  const { scenario, setScenario } = useLocalDb();
  return [scenario, setScenario] as const;
}

/**
 * The optimistic half of each command.
 *
 * This is deliberately small. It exists so a click has a visible local
 * consequence — which is the behaviour a local-first UI owes the user —
 * and not to reimplement the factory: anything that needs a rule the
 * server owns simply marks the write pending and waits.
 */
function applyCommand(
  command: Command,
  args: Record<string, unknown>,
  current: Partial<LocalDb>,
): Partial<LocalDb> {
  switch (command) {
    case "df.amendments.approve":
      return { ...current, decision: { state: "approved" } };

    case "df.amendments.reject":
      return {
        ...current,
        decision: { state: "rejected", reason: String(args.reason ?? "") },
      };

    default:
      // Every other command is a server-side effect with nothing sensible
      // to show locally before the row comes back. Recording it keeps the
      // call sites real rather than dead.
      return current;
  }
}

/**
 * The approval decision, which is the one piece of local state a screen
 * both writes and immediately reads back. It lives on the overlay rather
 * than in a component so that the conversation and the amendments screen
 * agree about it — they render the same approval.
 */
export function useDecision(): DecisionRow {
  return useLocalDb().db.decision;
}

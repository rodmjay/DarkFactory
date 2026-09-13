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

import { loadProjects, registerProjectAction } from "../factory/actions";
import type { FactoryProject } from "../factory/mcp";
import type { DecisionRow, LocalDb, ProjectRow, ScreenName } from "./schema";
import { buildDb, type Scenario } from "./fixtures";

/**
 * The write surface. Named commands rather than table writes, because
 * ADR-0031 has no raw client writes: every invariant this system has —
 * append-only revisions, approval gates, budget checks — lives on the
 * other side of one of these.
 *
 * Split in two, and the split is the point. This list used to be twelve
 * plausible-looking names of which five were real factory tools — the rest
 * (`df.amendments.approve`, `df.runs.steer`, `df.conversations.send`…) were
 * invented when the screens were built against fixtures, and nothing could
 * tell the difference until someone tried to wire one. `pnpm check:commands`
 * now asserts every name below is a tool the factory actually registers.
 */

/** Tools the factory registers today. Names and argument shapes match `src/DarkFactory.Mcp/Tools`. */
export const FACTORY_COMMANDS = [
  "df.conversations.start",
  "df.conversations.turn",
  "df.specs.approve",
  "df.specs.reject",
  "df.work.steer",
] as const;

/**
 * Actions the screens offer that no factory tool performs yet.
 *
 * Kept, typed separately, so a button can still be wired to its eventual
 * name — but nothing can mistake one for a working call. When the factory
 * grows the tool, the name moves up a list; `check:commands` fails until it
 * does, so the promotion cannot be forgotten either.
 */
export const PENDING_COMMANDS = [
  "df.batches.compose",
  "df.batches.deploy",
  "df.servers.authorize",
  "df.team.raise_budget",
  "df.team.hire",
] as const;

export type FactoryCommand = (typeof FACTORY_COMMANDS)[number];
export type PendingCommand = (typeof PENDING_COMMANDS)[number];
export type Command = FactoryCommand | PendingCommand;

/**
 * How current the mirror is.
 *
 * `hydrating` is the honest state on first paint: the mirror is showing
 * whatever it has while the factory is being asked. `unreachable` is not a
 * silent fall back to fixtures — a screen that cannot reach the factory
 * says so, because a stale roster presented as live is worse than no
 * roster.
 */
/**
 * The screens whose data comes from the factory.
 *
 * Everything else is not read from the factory yet. By default the shell
 * renders those screens as exactly that — a named gap, not their fixture
 * rows — and only a `?state=` link opens the prototype underneath. Wiring a
 * screen means adding it here and deleting its slice from `fixtures.ts` — so
 * this list is the progress bar, and it is in the code rather than in a
 * document that would drift from it.
 */
export const WIRED_SCREENS: ScreenName[] = ["projects"];

export type LiveState =
  | { status: "hydrating" }
  | { status: "live" }
  | { status: "unreachable"; error: string };

interface LocalDbContextValue {
  db: LocalDb;
  screen: ScreenName;
  dispatch: (command: Command, args?: Record<string, unknown>) => void;
  /**
   * Prototype affordance: the scenario the mirror is showing, or null when
   * no `?state=` was asked for — which is the product, and shows no
   * fixture row anywhere.
   */
  scenario: Scenario | null;
  setScenario: (scenario: Scenario) => void;
  live: LiveState;
  /** Which slices of the mirror are real rather than fixtures. */
  isLive: (slice: "projects") => boolean;
  refresh: () => void;
  register: (url: string, name?: string) => Promise<{ ok: boolean; error?: string }>;
}

const LocalDbContext = React.createContext<LocalDbContextValue | null>(null);

export function LocalDbProvider({
  screen,
  scenario,
  onScenarioChange,
  children,
}: {
  screen: ScreenName;
  scenario: Scenario | null;
  onScenarioChange: (scenario: Scenario) => void;
  children: React.ReactNode;
}) {
  /**
   * Local edits layered over the fixture mirror. A real connector would
   * write these through a command and let the row come back down the sync
   * stream; holding them here keeps the call sites identical either way.
   */
  const [overlay, setOverlay] = React.useState<Partial<LocalDb>>({});

  /**
   * The projects slice, hydrated from the factory.
   *
   * `df.projects.list` is a real query against a real database, so this is
   * the first part of the mirror that is not a fixture. It is held
   * separately from the overlay because a scenario change must not discard
   * it — the roster is a fact about the org, not about which state a
   * reviewer is looking at.
   */
  const [factoryProjects, setFactoryProjects] = React.useState<ProjectRow[] | null>(null);
  const [live, setLive] = React.useState<LiveState>({ status: "hydrating" });

  /**
   * Apply whatever the factory said about the roster.
   *
   * Kept separate so both the mount effect and the retry button end in the
   * same place, and so the state updates live in a promise callback rather
   * than in an effect body — which is the shape React wants for "subscribe
   * to an external system", and what `react-hooks/set-state-in-effect`
   * checks for.
   */
  const apply = React.useCallback(
    (result: Awaited<ReturnType<typeof loadProjects>>) => {
      if (result.ok) {
        setFactoryProjects(result.data.map(toProjectRow));
        setLive({ status: "live" });
      } else {
        setLive({ status: "unreachable", error: result.error });
      }
    },
    [],
  );

  React.useEffect(() => {
    let cancelled = false;
    // `live` already starts at `hydrating`, so there is nothing to set on
    // the way in — only on the way out.
    loadProjects().then((result) => {
      if (!cancelled) apply(result);
    });
    return () => {
      cancelled = true;
    };
  }, [apply]);

  /** The retry affordance. An event handler, so it may show "hydrating". */
  const refresh = React.useCallback(() => {
    setLive({ status: "hydrating" });
    loadProjects().then(apply);
  }, [apply]);

  const register = React.useCallback(
    async (url: string, name?: string) => {
      const result = await registerProjectAction(url, name);
      if (!result.ok) return { ok: false, error: result.error };
      // The factory has done the work; re-read rather than guessing what it
      // derived — the name, the stack hints and the seeded team are all its
      // answers, not ours.
      refresh();
      return { ok: true };
    },
    [refresh],
  );

  const db = React.useMemo(() => {
    if (scenario === null) {
      // The product. Every slice anything on screen reads without `?state=`
      // is either the factory's answer or empty: the roster and the current
      // project come from `df.projects.list`, and nothing arrives before it
      // has answered. The factory carries no org, so there is none to show,
      // and the counts and the ticker are empty because no factory query
      // backs them yet — which is also what makes the header's badges and
      // the ticker disappear, by their own rules rather than by a special
      // case.
      //
      // The remaining slices are still the fixture base, because `LocalDb`
      // has no empty value for most of them. Nothing reads them here: the
      // shell renders an unwired screen as a gap, not as its page.
      const projects = factoryProjects ?? [];
      return {
        ...buildDb("settled"),
        org: "",
        project: projects[0] ?? null,
        projects,
        ticker: [],
        amendments: [],
        batches: [],
        ...overlay,
      } as LocalDb;
    }

    const base = buildDb(scenario);

    // A demo scenario that is *about* the roster keeps the fixture roster;
    // anything else shows the real one once it has arrived.
    const scenarioOwnsProjects = scenario === "projects-empty";
    const projects =
      scenarioOwnsProjects || factoryProjects === null ? base.projects : factoryProjects;

    // Under `?state=`, `project` deliberately stays the fixture project. The
    // screens that read it — conversation, amendments, batches, team, usage —
    // are showing their prototypes, and putting a real project's name above
    // fixture counts would be the one thing this seam exists to prevent.
    return { ...base, projects, ...overlay } as LocalDb;
  }, [scenario, overlay, factoryProjects]);

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

  const isLive = React.useCallback(
    (slice: "projects") => slice === "projects" && factoryProjects !== null,
    [factoryProjects],
  );

  const value = React.useMemo(
    () => ({ db, screen, dispatch, scenario, setScenario, live, isLive, refresh, register }),
    [db, screen, dispatch, scenario, setScenario, live, isLive, refresh, register],
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

/**
 * The scenario a prototype screen is showing. Only meaningful under
 * `?state=` — the product has no scenario — so it reads as `settled`, the
 * prototype's default, rather than making every prototype screen handle a
 * null it can never be rendered with.
 */
export function useScenario() {
  const { scenario, setScenario } = useLocalDb();
  return [scenario ?? "settled", setScenario] as const;
}

/** True when `?state=` asked for the prototype, and fixtures may show. */
export function usePrototype(): boolean {
  return useLocalDb().scenario !== null;
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
    case "df.specs.approve":
      return { ...current, decision: { state: "approved" } };

    case "df.specs.reject":
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

/** How current the mirror is, and how to change it. */
export function useLive() {
  const { live, isLive, refresh, register } = useLocalDb();
  return { live, isLive, refresh, register };
}

/**
 * A factory project as a roster row.
 *
 * The four roll-ups stay null: `df.projects.list` does not carry them, and
 * this dashboard cannot yet query them. Null renders as "—", which is the
 * truth — unlike zero, which would claim the project has no runs.
 *
 * `stackHints` is not `layers`. The factory derives stack hints from the
 * workspace at registration ("node", "dotnet"); layers are spec-graph
 * layers, and a project with no specs yet genuinely has none.
 *
 * `org` is empty because `df.projects.list` does not carry one — a project
 * has a team, not an org. It used to be filled from the fixture org, which
 * put a made-up tenant on a real row.
 */
function toProjectRow(project: FactoryProject): ProjectRow {
  return {
    id: project.id,
    name: project.name,
    org: "",
    node_count: 0,
    snapshot_id: "",
    layers: [],
    active_runs: null,
    parked_runs: null,
    awaiting_amendments: null,
    deployable_batches: null,
    spend_usd: null,
    workspace_mcp_url: project.workspace_mcp_url,
    stack_hints: project.stack_hints,
  };
}

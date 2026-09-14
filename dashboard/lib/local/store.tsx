"use client";

/**
 * The seam ADR-0031 will sit behind.
 *
 * Screens never fetch. They read the local mirror through `useQuery` and
 * change it by dispatching a `df.*` command through `useDispatch` — which
 * is exactly the shape PowerSync gives us: reads come from local SQLite,
 * writes go to the backend connector and come back as synced rows.
 *
 * Until the sync service exists, `LocalDbProvider` hydrates the wired
 * slices from the factory through server actions, and sends each command
 * to the factory and re-reads what it touched — the way a real checkpoint
 * would bring the rows back. Under `?state=` it instead applies each
 * command's optimistic effect to the prototype's fixtures. Wiring PowerSync
 * is therefore a change *in this file*: `useQuery` becomes a SQL query and
 * `dispatch` becomes a connector call, and not one screen moves.
 */

import * as React from "react";

import { format, type Payload } from "@dark-factory/ui";

import {
  checkServerAction,
  connectServerAction,
  driftAction,
  ingestSpecsAction,
  loadSources,
  previewCorpusAction,
  type Result,
  approveAction,
  loadConversation,
  loadConversations,
  loadProjects,
  registerProjectAction,
  rejectAction,
  sendTurnAction,
  startConversationAction,
} from "../factory/actions";
import type {
  CorpusArea,
  CorpusDrift,
  FactoryConversation,
  FactoryConversationDetail,
  FactoryIntake,
  FactoryProject,
  FactoryServer,
} from "../factory/mcp";
import type {
  ConnectionRow,
  ConversationRow,
  DecisionRow,
  IntakeRow,
  LocalDb,
  ProjectRow,
  ScreenName,
  TurnRow,
} from "./schema";
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
 * The screens whose data comes from the factory.
 *
 * Everything else is not read from the factory yet. By default the shell
 * renders those screens as exactly that — a named gap, not their fixture
 * rows — and only a `?state=` link opens the prototype underneath. Wiring a
 * screen means adding it here and deleting its slice from `fixtures.ts` — so
 * this list is the progress bar, and it is in the code rather than in a
 * document that would drift from it.
 */
export const WIRED_SCREENS: ScreenName[] = ["projects", "conversation"];

/**
 * How current the mirror is.
 *
 * `hydrating` is the honest state on first paint: the mirror is showing
 * whatever it has while the factory is being asked. `unreachable` is not a
 * silent fall back to fixtures — a screen that cannot reach the factory
 * says so, because a stale roster presented as live is worse than no
 * roster.
 */
export type LiveState =
  | { status: "hydrating" }
  | { status: "live" }
  | { status: "unreachable"; error: string };

/**
 * Which conversation is on screen, and what went wrong with the last thing
 * sent. Not a mirror table: it is the reader's own position in the data,
 * which PowerSync would not sync either.
 */
export interface ConversationControls {
  /** The conversation on screen, or null while composing a new one. */
  activeId: string | null;
  /** Null starts a new conversation; it is created by the first message, not before. */
  select: (id: string | null) => void;
  error: string | null;
  dismissError: () => void;
  /** False until the factory has answered with this project's conversations. */
  hydrated: boolean;
  /** A turn is in flight somewhere; the composer waits for it. */
  busy: boolean;
}

/**
 * Wiring the project to its servers, and pulling specifications in — the
 * left panel's buttons (ADR-0038). Each is a factory call whose result the
 * panel shows; the connections and imports it changes are re-read after.
 */
export interface SourcesControls {
  hydrated: boolean;
  connect: (url: string) => Promise<Result<FactoryServer>>;
  check: (serverId: string) => Promise<Result<{ outcome: string; server: FactoryServer }>>;
  preview: (serverId: string) => Promise<Result<CorpusArea[]>>;
  ingest: (serverId: string, area: string) => Promise<Result<{ intake_id: string; name: string; documents: number }>>;
  drift: (intakeId: string) => Promise<Result<CorpusDrift>>;
}

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
  isLive: (slice: "projects" | "conversations") => boolean;
  refresh: () => void;
  register: (url: string, name?: string) => Promise<{ ok: boolean; error?: string }>;
  conversation: ConversationControls;
  sources: SourcesControls;
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
   * Local edits layered over the fixture mirror, under `?state=` only. The
   * product sends its commands to the factory and re-reads instead.
   */
  const [overlay, setOverlay] = React.useState<Partial<LocalDb>>({});

  /**
   * The projects slice, hydrated from the factory.
   *
   * It is held separately from the overlay because a scenario change must
   * not discard it — the roster is a fact about the org, not about which
   * state a reviewer is looking at.
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

  // ------------------------------------------------------ conversations

  /**
   * The project being worked in. There is no switcher yet because the org
   * has one project; when there are several, this is what it will set.
   */
  const projectId = factoryProjects?.[0]?.id ?? null;

  const [factoryConversations, setFactoryConversations] = React.useState<ConversationRow[] | null>(null);
  /** Null means "whatever is newest". `{ id: null }` means a new, unsent conversation. */
  const [selected, setSelected] = React.useState<{ id: string | null } | null>(null);
  const [thread, setThread] = React.useState<{ id: string; turns: TurnRow[] } | null>(null);
  const [pendingTurn, setPendingTurn] = React.useState<TurnRow | null>(null);
  /** The conversation a turn is in flight in, so its thread — and only its — shows the wait. */
  const [thinkingIn, setThinkingIn] = React.useState<string | null>(null);
  const [conversationError, setConversationError] = React.useState<string | null>(null);

  // With nothing chosen, the newest conversation someone actually had — not
  // an import's thread. An intake is newest whenever one has just been
  // pulled, and opening it by default sent a question meant for the
  // architect into the import's thread instead. Still listed, still
  // selectable; just not where the reader lands.
  const activeId = selected
    ? selected.id
    : (factoryConversations?.find((c) => c.kind !== "intake")?.id ?? null);

  React.useEffect(() => {
    if (projectId === null) return;
    let cancelled = false;
    loadConversations(projectId).then((result) => {
      if (cancelled) return;
      if (result.ok) setFactoryConversations(result.data.map(toConversationRow));
      else setConversationError(result.error);
    });
    return () => {
      cancelled = true;
    };
  }, [projectId]);

  React.useEffect(() => {
    if (activeId === null) return;
    let cancelled = false;
    loadConversation(activeId).then((result) => {
      if (cancelled) return;
      if (result.ok) setThread({ id: activeId, turns: toTurnRows(result.data) });
      else setConversationError(result.error);
    });
    return () => {
      cancelled = true;
    };
  }, [activeId]);

  // ------------------------------------------------------------- sources

  const workspaceUrl = factoryProjects?.[0]?.workspace_mcp_url ?? null;
  const [factorySources, setFactorySources] = React.useState<{
    connections: ConnectionRow[];
    intakes: IntakeRow[];
  } | null>(null);

  const reloadSources = React.useCallback(async (id: string, url: string | null) => {
    const result = await loadSources(id, url);
    if (result.ok) setFactorySources(toSources(result.data));
  }, []);

  React.useEffect(() => {
    if (projectId === null) return;
    let cancelled = false;
    const load = () =>
      loadSources(projectId, workspaceUrl).then((result) => {
        if (!cancelled && result.ok) setFactorySources(toSources(result.data));
      });
    load();
    // The monitor re-checks every server every thirty seconds (ADR-0038);
    // reading at the same cadence keeps "connected" meaning connected now.
    const timer = window.setInterval(load, 30_000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [projectId, workspaceUrl]);

  const reloadConversations = React.useCallback(async (id: string) => {
    const result = await loadConversations(id);
    if (result.ok) setFactoryConversations(result.data.map(toConversationRow));
  }, []);

  const reloadThread = React.useCallback(async (id: string) => {
    const result = await loadConversation(id);
    if (result.ok) setThread({ id, turns: toTurnRows(result.data) });
    else setConversationError(result.error);
  }, []);

  /**
   * A command, sent to the factory. Nothing is applied locally except the
   * human turn while it is in flight: every other effect — the reply, an
   * amendment, an approval — is the factory's to decide, so the screen
   * re-reads what it saved rather than guessing.
   */
  const runCommand = React.useCallback(
    async (command: Command, args: Record<string, unknown>) => {
      if (projectId === null) return;
      setConversationError(null);

      switch (command) {
        case "df.conversations.start": {
          const result = await startConversationAction(
            projectId,
            typeof args.title === "string" ? args.title : undefined,
          );
          if (!result.ok) return setConversationError(result.error);
          setSelected({ id: result.data.id });
          await reloadConversations(projectId);
          return;
        }

        case "df.conversations.turn": {
          const message = String(args.message ?? "").trim();
          if (message === "") return;

          // A new conversation is created by its first message, so the
          // list never fills with empty threads somebody opened and left.
          let id = typeof args.conversation_id === "string" && args.conversation_id ? args.conversation_id : null;
          if (id === null) {
            const started = await startConversationAction(projectId, titleFrom(message));
            if (!started.ok) return setConversationError(started.error);
            id = started.data.id;
            setSelected({ id });
            // Listed now, not after the reply: a first turn can take
            // minutes, and the list saying "no conversations" meanwhile
            // contradicts the thread beside it.
            await reloadConversations(projectId);
          }

          setPendingTurn(pendingHumanTurn(id, message));
          setThinkingIn(id);
          const result = await sendTurnAction(id, message);
          setThinkingIn(null);
          setPendingTurn(null);
          if (!result.ok) setConversationError(result.error);

          await reloadThread(id);
          await reloadConversations(projectId);
          return;
        }

        case "df.specs.approve":
        case "df.specs.reject": {
          const amendmentId = String(args.amendment_id ?? "");
          const result =
            command === "df.specs.approve"
              ? await approveAction(amendmentId)
              : await rejectAction(amendmentId, String(args.reason ?? ""));
          if (!result.ok) setConversationError(result.error);

          if (activeId !== null) await reloadThread(activeId);
          await reloadConversations(projectId);
          return;
        }

        default:
          // Pending commands have no factory tool yet, and `df.work.steer`
          // is not reachable from a wired screen. Nothing to send.
          return;
      }
    },
    [projectId, activeId, reloadConversations, reloadThread],
  );

  const db = React.useMemo(() => {
    if (scenario === null) {
      // The product. Every slice anything on screen reads without `?state=`
      // is either the factory's answer or empty: the roster and the current
      // project from `df.projects.list`, the conversation list and thread
      // from `df.conversations.*`. The factory carries no org, so there is
      // none to show, and the counts and the ticker are empty because no
      // factory query backs them yet — which is also what makes the header's
      // badges and the ticker disappear, by their own rules rather than by
      // a special case.
      //
      // The remaining slices are still the fixture base, because `LocalDb`
      // has no empty value for most of them. Nothing reads them here: the
      // shell renders an unwired screen as a gap, not as its page.
      const projects = factoryProjects ?? [];
      const turns = thread !== null && thread.id === activeId ? thread.turns : [];
      const pending = pendingTurn !== null && pendingTurn.conversation_id === activeId ? [pendingTurn] : [];

      return {
        ...buildDb("settled"),
        org: "",
        project: projects[0] ?? null,
        projects,
        ticker: [],
        amendments: [],
        batches: [],
        conversations: factoryConversations ?? [],
        turns: [...turns, ...pending],
        retrieval: [],
        connections: factorySources?.connections ?? [],
        intakes: factorySources?.intakes ?? [],
        thinking: thinkingIn !== null && thinkingIn === activeId,
        decision: { state: "undecided" },
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
  }, [scenario, overlay, factoryProjects, factoryConversations, thread, activeId, pendingTurn, thinkingIn, factorySources]);

  const dispatch = React.useCallback(
    (command: Command, args: Record<string, unknown> = {}) => {
      if (scenario === null) {
        void runCommand(command, args);
        return;
      }
      setOverlay((current) => applyCommand(command, args, current));
    },
    [scenario, runCommand],
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
    (slice: "projects" | "conversations") =>
      slice === "projects" ? factoryProjects !== null : factoryConversations !== null,
    [factoryProjects, factoryConversations],
  );

  const prototypeConversationId = db.conversations[0]?.id ?? null;
  const conversation = React.useMemo<ConversationControls>(
    () =>
      scenario === null
        ? {
            activeId,
            select: (id) => {
              setConversationError(null);
              setSelected({ id });
            },
            error: conversationError,
            dismissError: () => setConversationError(null),
            hydrated: factoryConversations !== null,
            busy: thinkingIn !== null,
          }
        : {
            // The prototype's thread is always its first conversation.
            activeId: prototypeConversationId,
            select: () => {},
            error: null,
            dismissError: () => {},
            hydrated: true,
            busy: false,
          },
    [scenario, activeId, conversationError, factoryConversations, thinkingIn, prototypeConversationId],
  );

  const sources = React.useMemo<SourcesControls>(() => {
    const noProject = { ok: false as const, error: "There is no project to connect to yet." };
    const refreshAfter = async <T,>(result: Result<T>): Promise<Result<T>> => {
      if (projectId !== null) await reloadSources(projectId, workspaceUrl);
      return result;
    };

    return {
      hydrated: factorySources !== null,
      connect: async (url) => (projectId === null ? noProject : refreshAfter(await connectServerAction(url, projectId))),
      check: async (serverId) => refreshAfter(await checkServerAction(serverId)),
      preview: (serverId) => previewCorpusAction(serverId),
      ingest: async (serverId, area) => {
        if (projectId === null) return noProject;
        const result = await refreshAfter(await ingestSpecsAction(projectId, serverId, area));
        // The import's conversation is new; the list should show it.
        if (result.ok) await reloadConversations(projectId);
        return result;
      },
      drift: (intakeId) => driftAction(intakeId),
    };
  }, [projectId, workspaceUrl, factorySources, reloadSources, reloadConversations]);

  const value = React.useMemo(
    () => ({ db, screen, dispatch, scenario, setScenario, live, isLive, refresh, register, conversation, sources }),
    [db, screen, dispatch, scenario, setScenario, live, isLive, refresh, register, conversation, sources],
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

/** Which conversation is on screen, and how to move between them. */
export function useConversation(): ConversationControls {
  return useLocalDb().conversation;
}

/** Connecting servers and ingesting specifications (the left panel). */
export function useSources(): SourcesControls {
  return useLocalDb().sources;
}

function toSources(data: { servers: FactoryServer[]; intakes: FactoryIntake[] }): {
  connections: ConnectionRow[];
  intakes: IntakeRow[];
} {
  return {
    connections: data.servers.map((s) => ({
      id: s.id,
      name: s.name,
      domain: s.domain,
      url: s.url,
      status: s.status,
      last_seen_at: s.last_seen_at,
      unreachable_since: s.unreachable_since,
      last_error: s.last_error,
    })),
    intakes: data.intakes.map((i) => ({
      id: i.intake_id,
      name: i.name,
      source_server_id: i.source_server_id,
      conversation_id: i.conversation_id,
      documents: i.documents,
      extracted: i.extracted,
      proposed: i.proposed,
      open_questions: i.open_questions,
    })),
  };
}

/**
 * The optimistic half of each command, for the prototype.
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
 * The approval decision, which is the one piece of local state a prototype
 * screen both writes and immediately reads back. It lives on the overlay
 * rather than in a component so that the conversation and the amendments
 * screen agree about it — they render the same approval. In the product the
 * decision is the amendment's own status, read back from the factory.
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

/**
 * A factory conversation as a list row. The subtitle is where it stands,
 * because that — not when it started — is what decides which one to open.
 */
function toConversationRow(conversation: FactoryConversation): ConversationRow {
  const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;
  const state =
    conversation.kind === "intake"
      ? "import of existing specifications"
      : conversation.awaiting_amendments > 0
        ? `${plural(conversation.awaiting_amendments, "amendment")} awaiting`
        : conversation.amendments > 0
          ? plural(conversation.amendments, "amendment")
          : conversation.turn_count === 0
            ? "no messages yet"
            : "no amendment";

  return {
    id: conversation.id,
    project_id: conversation.project_id,
    title: conversation.title?.trim() || "Untitled conversation",
    subtitle: `${state} · ${format.at(conversation.updated_at)}`,
    kind: conversation.kind,
    updated_at: conversation.updated_at,
    turn_count: conversation.turn_count,
    // Deployments are role-named (ADR-0027) — the architect answers on
    // `architect` — so the deployment is the label, not a suffix to one.
    deployment: conversation.deployment ?? "architect",
    snapshot_id: "",
  };
}

/**
 * A thread as the screen draws it. Payloads pass through as the factory
 * stored them; the one thing added is the approval card after each spec
 * diff, carrying the amendment's current status — the turn recorded the
 * proposal, and whether it has since been approved is a separate, later
 * fact.
 */
function toTurnRows(detail: FactoryConversationDetail): TurnRow[] {
  const amendments = new Map(detail.amendments.map((a) => [a.id, a]));

  return detail.turns
    .filter((turn) => turn.role !== "system")
    .map((turn): TurnRow => {
      if (turn.role === "user") {
        return {
          id: turn.id,
          conversation_id: detail.conversation.id,
          seq: turn.seq,
          author: "human",
          author_name: "You",
          at: turn.at,
          payloads: [{ type: "markdown", text: turn.content }],
        };
      }

      const stored: Payload[] = turn.payloads?.length
        ? turn.payloads
        : [{ type: "markdown", text: turn.content }];

      const payloads = stored.flatMap((payload): Payload[] => {
        // A settled turn with no prose stores an empty markdown payload;
        // drawing it would be an empty bubble.
        if (payload.type === "markdown" && payload.text.trim() === "") return [];
        if (payload.type !== "spec_diff") return [payload];

        const amendment = amendments.get(payload.amendment_id);
        return [
          payload,
          {
            type: "approval_card",
            approval: {
              id: payload.amendment_id,
              target_type: "amendment",
              target_id: payload.amendment_id,
              summary: payload.summary,
              status:
                amendment?.status === "approved"
                  ? "approved"
                  : amendment?.status === "rejected"
                    ? "rejected"
                    : "awaiting",
              // Approvers per project are ADR-0017's and not modelled yet;
              // an empty list is the truth, not a placeholder.
              required_approvers: [],
              reason: amendment?.rejected_reason ?? undefined,
            },
          },
        ];
      });

      return {
        id: turn.id,
        conversation_id: detail.conversation.id,
        seq: turn.seq,
        author: "agent",
        author_name: "architect",
        // Only worth saying when it is not the name already on the turn.
        deployment:
          detail.conversation.deployment && detail.conversation.deployment !== "architect"
            ? detail.conversation.deployment
            : undefined,
        at: turn.at,
        payloads,
      };
    });
}

/** The human turn while the factory has not yet saved it. */
function pendingHumanTurn(conversationId: string, text: string): TurnRow {
  return {
    id: `pending-${conversationId}`,
    conversation_id: conversationId,
    seq: Number.MAX_SAFE_INTEGER,
    author: "human",
    author_name: "You",
    at: new Date().toISOString(),
    pending: true,
    payloads: [{ type: "markdown", text }],
  };
}

/** A new conversation is titled by its first message, cut at a word near sixty characters. */
function titleFrom(message: string): string {
  const line = message.split("\n")[0].trim();
  if (line.length <= 60) return line;
  const cut = line.slice(0, 60);
  const space = cut.lastIndexOf(" ");
  return `${space > 30 ? cut.slice(0, space) : cut}…`;
}

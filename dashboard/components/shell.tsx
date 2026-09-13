"use client";

/**
 * The shell every screen renders inside.
 *
 * The primary navigation *is* the four-step flow — converse, approve,
 * execute, deploy — because that is the product, and a nav that lists
 * screens instead of steps loses the one thing a new user needs to
 * understand. The counts on each step are the whole point: they say where
 * the work has stopped and who it is waiting on.
 */

import * as React from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Check, Moon, RefreshCw, Sun, WifiOff } from "lucide-react";

import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  cn,
  StatusChip,
  type StageStatus,
} from "@dark-factory/ui";

import {
  LocalDbProvider,
  WIRED_SCREENS,
  useLive,
  usePrototype,
  useQuery,
  useScenario,
  useScreen,
} from "@/lib/local/store";
import { SCENARIOS_FOR, SCENARIO_LABEL, type Scenario } from "@/lib/local/fixtures";
import type { ScreenName } from "@/lib/local/schema";

/** What each screen is called where a sentence has to name it. */
const SCREEN_LABEL: Record<ScreenName, string> = {
  conversation: "Conversation",
  amendments: "Amendments",
  specs: "Spec graph",
  batches: "Batches and runs",
  team: "Team",
  servers: "Servers",
  usage: "Usage",
  projects: "Projects",
};

/** Route ↔ screen. The nav, the state strip and the ticker all read this. */
const ROUTES: Record<ScreenName, string> = {
  conversation: "/conversation",
  amendments: "/amendments",
  specs: "/specs",
  batches: "/batches",
  team: "/team",
  servers: "/servers",
  usage: "/usage",
  projects: "/projects",
};

function screenFromPath(pathname: string): ScreenName {
  const hit = (Object.keys(ROUTES) as ScreenName[]).find((s) => pathname.startsWith(ROUTES[s]));
  return hit ?? "conversation";
}

export function FactoryShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const params = useSearchParams();
  const screen = screenFromPath(pathname);

  /**
   * No `?state=` is the product, and the product shows no fixture row
   * anywhere. The prototype is still one link away for design review, but
   * it has to be asked for: a screen that fills itself with a made-up
   * project's amendments by default is indistinguishable, to a new reader,
   * from a factory that has them. An unrecognised value is not a request
   * for the prototype either, so it falls to the product.
   */
  const raw = params.get("state");
  const scenario: Scenario | null = isScenario(raw) ? raw : null;
  const showPage = scenario !== null || WIRED_SCREENS.includes(screen);

  /**
   * The scenario lives in the URL rather than in component state, so every
   * state on every screen is a link somebody can open, screenshot or file a
   * bug against — which is the difference between a state that is claimed
   * and a state that is shown. `settled` is written out too: dropping the
   * parameter now means leaving the prototype, not choosing its default.
   */
  const setScenario = React.useCallback(
    (next: Scenario) => {
      router.replace(`${pathname}?state=${next}`, { scroll: false });
    },
    [pathname, router],
  );

  return (
    <LocalDbProvider screen={screen} scenario={scenario} onScenarioChange={setScenario}>
      <div className="flex h-screen min-w-[1364px] flex-col overflow-hidden bg-page">
        <Header />
        <Ticker />
        <div className="min-h-0 flex-1 overflow-auto">{showPage ? children : <NotWired />}</div>
        {scenario !== null ? <StateStrip /> : null}
      </div>
    </LocalDbProvider>
  );
}

/**
 * Where a nav link goes. Inside the prototype it stays inside the prototype
 * — a reviewer clicking from screen to screen should not fall out into the
 * product halfway through — keeping the current scenario where the target
 * screen offers it and its default where it does not.
 */
function useHref() {
  const prototype = usePrototype();
  const [scenario] = useScenario();

  return React.useCallback(
    (target: ScreenName) => {
      if (!prototype) return ROUTES[target];
      const state = SCENARIOS_FOR[target].includes(scenario) ? scenario : "settled";
      return `${ROUTES[target]}?state=${state}`;
    },
    [prototype, scenario],
  );
}

function isScenario(value: string | null): value is Scenario {
  return value !== null && value in SCENARIO_LABEL;
}

// ------------------------------------------------------------------ header

function Header() {
  const screen = useScreen();
  const prototype = usePrototype();
  const href = useHref();
  const { org, project, actor } = useQuery((db) => ({
    org: db.org,
    project: db.project,
    actor: db.actor,
  }));
  // Outside the prototype the mirror holds no amendments or batches — no
  // factory query backs these counts yet — so the badges are absent rather
  // than zero, by the same `> 0` rule that hides them on a quiet day.
  const awaiting = useQuery((db) => db.amendments.filter((a) => a.status === "proposed").length);
  const running = useQuery((db) => db.batches.filter((b) => b.status === "running").length);
  const deployable = useQuery((db) => db.batches.filter((b) => b.status === "deployable").length);

  // The factory carries no org, so outside the prototype this is just the
  // project's name — and nothing at all until the roster has answered,
  // because a placeholder name in this slot is read as the answer.
  const location = [org, project?.name].filter(Boolean).join(" / ");

  return (
    <header className="flex h-12 flex-none items-center gap-5 border-b border-border bg-card px-4">
      <div className="flex items-center gap-2.5">
        <span className="shrink-0 text-[13px] font-semibold tracking-tight whitespace-nowrap">
          Dark Factory
        </span>
        {location ? (
          <Link
            href={href("projects")}
            className="flex h-[26px] shrink-0 items-center gap-1.5 rounded-control border border-border bg-sunken px-2 text-xs text-secondary whitespace-nowrap hover:bg-raised hover:text-primary"
          >
            {location}
            <span className="text-[10px] text-muted">▾</span>
          </Link>
        ) : null}
      </div>

      <nav className="flex items-center gap-0.5">
        <Step n={1} label="Converse" href={href("conversation")} active={screen === "conversation"} />
        <Arrow />
        <Step
          n={2}
          label="Approve"
          href={href("amendments")}
          active={screen === "amendments"}
          badge={awaiting > 0 ? { text: String(awaiting), tone: "accent" } : undefined}
        />
        <Arrow />
        <Step
          n={3}
          label="Execute"
          href={href("batches")}
          active={screen === "batches"}
          badge={running > 0 ? { text: String(running), tone: "running" } : undefined}
        />
        <Arrow />
        <Step
          n={4}
          label="Deploy"
          href={href("batches")}
          badge={deployable > 0 ? { text: String(deployable), tone: "accent-quiet" } : undefined}
        />
      </nav>

      <div className="h-5 w-px bg-border" />

      <nav className="flex items-center gap-3.5 text-[13px]">
        <Secondary label="Projects" href={href("projects")} active={screen === "projects"} />
        <Secondary label="Spec graph" href={href("specs")} active={screen === "specs"} />
        <Secondary label="Team" href={href("team")} active={screen === "team"} />
        <Secondary label="Servers" href={href("servers")} active={screen === "servers"} />
        <Secondary label="Usage" href={href("usage")} active={screen === "usage"} />
      </nav>

      <div className="ml-auto flex items-center gap-2.5">
        <DataSource />
        {/* There is no sync service yet (ADR-0031), so "Synced" and the
            actor's initials are both fixture claims — the second one a
            named person. They belong to the prototype until the factory
            can answer who is signed in and how current the mirror is. */}
        {prototype ? <SyncBadge /> : null}
        <ThemeToggle />
        {prototype ? (
          <span className="inline-flex size-[26px] items-center justify-center rounded-pill border border-border bg-sunken text-[10px] font-semibold text-secondary">
            {actor.initials}
          </span>
        ) : null}
      </div>
    </header>
  );
}

/**
 * Where this screen's rows came from.
 *
 * Half the product reads the factory and half has nothing to read yet, and
 * a reviewer cannot tell which by looking at a populated table. Saying it in
 * the header is cheaper than being asked. `?state=` is the one place
 * fixtures still show, so it always says so there — on a wired screen too,
 * because the prototype layers fixture sync state and fixture counts over
 * whatever the factory returned.
 */
function DataSource() {
  const screen = useScreen();
  const prototype = usePrototype();
  const { live } = useLive();

  if (prototype) {
    return (
      <span
        title="Opened with ?state=, so this view shows prototype fixtures, not the factory."
        className="rounded-pill border border-border bg-sunken px-2 py-0.5 text-[11px] font-medium text-muted whitespace-nowrap"
      >
        Prototype data
      </span>
    );
  }

  if (!WIRED_SCREENS.includes(screen)) {
    return (
      <span
        title="This screen does not read the factory yet, so it has nothing to show."
        className="rounded-pill border border-border bg-sunken px-2 py-0.5 text-[11px] font-medium text-muted whitespace-nowrap"
      >
        Not wired
      </span>
    );
  }

  if (live.status === "unreachable") {
    return (
      <span
        title={live.error}
        className="rounded-pill border border-status-failed-border bg-status-failed-fill px-2 py-0.5 text-[11px] font-medium text-status-failed-text whitespace-nowrap"
      >
        Factory unreachable
      </span>
    );
  }

  return (
    <span
      title="These rows were read from the factory."
      className="rounded-pill border border-status-passed-border bg-status-passed-fill px-2 py-0.5 text-[11px] font-medium text-status-passed-text whitespace-nowrap"
    >
      {live.status === "hydrating" ? "Reading…" : "Live"}
    </span>
  );
}

function Arrow() {
  return <span className="px-0.5 text-[11px] text-muted">→</span>;
}

function Step({
  n,
  label,
  href,
  active,
  badge,
}: {
  n: number;
  label: string;
  href: string;
  active?: boolean;
  badge?: { text: string; tone: "accent" | "running" | "accent-quiet" };
}) {
  return (
    <Link
      href={href}
      className={cn(
        "flex h-[30px] shrink-0 items-center gap-[7px] rounded-control border border-transparent px-2.5 text-[13px] whitespace-nowrap",
        active ? "text-primary" : "text-secondary hover:bg-sunken hover:text-primary",
      )}
    >
      <span className="inline-flex size-[15px] shrink-0 items-center justify-center rounded-pill border border-border-strong font-mono text-[9px]">
        {n}
      </span>
      {label}
      {badge ? <StepBadge {...badge} /> : null}
      {active ? <span aria-hidden className="size-[5px] rounded-pill bg-accent" /> : null}
    </Link>
  );
}

function StepBadge({ text, tone }: { text: string; tone: "accent" | "running" | "accent-quiet" }) {
  return (
    <span
      className={cn(
        "inline-flex h-4 items-center rounded-pill px-1.5 text-[10px] tnum whitespace-nowrap",
        tone === "accent" && "bg-accent font-semibold text-accent-fg",
        tone === "running" &&
          "border border-status-running-border bg-status-running-fill font-medium text-status-running-text",
        tone === "accent-quiet" &&
          "border border-accent-border bg-accent-fill font-medium text-accent-text",
      )}
    >
      {text}
    </span>
  );
}

function Secondary({ label, href, active }: { label: string; href: string; active?: boolean }) {
  return (
    <Link
      href={href}
      className={cn(
        "flex shrink-0 items-center gap-1.5 whitespace-nowrap",
        active ? "text-primary" : "text-secondary hover:text-primary",
      )}
    >
      {label}
      {active ? <span aria-hidden className="size-[5px] rounded-pill bg-accent" /> : null}
    </Link>
  );
}

/**
 * ADR-0031 means the UI is often showing a local copy, so it has to be able
 * to say so. The pending count is the part that matters: "offline" with no
 * number is a status, "offline, 1 waiting" is information.
 */
function SyncBadge() {
  const sync = useQuery((db) => db.sync);

  if (sync.state === "syncing") {
    return (
      <span
        title="Sending local writes to the factory."
        className="inline-flex items-center gap-1.5 rounded-pill border border-status-running-border bg-status-running-fill px-2 py-0.5 text-[11px] font-medium text-status-running-text whitespace-nowrap"
      >
        <RefreshCw aria-hidden className="size-3 animate-spin" />
        Syncing
        <span className="tnum opacity-80">{sync.pending}</span>
      </span>
    );
  }

  if (sync.state === "offline") {
    return (
      <span
        title="Working from the local copy. Changes will sync when the connection returns."
        className="inline-flex items-center gap-1.5 rounded-pill border border-border bg-sunken px-2 py-0.5 text-[11px] font-medium text-secondary whitespace-nowrap"
      >
        <WifiOff aria-hidden className="size-3" />
        Offline
        <span className="tnum opacity-80">{sync.pending}</span>
      </span>
    );
  }

  return (
    <span
      title="Everything local matches the factory."
      className="inline-flex items-center gap-1.5 rounded-pill border border-border bg-sunken px-2 py-0.5 text-[11px] font-medium text-muted whitespace-nowrap"
    >
      <Check aria-hidden className="size-3" />
      Synced
    </span>
  );
}

/**
 * Dark is primary; light is a complete second theme (ADR-0033), so this
 * swaps the class the tokens key off rather than toggling a "light mode"
 * bolted onto a dark design.
 */
function ThemeToggle() {
  const [theme, setTheme] = React.useState<"dark" | "light">("dark");

  const toggle = React.useCallback(() => {
    setTheme((current) => {
      const next = current === "dark" ? "light" : "dark";
      const root = document.documentElement;
      root.classList.remove("dark", "light");
      root.classList.add(next);
      return next;
    });
  }, []);

  return (
    <button
      type="button"
      onClick={toggle}
      title="Toggle theme"
      className="inline-flex size-[30px] items-center justify-center rounded-control border border-border-strong bg-raised text-secondary hover:text-primary"
    >
      {theme === "dark" ? (
        <Moon aria-hidden className="size-3.5" />
      ) : (
        <Sun aria-hidden className="size-3.5" />
      )}
      <span className="sr-only">Toggle theme</span>
    </button>
  );
}

// ------------------------------------------------------------------ ticker

/** What is happening across the project right now, on every screen. */
function Ticker() {
  const items = useQuery((db) => db.ticker);
  const screen = useScreen();
  const href = useHref();

  // Nothing queries runs from the factory yet, so outside the prototype the
  // ticker slice is empty and the strip does not render. Inside it, it
  // reports the fixture project's runs — and on a wired screen that would
  // be a live-looking strip about a project the reader is not viewing.
  if (WIRED_SCREENS.includes(screen)) return null;
  if (items.length === 0) return null;

  return (
    <div className="flex h-[34px] flex-none items-center gap-3.5 overflow-hidden border-b border-border bg-page px-4">
      <span className="flex-none font-mono text-[11px] tracking-wide text-muted uppercase">
        Happening now
      </span>
      <div className="flex items-center gap-2 overflow-x-auto">
        {items.map((item) => (
          <Link
            key={item.id}
            href={href(item.screen)}
            className="flex h-[22px] flex-none items-center gap-[7px] rounded-pill border border-border bg-card px-2 text-[11px] text-secondary whitespace-nowrap hover:border-border-strong"
          >
            <StatusChip status={item.status as StageStatus} compact />
            <span className="text-primary">{item.label}</span>
            <span className="font-mono text-muted">{item.detail}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}

// ------------------------------------------------------------ state strip

/**
 * Every state each screen must hold, as a link.
 *
 * This is scaffolding, and it is deliberately visible scaffolding: until a
 * sync service exists there is no way to *reach* the offline or parked
 * states from the product, and a state nobody can open is a state nobody
 * reviews. It renders only under `?state=` — the product is not a place to
 * advertise fixtures — and comes out entirely when PowerSync lands.
 */
function StateStrip() {
  const screen = useScreen();
  const pathname = usePathname();
  const [scenario, setScenario] = useScenario();
  const options = SCENARIOS_FOR[screen];

  return (
    <div className="flex h-9 flex-none items-center gap-3 border-t border-border bg-card px-4">
      <span className="font-mono text-[11px] tracking-wide text-muted uppercase">
        Prototype state
      </span>
      <div className="flex items-center gap-1">
        {options.map((option) => (
          <button
            key={option}
            type="button"
            onClick={() => setScenario(option)}
            className={cn(
              "h-[22px] rounded-pill border px-2 text-[11px] whitespace-nowrap",
              option === scenario
                ? "border-accent-border bg-accent-fill text-accent-text"
                : "border-border bg-sunken text-secondary hover:text-primary",
            )}
          >
            {option}
          </button>
        ))}
      </div>
      <span className="text-[11px] text-muted">showing: {SCENARIO_LABEL[scenario]}</span>
      <span className="ml-auto text-[11px] text-muted">
        All eight screens · contracts in docs/screens/
      </span>
      <Link href={pathname} className="text-[11px] text-secondary hover:text-primary">
        Leave the prototype
      </Link>
    </div>
  );
}

// ---------------------------------------------------------------- not wired

/**
 * What an unwired screen shows in the product: that it is unwired.
 *
 * Not an empty state. "No amendments awaiting you" is a claim about the
 * factory, and this screen has not asked the factory anything — so it names
 * the gap instead of drawing either a fixture's rows or a zero it cannot
 * back. The prototype underneath is still one explicit link away, because
 * design review needs it and a link someone chose to open cannot be
 * mistaken for the factory's answer.
 */
function NotWired() {
  const screen = useScreen();
  const pathname = usePathname();
  const project = useQuery((db) => db.project);
  const label = SCREEN_LABEL[screen];

  return (
    <div className="mx-auto max-w-xl px-6 py-16">
      <Card>
        <CardHeader>
          <CardTitle>{label} is not wired to the factory yet</CardTitle>
          <CardDescription>
            This screen&rsquo;s data is not read from the factory yet, so there is nothing here to
            show{project ? ` for ${project.name}` : ""}. It will fill in when the screen is wired to
            the factory&rsquo;s own queries.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex items-center gap-3">
          <Button size="sm" variant="outline" asChild>
            <Link href={ROUTES.projects}>Open Projects</Link>
          </Button>
          <span className="text-2xs text-muted">Projects reads the factory today.</span>
          <Link
            href={`${pathname}?state=settled`}
            className="ml-auto text-2xs text-secondary hover:text-primary"
          >
            View the prototype
          </Link>
        </CardContent>
      </Card>
    </div>
  );
}

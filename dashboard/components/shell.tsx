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

import { cn, StatusChip, type StageStatus } from "@dark-factory/ui";

import {
  LocalDbProvider,
  WIRED_SCREENS,
  useLive,
  useQuery,
  useScenario,
  useScreen,
} from "@/lib/local/store";
import { SCENARIOS_FOR, SCENARIO_LABEL, type Scenario } from "@/lib/local/fixtures";
import type { ScreenName } from "@/lib/local/schema";

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

  const raw = params.get("state");
  const scenario: Scenario = isScenario(raw) ? raw : "settled";

  /**
   * The scenario lives in the URL rather than in component state, so every
   * state on every screen is a link somebody can open, screenshot or file a
   * bug against — which is the difference between a state that is claimed
   * and a state that is shown.
   */
  const setScenario = React.useCallback(
    (next: Scenario) => {
      const search = next === "settled" ? "" : `?state=${next}`;
      router.replace(`${pathname}${search}`, { scroll: false });
    },
    [pathname, router],
  );

  return (
    <LocalDbProvider screen={screen} scenario={scenario} onScenarioChange={setScenario}>
      <div className="flex h-screen min-w-[1364px] flex-col overflow-hidden bg-page">
        <Header />
        <Ticker />
        <div className="min-h-0 flex-1 overflow-auto">{children}</div>
        <StateStrip />
      </div>
    </LocalDbProvider>
  );
}

function isScenario(value: string | null): value is Scenario {
  return value !== null && value in SCENARIO_LABEL;
}

// ------------------------------------------------------------------ header

function Header() {
  const screen = useScreen();
  const { org, project, actor } = useQuery((db) => ({
    org: db.org,
    project: db.project,
    actor: db.actor,
  }));
  const awaiting = useQuery((db) => db.amendments.filter((a) => a.status === "proposed").length);
  const running = useQuery((db) => db.batches.filter((b) => b.status === "running").length);
  const deployable = useQuery((db) => db.batches.filter((b) => b.status === "deployable").length);

  return (
    <header className="flex h-12 flex-none items-center gap-5 border-b border-border bg-card px-4">
      <div className="flex items-center gap-2.5">
        <span className="shrink-0 text-[13px] font-semibold tracking-tight whitespace-nowrap">
          Dark Factory
        </span>
        <Link
          href={ROUTES.projects}
          className="flex h-[26px] shrink-0 items-center gap-1.5 rounded-control border border-border bg-sunken px-2 text-xs text-secondary whitespace-nowrap hover:bg-raised hover:text-primary"
        >
          {project ? `${org} / ${project.name}` : org}
          <span className="text-[10px] text-muted">▾</span>
        </Link>
      </div>

      <nav className="flex items-center gap-0.5">
        <Step n={1} label="Converse" href={ROUTES.conversation} active={screen === "conversation"} />
        <Arrow />
        <Step
          n={2}
          label="Approve"
          href={ROUTES.amendments}
          active={screen === "amendments"}
          badge={awaiting > 0 ? { text: String(awaiting), tone: "accent" } : undefined}
        />
        <Arrow />
        <Step
          n={3}
          label="Execute"
          href={ROUTES.batches}
          active={screen === "batches"}
          badge={running > 0 ? { text: String(running), tone: "running" } : undefined}
        />
        <Arrow />
        <Step
          n={4}
          label="Deploy"
          href={ROUTES.batches}
          badge={deployable > 0 ? { text: String(deployable), tone: "accent-quiet" } : undefined}
        />
      </nav>

      <div className="h-5 w-px bg-border" />

      <nav className="flex items-center gap-3.5 text-[13px]">
        <Secondary label="Projects" href={ROUTES.projects} active={screen === "projects"} />
        <Secondary label="Spec graph" href={ROUTES.specs} active={screen === "specs"} />
        <Secondary label="Team" href={ROUTES.team} active={screen === "team"} />
        <Secondary label="Servers" href={ROUTES.servers} active={screen === "servers"} />
        <Secondary label="Usage" href={ROUTES.usage} active={screen === "usage"} />
      </nav>

      <div className="ml-auto flex items-center gap-2.5">
        <DataSource />
        <SyncBadge />
        <ThemeToggle />
        <span className="inline-flex size-[26px] items-center justify-center rounded-pill border border-border bg-sunken text-[10px] font-semibold text-secondary">
          {actor.initials}
        </span>
      </div>
    </header>
  );
}

/**
 * Where this screen's rows came from.
 *
 * Half the product reads the factory and half still reads fixtures, and a
 * reviewer cannot tell which by looking at a populated table. Saying it in
 * the header is cheaper than being asked, and the chip disappears screen by
 * screen as each one is wired.
 */
function DataSource() {
  const screen = useScreen();
  const { live } = useLive();

  if (!WIRED_SCREENS.includes(screen)) {
    return (
      <span
        title="This screen still renders fixtures. It has not been wired to the factory yet."
        className="rounded-pill border border-border bg-sunken px-2 py-0.5 text-[11px] font-medium text-muted whitespace-nowrap"
      >
        Prototype data
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

  // The ticker reports the fixture project's runs. On a wired screen that
  // would be a live-looking strip about a project the reader is not viewing.
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
            href={ROUTES[item.screen]}
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
 * reviews. It comes out when PowerSync lands.
 */
function StateStrip() {
  const screen = useScreen();
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
    </div>
  );
}

// The factory's REST facade (for the dashboard's gate buttons and read
// views) and its SignalR hub (for live updates) are step 3/4 work — see
// the setup prompt's build order. This file is the one seam the dashboard
// pages call through, so wiring them up later is a change in one place.

export const FACTORY_API_URL = process.env.FACTORY_API_URL ?? "http://localhost:5100";

export type ProjectSummary = {
  id: string;
  name: string;
  activeRunCount: number;
};

export type RunSummary = {
  id: string;
  projectId: string;
  currentStage: string;
  status: string;
  createdAt: string;
};

async function tryFetchJson<T>(path: string): Promise<T | null> {
  try {
    const res = await fetch(`${FACTORY_API_URL}${path}`, { cache: "no-store" });
    if (!res.ok) return null;
    return (await res.json()) as T;
  } catch {
    // The factory API isn't implemented yet (step 3) or isn't reachable
    // from wherever the dashboard is running. Callers render an empty
    // state rather than throwing.
    return null;
  }
}

export function listProjects(): Promise<ProjectSummary[] | null> {
  return tryFetchJson<ProjectSummary[]>("/api/projects");
}

export function listActiveRuns(): Promise<RunSummary[] | null> {
  return tryFetchJson<RunSummary[]>("/api/runs?status=active");
}

export function getProjectRuns(projectId: string): Promise<RunSummary[] | null> {
  return tryFetchJson<RunSummary[]>(`/api/projects/${projectId}/runs`);
}

export function getRun(runId: string): Promise<RunSummary | null> {
  return tryFetchJson<RunSummary>(`/api/runs/${runId}`);
}

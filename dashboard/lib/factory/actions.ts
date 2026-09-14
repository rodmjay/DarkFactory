"use server";

/**
 * The write and read surface the browser is allowed to reach.
 *
 * Server actions rather than route handlers: the client calls a typed
 * function, and the factory's endpoint, its MCP framing and its errors all
 * stay on this side of the boundary. `lib/factory/mcp.ts` imports
 * `server-only` so a mistaken client import fails to build rather than
 * shipping the factory's internal address to a browser.
 *
 * Nothing here throws. A failure the user can do something about — an
 * unreachable workspace server, a URL that does not conform — is a value
 * the screen renders, not an exception that replaces the page with an
 * error boundary.
 */

import {
  FactoryError,
  checkServer,
  intakeDrift,
  listIntakes,
  listServers,
  previewCorpus,
  pullIntake,
  registerServer,
  type CorpusArea,
  type CorpusDrift,
  type FactoryIntake,
  type FactoryServer,
  approveAmendment,
  getConversation,
  listConversations,
  listProjects,
  registerProject,
  rejectAmendment,
  sendTurn,
  startConversation,
  type FactoryConversation,
  type FactoryConversationDetail,
  type FactoryProject,
} from "./mcp";

export type Result<T> = { ok: true; data: T } | { ok: false; error: string };

async function attempt<T>(run: () => Promise<T>): Promise<Result<T>> {
  try {
    return { ok: true, data: await run() };
  } catch (cause) {
    if (cause instanceof FactoryError) return { ok: false, error: cause.message };
    return { ok: false, error: "Something went wrong talking to the factory." };
  }
}

export async function loadProjects(): Promise<Result<FactoryProject[]>> {
  return attempt(() => listProjects());
}

/**
 * Register a project against a workspace MCP server.
 *
 * The URL is the only thing the user supplies that the factory cannot
 * derive, and validating its shape here means an obvious typo is answered
 * instantly instead of after a 30-second handshake timeout.
 */
export async function registerProjectAction(
  workspaceMcpUrl: string,
  name?: string,
): Promise<Result<FactoryProject>> {
  const url = workspaceMcpUrl.trim();

  if (url === "") {
    return { ok: false, error: "A workspace server URL is required." };
  }

  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return { ok: false, error: `“${url}” is not a URL.` };
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    return { ok: false, error: "A workspace server URL must be http or https." };
  }

  return attempt(() => registerProject(url, name?.trim() || undefined));
}

// ----------------------------------------------------------- conversations

export async function loadConversations(projectId: string): Promise<Result<FactoryConversation[]>> {
  return attempt(() => listConversations(projectId));
}

export async function loadConversation(conversationId: string): Promise<Result<FactoryConversationDetail>> {
  return attempt(() => getConversation(conversationId));
}

export async function startConversationAction(
  projectId: string,
  title?: string,
): Promise<Result<{ id: string }>> {
  return attempt(() => startConversation(projectId, title?.trim() || undefined));
}

export async function sendTurnAction(conversationId: string, message: string): Promise<Result<void>> {
  const text = message.trim();
  if (text === "") return { ok: false, error: "A message cannot be empty." };
  return attempt(() => sendTurn(conversationId, text));
}

export async function approveAction(amendmentId: string): Promise<Result<void>> {
  return attempt(() => approveAmendment(amendmentId));
}

// ------------------------------------------------------- servers and imports

/**
 * The project's servers and imports, in one read. A server belongs to the
 * project when it was registered for it, or when it is the workspace the
 * project is bound to by URL.
 */
export async function loadSources(
  projectId: string,
  workspaceUrl: string | null,
): Promise<Result<{ servers: FactoryServer[]; intakes: FactoryIntake[] }>> {
  return attempt(async () => {
    const [servers, intakes] = await Promise.all([listServers(), listIntakes(projectId)]);
    return {
      servers: servers.filter((s) => s.project_id === projectId || (workspaceUrl !== null && s.url === workspaceUrl)),
      intakes,
    };
  });
}

/** Same shape check as registering a project: an obvious typo is answered now, not after a handshake times out. */
export async function connectServerAction(url: string, projectId: string): Promise<Result<FactoryServer>> {
  const trimmed = url.trim();
  if (trimmed === "") return { ok: false, error: "The server's MCP URL is required." };
  try {
    const parsed = new URL(trimmed);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
      return { ok: false, error: "An MCP server URL must be http or https." };
    }
  } catch {
    return { ok: false, error: `“${trimmed}” is not a URL.` };
  }
  return attempt(async () => (await registerServer(trimmed, projectId)).server);
}

export async function checkServerAction(serverId: string): Promise<Result<{ outcome: string; server: FactoryServer }>> {
  return attempt(() => checkServer(serverId));
}

export async function previewCorpusAction(serverId: string): Promise<Result<CorpusArea[]>> {
  return attempt(() => previewCorpus(serverId));
}

export async function ingestSpecsAction(
  projectId: string,
  serverId: string,
  area: string,
): Promise<Result<{ intake_id: string; name: string; documents: number }>> {
  return attempt(async () => {
    const pulled = await pullIntake(projectId, serverId, area);
    return { intake_id: pulled.intake_id, name: pulled.name, documents: pulled.sources.length };
  });
}

export async function driftAction(intakeId: string): Promise<Result<CorpusDrift>> {
  return attempt(() => intakeDrift(intakeId));
}

/** The factory records the reason with the rejection, so an empty one is refused here first. */
export async function rejectAction(amendmentId: string, reason: string): Promise<Result<void>> {
  if (reason.trim() === "") return { ok: false, error: "Say why it is being rejected." };
  return attempt(() => rejectAmendment(amendmentId, reason.trim()));
}

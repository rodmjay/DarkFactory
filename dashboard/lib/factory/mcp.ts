import "server-only";

/**
 * The dashboard's client for the factory's own MCP surface.
 *
 * The factory speaks MCP and almost nothing else — `/health`, artifacts and
 * webhooks are the only REST it has — so `df.*` tools are the API, and this
 * is the one place that knows how to call them.
 *
 * It runs on the server. The browser never reaches the factory directly:
 * the endpoint is an internal Compose address (`http://factory:8080`), and
 * putting it in front of a browser would mean CORS, a published port, and
 * the factory trusting whatever a page sent it.
 *
 * The transport is streamable HTTP in its stateless form. The factory
 * returns no `mcp-session-id`, so there is no session to establish or keep
 * alive and a tool call is one POST. That is worth stating because the
 * stateful form of this protocol is much more work, and a future factory
 * that starts issuing session ids will break here rather than silently.
 */

const ENDPOINT = `${process.env.FACTORY_API_URL ?? "http://localhost:5100"}/mcp`;

/** Registration runs a handshake and a conformance check against someone
 *  else's server, so it is not a fast call. */
const TIMEOUT_MS = 30_000;

export class FactoryError extends Error {
  constructor(
    message: string,
    readonly tool: string,
  ) {
    super(message);
    this.name = "FactoryError";
  }
}

interface JsonRpcResponse {
  result?: {
    content?: { type: string; text?: string }[];
    isError?: boolean;
  };
  error?: { code: number; message: string };
}

/**
 * Call one `df.*` tool and return its parsed result.
 *
 * Tool results arrive as MCP content blocks whose text is itself JSON — the
 * shape the C# record serialized to. Both hops are unwrapped here so no
 * caller has to know the envelope exists.
 */
export async function callTool<T>(tool: string, args: Record<string, unknown> = {}): Promise<T> {
  const body = JSON.stringify({
    jsonrpc: "2.0",
    id: 1,
    method: "tools/call",
    params: { name: tool, arguments: args },
  });

  let response: Response;
  try {
    response = await fetch(ENDPOINT, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        // Streamable HTTP may answer as either, and the factory chooses SSE.
        Accept: "application/json, text/event-stream",
      },
      body,
      cache: "no-store",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch (cause) {
    throw new FactoryError(
      cause instanceof Error && cause.name === "TimeoutError"
        ? `The factory did not answer ${tool} within ${TIMEOUT_MS / 1000}s.`
        : "The factory is not reachable.",
      tool,
    );
  }

  if (!response.ok) {
    throw new FactoryError(`The factory returned HTTP ${response.status}.`, tool);
  }

  const payload = parse(await response.text());

  if (payload.error) throw new FactoryError(payload.error.message, tool);

  const content = payload.result?.content ?? [];
  const text = content.map((block) => block.text ?? "").join("");

  // An MCP tool reports a domain failure as a result with `isError`, not as
  // a transport error. Collapsing the two would turn "that URL is not a
  // conformant workspace server" into "the factory is down".
  if (payload.result?.isError) {
    throw new FactoryError(clean(text) || "The tool reported an error.", tool);
  }

  if (text.trim() === "") return undefined as T;

  try {
    return JSON.parse(text) as T;
  } catch {
    // Not every tool returns JSON; a plain string is a legitimate result.
    return text as T;
  }
}

/**
 * Drop the MCP wrapper the server puts in front of a tool's own words.
 *
 * The factory's message — "df.describe is mandatory; a server that cannot
 * answer it is not registered" — is the useful half, and "An error occurred
 * invoking 'df.projects.register'" in front of it is machinery the reader
 * did not ask about.
 */
function clean(message: string): string {
  return message.replace(/^An error occurred invoking '[^']*':\s*/, "").trim();
}

/**
 * Pull the JSON-RPC message out of whichever framing came back.
 *
 * SSE arrives as `event: message` / `data: {…}` lines; the same server may
 * answer a different call as plain JSON. Handling both here means the rest
 * of the file never thinks about framing.
 */
function parse(raw: string): JsonRpcResponse {
  const trimmed = raw.trim();
  if (trimmed.startsWith("{")) return JSON.parse(trimmed) as JsonRpcResponse;

  const data = trimmed
    .split("\n")
    .filter((line) => line.startsWith("data:"))
    .map((line) => line.slice(5).trim())
    .join("");

  if (!data) throw new Error("The factory returned a response with no JSON-RPC payload.");
  return JSON.parse(data) as JsonRpcResponse;
}

// ---------------------------------------------------------------- projects

/**
 * `ProjectSummary` as `df.projects.*` serializes it.
 *
 * snake_case, because that is the wire convention the schemas under
 * `contracts/schemas/` use throughout — see DESIGN.md § Wire shapes, which
 * records the time the envelope briefly serialized camelCase by inheriting
 * the SDK's default handling of C# records. This client was written against
 * a build doing exactly that and broke silently when the factory went back
 * to the documented shape: the fields simply read `undefined`, so the
 * roster still rendered and just quietly lost its stack hints.
 *
 * `normalizeProject` therefore accepts either spelling rather than trusting
 * one. A screen that loses a column without erroring is the worst way to
 * find out a serializer moved.
 */
export interface FactoryProject {
  id: string;
  name: string;
  workspace_mcp_url: string;
  stack_hints: string[];
  team_id?: string | null;
}

interface FactoryProjectWire {
  id: string;
  name: string;
  workspace_mcp_url?: string;
  workspaceMcpUrl?: string;
  stack_hints?: string[];
  stackHints?: string[];
  team_id?: string | null;
  teamId?: string | null;
}

function normalizeProject(wire: FactoryProjectWire): FactoryProject {
  return {
    id: wire.id,
    name: wire.name,
    workspace_mcp_url: wire.workspace_mcp_url ?? wire.workspaceMcpUrl ?? "",
    stack_hints: wire.stack_hints ?? wire.stackHints ?? [],
    team_id: wire.team_id ?? wire.teamId ?? null,
  };
}

export async function listProjects(): Promise<FactoryProject[]> {
  const wire = await callTool<FactoryProjectWire[]>("df.projects.list");
  return (wire ?? []).map(normalizeProject);
}

/**
 * Register a project against a workspace server.
 *
 * The factory does the work that matters here: it runs `df.describe`
 * against the URL, exercises every declared capability once, derives a
 * name, and seeds the project's default team. The dashboard's whole job is
 * to collect one URL and show what came back.
 */
export async function registerProject(
  workspaceMcpUrl: string,
  name?: string,
): Promise<FactoryProject> {
  const wire = await callTool<FactoryProjectWire>("df.projects.register", {
    workspace_mcp_url: workspaceMcpUrl,
    ...(name ? { name } : {}),
  });
  return normalizeProject(wire);
}

/** `TeamMemberSummary` from `df.projects.team`. */
export interface FactoryTeamMember {
  role: string;
  deployment: string;
  fallback?: string | null;
  tokenBudget?: number | null;
  maxOutputTokens?: number | null;
}

export function projectTeam(projectId: string): Promise<FactoryTeamMember[]> {
  return callTool<FactoryTeamMember[]>("df.projects.team", { project_id: projectId });
}

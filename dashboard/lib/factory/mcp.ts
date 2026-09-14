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

import http from "node:http";
import https from "node:https";

import type { Payload } from "@dark-factory/ui";

const ENDPOINT = `${process.env.FACTORY_API_URL ?? "http://localhost:5100"}/mcp`;

/** Registration runs a handshake and a conformance check against someone
 *  else's server, so it is not a fast call. */
const TIMEOUT_MS = 30_000;

/**
 * An architect turn is a model call that may emit a whole spec diff, and
 * the factory gives the gateway fifteen minutes (`AnthropicOptions.Timeout`).
 * Giving up sooner here would abandon a reply the factory is still going to
 * save — the next read would show it, and the reader would have been told
 * it failed.
 */
export const TURN_TIMEOUT_MS = 15 * 60_000;

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
export async function callTool<T>(
  tool: string,
  args: Record<string, unknown> = {},
  { timeoutMs = TIMEOUT_MS }: { timeoutMs?: number } = {},
): Promise<T> {
  const body = JSON.stringify({
    jsonrpc: "2.0",
    id: 1,
    method: "tools/call",
    params: { name: tool, arguments: args },
  });

  let response: { status: number; text: string };
  try {
    response = await post(body, timeoutMs);
  } catch (cause) {
    throw new FactoryError(
      cause instanceof Error && cause.name === "TimeoutError"
        ? `The factory did not answer ${tool} within ${Math.round(timeoutMs / 1000)}s.`
        : "The factory is not reachable.",
      tool,
    );
  }

  if (response.status < 200 || response.status >= 300) {
    throw new FactoryError(`The factory returned HTTP ${response.status}.`, tool);
  }

  const payload = parse(response.text);

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
 * One POST, over `node:http` rather than `fetch`.
 *
 * Node's `fetch` gives up on a response whose headers take longer than five
 * minutes, whatever abort signal it is handed, and an architect turn can
 * take longer than that before the factory answers. That exact limit is
 * what lost a turn in an earlier seeding session (docs/handoffs/0006). The
 * timeout here is the only one, and it is the caller's.
 */
function post(body: string, timeoutMs: number): Promise<{ status: number; text: string }> {
  const url = new URL(ENDPOINT);
  const client = url.protocol === "https:" ? https : http;

  return new Promise((resolve, reject) => {
    const request = client.request(
      url,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          // Streamable HTTP may answer as either, and the factory chooses SSE.
          Accept: "application/json, text/event-stream",
          "Content-Length": Buffer.byteLength(body),
        },
      },
      (response) => {
        let text = "";
        response.setEncoding("utf8");
        response.on("data", (chunk: string) => (text += chunk));
        response.on("end", () => resolve({ status: response.statusCode ?? 0, text }));
        response.on("error", reject);
      },
    );

    request.setTimeout(timeoutMs, () => {
      const timeout = new Error(`No answer within ${timeoutMs}ms.`);
      timeout.name = "TimeoutError";
      request.destroy(timeout);
    });
    request.on("error", reject);
    request.end(body);
  });
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

// ----------------------------------------------------------- conversations

/** `ConversationListItem` from `df.conversations.list` and `.get`. */
export interface FactoryConversation {
  id: string;
  project_id: string;
  title: string | null;
  status: string;
  /** `intake` for the thread an import files its proposals under (ADR-0037). */
  kind: "conversation" | "intake";
  turn_count: number;
  amendments: number;
  awaiting_amendments: number;
  created_at: string;
  updated_at: string;
  /** The deployment the project's architect answers on; null with no team. */
  deployment?: string | null;
}

/** `TurnView`: a turn's payloads exactly as the factory stored them. */
export interface FactoryTurn {
  id: string;
  seq: number;
  role: "user" | "assistant" | "system";
  content: string;
  /** Null on a human turn — its content is the whole of it. */
  payloads?: Payload[] | null;
  tokens?: number | null;
  at: string;
}

export interface FactoryAmendmentState {
  id: string;
  status: "proposed" | "approved" | "rejected";
  turn_id?: string | null;
  created_at: string;
  rejected_reason?: string | null;
}

export interface FactoryConversationDetail {
  conversation: FactoryConversation;
  turns: FactoryTurn[];
  amendments: FactoryAmendmentState[];
}

export async function listConversations(projectId: string): Promise<FactoryConversation[]> {
  return (await callTool<FactoryConversation[]>("df.conversations.list", { project_id: projectId })) ?? [];
}

export function getConversation(conversationId: string): Promise<FactoryConversationDetail> {
  return callTool<FactoryConversationDetail>("df.conversations.get", { conversation_id: conversationId });
}

export function startConversation(
  projectId: string,
  title?: string,
): Promise<{ id: string; project_id: string; title: string | null }> {
  return callTool("df.conversations.start", { project_id: projectId, ...(title ? { title } : {}) });
}

/**
 * One turn. The result is not used: the factory has persisted both turns
 * (and any amendment) by the time this returns, and the thread is re-read
 * from there so what the reader sees is what was saved.
 */
export async function sendTurn(conversationId: string, message: string): Promise<void> {
  await callTool(
    "df.conversations.turn",
    { conversation_id: conversationId, message },
    { timeoutMs: TURN_TIMEOUT_MS },
  );
}

export async function approveAmendment(amendmentId: string): Promise<void> {
  await callTool("df.specs.approve", { amendment_id: amendmentId });
}

export async function rejectAmendment(amendmentId: string, reason: string): Promise<void> {
  await callTool("df.specs.reject", { amendment_id: amendmentId, reason });
}

// ------------------------------------------------------- servers and imports

/** `ServerSummary` from `df.servers.*`, with the health monitor's fields (ADR-0038). */
export interface FactoryServer {
  id: string;
  url: string;
  name: string;
  domain: string;
  status: string;
  project_id?: string | null;
  /** What the server said it serves — a project or a repo; null when shared or unscoped. */
  scope?: string | null;
  last_seen_at?: string | null;
  unreachable_since?: string | null;
  last_error?: string | null;
}

export async function listServers(): Promise<FactoryServer[]> {
  return (await callTool<FactoryServer[]>("df.servers.list")) ?? [];
}

/** Runs the handshake and every conformance probe, so it is given a minute. */
export function registerServer(
  url: string,
  projectId: string,
): Promise<{ server: FactoryServer; conformance: { capability: string; status: string; detail?: string | null }[] }> {
  return callTool("df.servers.register", { url, project_id: projectId }, { timeoutMs: 60_000 });
}

export function checkServer(serverId: string): Promise<{ outcome: string; server: FactoryServer }> {
  return callTool("df.servers.check", { server_id: serverId }, { timeoutMs: 60_000 });
}

/** `IntakeListItem` from `df.intake.list`. */
export interface FactoryIntake {
  intake_id: string;
  name: string;
  source_server_id?: string | null;
  conversation_id: string;
  documents: number;
  extracted: number;
  proposed: number;
  open_questions: number;
}

export async function listIntakes(projectId: string): Promise<FactoryIntake[]> {
  return (await callTool<FactoryIntake[]>("df.intake.list", { project_id: projectId })) ?? [];
}

export interface CorpusArea {
  area: string;
  documents: number;
  retired: number;
}

export async function previewCorpus(serverId: string): Promise<CorpusArea[]> {
  return (await callTool<CorpusArea[]>("df.intake.preview", { server_id: serverId }, { timeoutMs: 60_000 })) ?? [];
}

/** Fetches and hash-checks every document in the area: one call per document, so minutes, not seconds. */
export function pullIntake(
  projectId: string,
  serverId: string,
  area: string,
): Promise<{ intake_id: string; name: string; sources: unknown[] }> {
  return callTool("df.intake.pull", { project_id: projectId, server_id: serverId, area }, { timeoutMs: 5 * 60_000 });
}

export interface CorpusDrift {
  intake_id: string;
  server_name: string;
  changed: { origin_id: string }[];
  added: string[];
  removed: string[];
}

/** `IntakeSourceRow` from `df.intake.status`. */
export interface FactoryIntakeSourceRow {
  source_id: string;
  seq: number;
  source_ref: string;
  title: string;
  status: string;
  draft_revision: number;
  nodes: number;
  open_questions: number;
  answered_questions: number;
  deferred_questions: number;
  resolved_questions: number;
  amendment_id?: string | null;
  failure?: string | null;
}

export interface FactoryIntakeStatus {
  intake_id: string;
  name: string;
  sources: FactoryIntakeSourceRow[];
}

export function getIntakeStatus(intakeId: string): Promise<FactoryIntakeStatus> {
  return callTool("df.intake.status", { intake_id: intakeId });
}

/** `SpecNodeSummary` from `df.specs.query`: a node in the graph proper. */
export interface FactorySpecNode {
  spec_id: string;
  kind: string;
  layer: string;
  text?: string | null;
  retired: boolean;
}

export async function querySpecNodes(projectId: string): Promise<FactorySpecNode[]> {
  return (await callTool<FactorySpecNode[]>("df.specs.query", { project_id: projectId, limit: 1000 })) ?? [];
}

export interface FactoryIntakeQuestion {
  question_id: string;
  kind: string;
  status: string;
  question: string;
  quote?: string | null;
  answer?: string | null;
  affects: number[];
}

/** `IntakeSourceView` from `df.intake.source`: one document's text, draft and questions. */
export interface FactoryIntakeSource {
  source_id: string;
  intake_id: string;
  seq: number;
  source_ref: string;
  title: string;
  status: string;
  draft_revision: number;
  content: string;
  draft?: {
    creates: { kind: string; layer: string; text: string; rationale?: string | null }[];
    edge_adds: { from_spec_id: string; to_spec_id: string; kind: string }[];
  } | null;
  questions: FactoryIntakeQuestion[];
  failure?: string | null;
  amendment_id?: string | null;
}

export function getIntakeSource(sourceId: string): Promise<FactoryIntakeSource> {
  return callTool("df.intake.source", { source_id: sourceId });
}

export interface IntakeRefresh {
  intake_id: string;
  updated: number;
  added: number;
  removed: number;
  changed_after_extraction: string[];
}

/** Re-fetches every edited document, so it is given minutes like a pull. */
export function refreshIntake(intakeId: string): Promise<IntakeRefresh> {
  return callTool("df.intake.refresh", { intake_id: intakeId }, { timeoutMs: 5 * 60_000 });
}

export function intakeDrift(intakeId: string): Promise<CorpusDrift> {
  return callTool("df.intake.drift", { intake_id: intakeId }, { timeoutMs: 60_000 });
}

#!/usr/bin/env node
import { resolve } from "node:path";
import express from "express";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { z } from "zod";
import { describeServer } from "./describe.js";
import { echoEnvelope, envelopeSchema } from "./envelope.js";
import { listFiles, readManyFiles, searchFiles, writeManyFiles } from "./files.js";
import { runCommand } from "./exec.js";
import { applyPatch, branch, commit, openPr, push } from "./vcs.js";

function parseArgs(argv: string[]) {
  let httpPort: number | undefined;
  let root = process.cwd();

  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--http") {
      httpPort = Number(argv[++i]);
    } else if (argv[i] === "--root") {
      root = resolve(argv[++i]);
    }
  }

  return { httpPort, root };
}

function json(value: unknown) {
  return { content: [{ type: "text" as const, text: JSON.stringify(value) }] };
}

function buildServer(root: string): McpServer {
  const server = new McpServer({ name: "dark-factory-workspace-mcp", version: "0.2.0" });

  // The mandatory handshake (ADR-0018, docs/conventions/describe.md).
  // Unlike every other tool here it does NOT echo the envelope back: the
  // published schema is closed, and an extra property would fail
  // registration against the factory.
  server.registerTool(
    "df.describe",
    { description: "The Dark Factory handshake: convention version, capabilities, domain, requires and effective config.", inputSchema: { envelope: envelopeSchema } },
    async () => json(describeServer(root)),
  );

  server.registerTool(
    "df.files.list",
    {
      description: "List files matching one or more globs.",
      inputSchema: { globs: z.array(z.string()), envelope: envelopeSchema },
    },
    async ({ globs, envelope }) => json({ ...echoEnvelope(envelope), paths: await listFiles(root, globs) }),
  );

  server.registerTool(
    "df.files.read_many",
    {
      description: "Read the contents of multiple files in one call.",
      inputSchema: { paths: z.array(z.string()), envelope: envelopeSchema },
    },
    async ({ paths, envelope }) =>
      json({ ...echoEnvelope(envelope), files: await readManyFiles(root, paths) }),
  );

  server.registerTool(
    "df.files.search",
    {
      description: "Search file contents for a query string.",
      inputSchema: { query: z.string(), globs: z.array(z.string()).optional(), envelope: envelopeSchema },
    },
    async ({ query, globs, envelope }) =>
      json({ ...echoEnvelope(envelope), matches: await searchFiles(root, query, globs) }),
  );

  server.registerTool(
    "df.files.write_many",
    {
      description: "Write multiple files in one call.",
      inputSchema: {
        files: z.array(z.object({ path: z.string(), content: z.string() })),
        envelope: envelopeSchema,
      },
    },
    async ({ files, envelope }) =>
      json({ ...echoEnvelope(envelope), written: await writeManyFiles(root, files) }),
  );

  server.registerTool(
    "df.exec.run",
    {
      description: "Run a shell command in the workspace.",
      inputSchema: {
        command: z.string(),
        cwd: z.string().optional(),
        timeout: z.number().default(30_000),
        envelope: envelopeSchema,
      },
    },
    async ({ command, cwd, timeout, envelope }) =>
      json({ ...echoEnvelope(envelope), ...(await runCommand(command, cwd ? resolve(root, cwd) : root, timeout)) }),
  );

  server.registerTool(
    "df.vcs.branch",
    { description: "Create or switch to a branch.", inputSchema: { name: z.string(), envelope: envelopeSchema } },
    async ({ name, envelope }) => json({ ...echoEnvelope(envelope), ...branch(root, name) }),
  );

  server.registerTool(
    "df.vcs.apply_patch",
    {
      description: "Fetch a patch from the URL the factory minted and apply it, returning the files it touched.",
      inputSchema: { patch_ref: z.string(), envelope: envelopeSchema },
    },
    async ({ patch_ref, envelope }) => json({ ...echoEnvelope(envelope), ...(await applyPatch(root, patch_ref)) }),
  );

  server.registerTool(
    "df.vcs.commit",
    { description: "Commit staged changes.", inputSchema: { message: z.string(), envelope: envelopeSchema } },
    async ({ message, envelope }) => json({ ...echoEnvelope(envelope), ...commit(root, message) }),
  );

  server.registerTool(
    "df.vcs.push",
    { description: "Push the current branch.", inputSchema: { envelope: envelopeSchema } },
    async ({ envelope }) => json({ ...echoEnvelope(envelope), ...push(root) }),
  );

  server.registerTool(
    "df.vcs.open_pr",
    {
      description: "Open a pull request for the current branch.",
      inputSchema: { title: z.string(), body: z.string(), envelope: envelopeSchema },
    },
    async ({ title, body, envelope }) => json({ ...echoEnvelope(envelope), ...openPr(root, title, body) }),
  );

  return server;
}

async function main() {
  const { httpPort, root } = parseArgs(process.argv.slice(2));

  if (httpPort) {
    const app = express();
    app.use(express.json());

    app.post("/mcp", async (req, res) => {
      // Stateless: a fresh server + transport per request keeps the
      // reference implementation simple. A stateful session (reused
      // transport, `sessionIdGenerator`) is a step-3 optimization once real
      // multi-call workflows need it.
      const server = buildServer(root);
      const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
      res.on("close", () => {
        transport.close();
        server.close();
      });
      await server.connect(transport);
      await transport.handleRequest(req, res, req.body);
    });

    app.listen(httpPort, () => {
      console.error(`dark-factory-workspace-mcp listening on http://localhost:${httpPort}/mcp (root=${root})`);
    });
  } else {
    const server = buildServer(root);
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error(`dark-factory-workspace-mcp connected over stdio (root=${root})`);
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});

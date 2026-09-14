#!/usr/bin/env node
// Drives corpus intake (docs/adr/0037) against a running factory.
//
//   node scripts/intake.mjs start --project <id> --name <name> --dir <path> [--ref-prefix <repo>]
//   node scripts/intake.mjs extract --intake <id> [--source <id>] [--limit N]
//   node scripts/intake.mjs status --intake <id>
//   node scripts/intake.mjs questions --intake <id> [--status open]
//
// Dependency-free on purpose, and on node:http rather than fetch: an
// extraction can outlast undici's five-minute headers timeout, which is
// exactly how an earlier seeding session lost a turn.
//
// `start` skips documents whose front matter says `status: superseded` —
// their replacement carries what is still true, and importing both would
// hand the extractor two answers to one question.

import { readdirSync, readFileSync } from "node:fs";
import { basename, join, relative, resolve } from "node:path";
import http from "node:http";

const FACTORY = new URL(process.env.FACTORY_MCP_URL ?? "http://localhost:15100/mcp");

function args(argv) {
  const out = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i].startsWith("--")) out[argv[i].slice(2)] = argv[i + 1]?.startsWith("--") ? true : argv[++i];
    else out._.push(argv[i]);
  }
  return out;
}

function call(name, argumentsObject) {
  const body = JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name, arguments: argumentsObject } });
  return new Promise((ok, fail) => {
    const req = http.request(
      FACTORY,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json, text/event-stream",
          "Content-Length": Buffer.byteLength(body),
        },
      },
      (res) => {
        let text = "";
        res.setEncoding("utf8");
        res.on("data", (chunk) => (text += chunk));
        res.on("end", () => {
          // Streamable HTTP answers either as JSON or as one SSE event.
          const payload = text.trimStart().startsWith("{")
            ? text
            : text.split("\n").filter((l) => l.startsWith("data: ")).map((l) => l.slice(6)).join("");
          try {
            const message = JSON.parse(payload);
            if (message.error) return fail(new Error(`${name}: ${message.error.message}`));
            const result = message.result;
            const content = result.content?.[0]?.text ?? "";
            if (result.isError) return fail(new Error(`${name}: ${content}`));
            ok(result.structuredContent ?? JSON.parse(content));
          } catch (error) {
            fail(new Error(`${name}: unreadable response (HTTP ${res.statusCode}): ${text.slice(0, 500)}`));
          }
        });
      },
    );
    req.setTimeout(0);
    req.on("error", fail);
    req.end(body);
  });
}

function frontMatter(text) {
  const match = /^---\n([\s\S]*?)\n---/.exec(text);
  const fields = {};
  for (const line of match?.[1].split("\n") ?? []) {
    const kv = /^([a-z_]+):\s*(.*)$/.exec(line);
    if (kv) fields[kv[1]] = kv[2].replace(/\s+#.*$/, "").trim();
  }
  return fields;
}

function titleOf(text, file) {
  return /^# (.+)$/m.exec(text)?.[1].trim() ?? basename(file, ".md");
}

async function start(opts) {
  const dir = resolve(opts.dir);
  const prefix = opts["ref-prefix"] ?? basename(dir);
  const root = opts.root ? resolve(opts.root) : dir;
  const sources = [];
  const skipped = [];
  for (const file of readdirSync(dir).filter((f) => f.endsWith(".md")).sort()) {
    const content = readFileSync(join(dir, file), "utf8");
    const ref = `${prefix}:${relative(root, join(dir, file))}`;
    if (frontMatter(content).status === "superseded") {
      skipped.push(ref);
      continue;
    }
    sources.push({ source_ref: ref, title: titleOf(content, file), content });
  }
  for (const ref of skipped) console.error(`skipped (superseded): ${ref}`);
  const summary = await call("df.intake.start", { project_id: opts.project, name: opts.name, sources });
  console.log(JSON.stringify({ intake_id: summary.intake_id, sources: summary.sources.length }, null, 2));
}

async function status(opts) {
  const summary = await call("df.intake.status", { intake_id: opts.intake });
  for (const s of summary.sources) {
    console.log(
      [String(s.seq).padStart(2), s.status.padEnd(9), `r${s.draft_revision}`, `${s.nodes} nodes`.padEnd(9),
        `${s.open_questions} open`.padEnd(8), s.source_ref].join("  "),
    );
  }
  return summary;
}

async function extract(opts) {
  let targets;
  if (opts.source) targets = [opts.source];
  else {
    const summary = await call("df.intake.status", { intake_id: opts.intake });
    targets = summary.sources.filter((s) => s.status === "pending" || s.status === "failed").map((s) => s.source_id);
  }
  if (opts.limit) targets = targets.slice(0, Number(opts.limit));
  for (const id of targets) {
    const began = Date.now();
    try {
      const r = await call("df.intake.extract", { source_id: id });
      const seconds = ((Date.now() - began) / 1000).toFixed(0);
      console.log(
        `${r.accepted ? "ok  " : "FAIL"} ${r.source_ref}  r${r.draft_revision}  ${r.draft?.creates.length ?? 0} nodes  ` +
          `${r.open_questions.length} open  ${r.tokens_used} tok  ${seconds}s`,
      );
      for (const e of r.errors) console.log(`       ${e}`);
    } catch (error) {
      console.log(`ERR  ${id}  ${error.message}`);
    }
  }
}

async function questions(opts) {
  const rows = await call("df.intake.questions", { intake_id: opts.intake, ...(opts.status ? { status: opts.status } : {}) });
  console.log(JSON.stringify(rows, null, 2));
}

const opts = args(process.argv.slice(2));
const verb = { start, extract, status, questions }[opts._[0]];
if (!verb) {
  console.error("usage: intake.mjs start|extract|status|questions [--options]  (see header)");
  process.exit(2);
}
verb(opts).catch((error) => {
  console.error(error.message);
  process.exit(1);
});

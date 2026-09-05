import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";

// The convention version this server speaks. v0.2 moved every convention
// tool under the `df.` root and made df.describe mandatory
// (docs/conventions/describe.md, ADR-0018).
export const CONVENTION_VERSION = "0.2.0";

// Every df.* tool this server registers. Kept next to the registrations in
// index.ts so "what we implement" and "what we claim to implement" cannot
// drift apart — the factory would catch the drift at registration and mark
// us degraded, which is a slow way to find a typo.
export const CAPABILITIES = [
  "df.describe",
  "df.files.list",
  "df.files.read_many",
  "df.files.search",
  "df.files.write_many",
  "df.exec.run",
  "df.vcs.branch",
  "df.vcs.apply_patch",
  "df.vcs.commit",
  "df.vcs.push",
  "df.vcs.open_pr",
] as const;

// df.describe() — the mandatory handshake. Validated by the factory against
// contracts/schemas/describe.schema.json before anything is stored, so the
// shape here is a contract, not a convenience. Note in particular that this
// response does NOT echo the envelope the way every other tool does: the
// schema is closed, and an extra property fails registration.
export function describeServer(root: string) {
  return {
    name: "dark-factory-workspace-mcp",
    convention_version: CONVENTION_VERSION,
    domain: "workspace",
    capabilities: [...CAPABILITIES],
    requires: [],
    // What this instance is pointed at. Never secrets — the factory stores
    // this and the dashboard renders it.
    effective_config: describeWorkspace(root),
  };
}

// The v0.1 `workspace.describe` payload, now the body of effective_config.
// Still used by projects.register to derive a project's name and stack
// hints (ADR-0013).
export function describeWorkspace(root: string) {
  return {
    name: deriveName(root),
    root,
    vcs: { provider: "git", default_branch: defaultBranch(root) },
    stack_hints: stackHints(root),
  };
}

function deriveName(root: string): string {
  const packageJsonPath = join(root, "package.json");
  if (existsSync(packageJsonPath)) {
    try {
      const pkg = JSON.parse(readFileSync(packageJsonPath, "utf-8"));
      if (typeof pkg.name === "string" && pkg.name.length > 0) {
        return pkg.name;
      }
    } catch {
      // fall through to directory name
    }
  }
  return basename(root);
}

function defaultBranch(root: string): string {
  try {
    return execFileSync("git", ["symbolic-ref", "--short", "HEAD"], {
      cwd: root,
      encoding: "utf-8",
    }).trim();
  } catch {
    return "main";
  }
}

const STACK_MARKERS: Record<string, string> = {
  "package.json": "node",
  "tsconfig.json": "typescript",
  "next.config.ts": "nextjs",
  "next.config.js": "nextjs",
  "DarkFactory.sln": "dotnet",
  "requirements.txt": "python",
  "pyproject.toml": "python",
  "go.mod": "go",
  "Cargo.toml": "rust",
};

function stackHints(root: string): string[] {
  return Object.entries(STACK_MARKERS)
    .filter(([marker]) => existsSync(join(root, marker)))
    .map(([, hint]) => hint)
    .filter((hint, index, all) => all.indexOf(hint) === index);
}

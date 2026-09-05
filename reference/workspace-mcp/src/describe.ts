import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";

// Implements workspace.describe() (docs/conventions/workspace.md), used by
// projects.register (docs/adr/0013-automatic-project-naming.md) to derive a
// project's name and to seed its stack hints.
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

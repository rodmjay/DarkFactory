import { execFileSync } from "node:child_process";

// Implements vcs.* (docs/conventions/workspace.md).

function git(root: string, args: string[]): string {
  return execFileSync("git", args, { cwd: root, encoding: "utf-8" }).trim();
}

export function branch(root: string, name: string) {
  try {
    git(root, ["checkout", "-b", name]);
  } catch {
    git(root, ["checkout", name]); // branch already exists locally
  }
  return { ok: true };
}

export function commit(root: string, message: string) {
  git(root, ["add", "-A"]);
  git(root, ["commit", "-m", message]);
  const sha = git(root, ["rev-parse", "HEAD"]);
  return { ok: true, sha };
}

export function push(root: string) {
  const branchName = git(root, ["symbolic-ref", "--short", "HEAD"]);
  git(root, ["push", "-u", "origin", branchName]);
  return { ok: true };
}

function hasRemote(root: string): boolean {
  try {
    return git(root, ["remote"]).length > 0;
  } catch {
    return false;
  }
}

export function openPr(root: string, title: string, body: string) {
  if (hasRemote(root)) {
    try {
      const url = execFileSync("gh", ["pr", "create", "--title", title, "--body", body, "--fill-first"], {
        cwd: root,
        encoding: "utf-8",
      }).trim();
      return { url };
    } catch {
      // fall through to the documented stub behavior below
    }
  }

  // No remote configured (or `gh` unavailable): stub, per
  // docs/conventions/workspace.md's vcs.open_pr contract.
  return { url: "https://example.invalid/pr/stub/1" };
}

/**
 * apply_patch's semantics depend on how the factory resolves an
 * artifact_ref into bytes a workspace server (which has no access to the
 * factory's artifact store) can consume — that resolution mechanism is
 * step 3 work, alongside the front MCP surface. This stub reports
 * `needs_human` so a caller sees a clear, correctly-classified failure
 * (docs/adr/0007-failure-classes.md) instead of a silent no-op.
 */
export function applyPatch(_root: string, _patchRef: string) {
  return {
    ok: false,
    failure_class: "needs_human" as const,
    message: "vcs.apply_patch is not implemented yet; artifact_ref resolution lands in step 3.",
  };
}

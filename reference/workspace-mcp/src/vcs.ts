import { execFileSync } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

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
 * Applies a patch the factory produced.
 *
 * `patch_ref` is an HTTPS URL the factory minted for exactly this call — one
 * artifact, one project, expiring in minutes. A workspace server holds no
 * factory credential, so the URL is the whole authorisation; we fetch it and
 * nothing else.
 *
 * The patch is applied with `git apply --3way`, which is what makes this
 * usable rather than brittle: a plain `git apply` fails outright whenever
 * context has drifted by a line, while a three-way merge resolves what it
 * can and reports a genuine conflict when it cannot. The check pass first
 * means a patch that cannot apply changes nothing at all — a half-applied
 * patch is far worse than a rejected one, because the run continues on top
 * of it.
 */
export async function applyPatch(root: string, patchRef: string) {
  let patch: string;
  try {
    patch = await fetchPatch(patchRef);
  } catch (error) {
    return {
      ok: false,
      // The factory is unreachable or the URL expired; both are worth
      // retrying (docs/adr/0007-failure-classes.md).
      failure_class: "retryable" as const,
      message: `could not fetch the patch from '${patchRef}': ${(error as Error).message}`,
    };
  }

  if (patch.trim().length === 0) {
    return { ok: false, failure_class: "permanent" as const, message: "the patch was empty" };
  }

  const patchPath = join(mkdtempSync(join(tmpdir(), "df-patch-")), "changeset.patch");
  writeFileSync(patchPath, patch, "utf-8");

  try {
    // Dry run. Nothing is written unless the whole patch can land.
    git(root, ["apply", "--3way", "--check", patchPath]);
  } catch (error) {
    return {
      ok: false,
      // The patch does not fit this tree. Retrying an identical patch
      // against an identical tree will fail identically.
      failure_class: "permanent" as const,
      message: `the patch does not apply cleanly: ${stderrOf(error)}`,
    };
  }

  // --numstat only. Adding --summary also emits lines like
  // "create mode 100644 DEMO.md", which have no tabs and would be
  // reported as if they were file paths.
  const filesChanged = git(root, ["apply", "--numstat", patchPath])
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => line.split("\t"))
    .filter((fields) => fields.length >= 3)
    .map((fields) => fields[2])
    .filter((path, index, all) => all.indexOf(path) === index);

  git(root, ["apply", "--3way", patchPath]);

  return { ok: true, files_changed: filesChanged };
}

async function fetchPatch(patchRef: string): Promise<string> {
  if (!/^https?:\/\//i.test(patchRef)) {
    // factory:// refs are internal to the factory. A workspace server is
    // handed a resolved URL precisely because it cannot resolve one.
    throw new Error("expected an http(s) URL; the factory resolves artifact refs before calling");
  }

  const response = await fetch(patchRef);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }
  return await response.text();
}

function stderrOf(error: unknown): string {
  const withStderr = error as { stderr?: Buffer | string; message?: string };
  const stderr = withStderr.stderr?.toString().trim();
  return stderr && stderr.length > 0 ? stderr : (withStderr.message ?? String(error));
}

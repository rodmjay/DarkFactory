import fg from "fast-glob";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join, relative, resolve } from "node:path";

// Implements files.list / files.read_many / files.search / files.write_many
// (docs/conventions/workspace.md). Batch-first: one round trip per intent.

const MAX_INLINE_BYTES = 200_000;

/** Resolves a workspace-relative path and refuses to escape the workspace root. */
function resolveWithinRoot(root: string, path: string): string {
  const resolved = resolve(root, path);
  const relativePath = relative(root, resolved);
  if (relativePath.startsWith("..") || resolve(relativePath) === relativePath) {
    throw new Error(`path '${path}' escapes the workspace root`);
  }
  return resolved;
}

// Directories nobody means when they ask for "**/*", and which would bury
// a real answer in noise if included.
const ALWAYS_IGNORED = ["**/.git/**", "**/node_modules/**"];

export async function listFiles(root: string, globs: string[]): Promise<string[]> {
  // dot: true. A workspace server that cannot see .github/workflows,
  // .env.example or .dark-factory/ when explicitly asked for them is not
  // much use for maintaining a repository — those are exactly the files a
  // build stage needs to reason about. The noise that `dot: false` was
  // really guarding against is .git and node_modules, which are excluded
  // by name instead.
  return fg(globs, {
    cwd: root,
    dot: true,
    onlyFiles: true,
    followSymbolicLinks: false,
    ignore: ALWAYS_IGNORED,
  });
}

export async function readManyFiles(root: string, paths: string[]) {
  return Promise.all(
    paths.map(async (path) => {
      const absolute = resolveWithinRoot(root, path);
      try {
        const buffer = await readFile(absolute);
        const truncated = buffer.length > MAX_INLINE_BYTES;
        const content = buffer.subarray(0, MAX_INLINE_BYTES).toString("utf-8");
        return { path, content, truncated };
      } catch (error) {
        return { path, content: "", truncated: false, error: (error as Error).message };
      }
    }),
  );
}

export async function searchFiles(root: string, query: string, globs?: string[]) {
  const candidatePaths = await listFiles(root, globs ?? ["**/*"]);
  const matches: { path: string; line: number; column: number; preview: string }[] = [];

  for (const path of candidatePaths) {
    let text: string;
    try {
      text = await readFile(join(root, path), "utf-8");
    } catch {
      continue; // binary or unreadable; skip
    }

    const lines = text.split("\n");
    lines.forEach((line, index) => {
      const column = line.indexOf(query);
      if (column !== -1) {
        matches.push({ path, line: index + 1, column: column + 1, preview: line.trim() });
      }
    });
  }

  return matches;
}

export async function writeManyFiles(root: string, files: { path: string; content: string }[]) {
  const written: string[] = [];
  for (const file of files) {
    const absolute = resolveWithinRoot(root, file.path);
    await mkdir(dirname(absolute), { recursive: true });
    await writeFile(absolute, file.content, "utf-8");
    written.push(file.path);
  }
  return written;
}

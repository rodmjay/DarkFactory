import { exec } from "node:child_process";

// Implements exec.run (docs/conventions/workspace.md). The async job /
// webhook-callback path for calls that exceed the inline threshold is step
// 3 work, once the factory's webhook ingress and job records exist
// (docs/conventions/envelope.md#async-jobs); this reference implementation
// always runs inline and reports the result directly.

const TEST_SUMMARY_PATTERNS: { regex: RegExp; parse: (m: RegExpMatchArray) => { passed: number; failed: number; skipped: number } }[] = [
  {
    // xunit console runner: "Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7"
    regex: /Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)/,
    parse: (m) => ({ failed: Number(m[1]), passed: Number(m[2]), skipped: Number(m[3]) }),
  },
  {
    // jest: "Tests:       1 failed, 2 skipped, 7 passed, 10 total"
    regex: /Tests:\s*(?:(\d+) failed, )?(?:(\d+) skipped, )?(\d+) passed/,
    parse: (m) => ({
      failed: Number(m[1] ?? 0),
      skipped: Number(m[2] ?? 0),
      passed: Number(m[3] ?? 0),
    }),
  },
];

function parseTestSummary(output: string) {
  for (const { regex, parse } of TEST_SUMMARY_PATTERNS) {
    const match = output.match(regex);
    if (match) return parse(match);
  }
  return undefined;
}

export function runCommand(command: string, cwd: string, timeoutMs: number) {
  return new Promise<{
    exit_code: number;
    stdout_ref: string;
    stderr_ref: string;
    parsed?: { tests: { passed: number; failed: number; skipped: number } };
  }>((resolvePromise) => {
    exec(command, { cwd, timeout: timeoutMs, maxBuffer: 10 * 1024 * 1024 }, (error, stdout, stderr) => {
      const exitCode = error && typeof error.code === "number" ? error.code : error ? 1 : 0;
      const tests = parseTestSummary(stdout + "\n" + stderr);
      resolvePromise({
        exit_code: exitCode,
        stdout_ref: stdout,
        stderr_ref: stderr,
        ...(tests ? { parsed: { tests } } : {}),
      });
    });
  });
}

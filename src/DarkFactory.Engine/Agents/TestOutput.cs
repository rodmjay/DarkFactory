using System.Text.RegularExpressions;
using DarkFactory.Core;

namespace DarkFactory.Engine.Agents;

public sealed record ParsedTestRun(int Passed, int Failed, int Skipped, bool CountsFound);

/// <summary>
/// Reads pass/fail counts out of a test runner's output.
///
/// Best-effort by design, and the design matters: the counts are for the
/// report a human reads, while <em>success</em> is decided by the exit
/// code. A parser that inferred success from counts it recognised would
/// call an unrecognised runner's failing suite a pass, which is the single
/// worst thing this component could do.
/// </summary>
public static partial class TestOutput
{
    // dotnet: "Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13"
    [GeneratedRegex(@"Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetSummary();

    // vitest/jest: "Tests  3 failed | 12 passed | 1 skipped"
    [GeneratedRegex(@"(\d+)\s+failed", RegexOptions.IgnoreCase)]
    private static partial Regex JsFailed();

    [GeneratedRegex(@"(\d+)\s+passed", RegexOptions.IgnoreCase)]
    private static partial Regex JsPassed();

    [GeneratedRegex(@"(\d+)\s+skipped", RegexOptions.IgnoreCase)]
    private static partial Regex JsSkipped();

    public static ParsedTestRun Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new ParsedTestRun(0, 0, 0, CountsFound: false);
        }

        // Last match wins: a run of several test projects prints one
        // summary each, and the interesting one is not the first.
        var dotnet = DotnetSummary().Matches(output).LastOrDefault();
        if (dotnet is not null)
        {
            return new ParsedTestRun(
                Passed: int.Parse(dotnet.Groups[2].Value),
                Failed: int.Parse(dotnet.Groups[1].Value),
                Skipped: int.Parse(dotnet.Groups[3].Value),
                CountsFound: true);
        }

        var passed = Last(JsPassed(), output);
        var failed = Last(JsFailed(), output);
        var skipped = Last(JsSkipped(), output);

        return passed is null && failed is null
            ? new ParsedTestRun(0, 0, 0, CountsFound: false)
            : new ParsedTestRun(passed ?? 0, failed ?? 0, skipped ?? 0, CountsFound: true);
    }

    private static int? Last(Regex pattern, string output)
    {
        var match = pattern.Matches(output).LastOrDefault();
        return match is null ? null : int.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// How a verify failure is classified (docs/adr/0007).
    ///
    /// A red suite is <see cref="FailureClass.NeedsHuman"/>, not
    /// <see cref="FailureClass.Permanent"/>: the run did its job and
    /// produced a real answer, and what happens next — fix the code, fix the
    /// test, or accept it — is a judgement nobody should be making
    /// automatically. Retryable would be worse still: running the same tests
    /// against the same tree again is not a strategy.
    ///
    /// A command that could not run at all is different. That is the
    /// factory's own misconfiguration, and it is permanent until someone
    /// changes the configuration.
    /// </summary>
    public static FailureClass Classify(int exitCode, ParsedTestRun parsed) =>
        exitCode switch
        {
            0 => throw new InvalidOperationException("A zero exit code is not a failure."),
            // 127 is "command not found" on every POSIX shell: the team's
            // declared test command does not exist in this workspace.
            127 or 126 => FailureClass.Permanent,
            _ => FailureClass.NeedsHuman,
        };
}

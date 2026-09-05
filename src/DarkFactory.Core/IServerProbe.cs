namespace DarkFactory.Core;

/// <summary>
/// The seam between the registry (which owns the rules) and MCP (which owns
/// the wire). Deliberately expressed in strings and primitives so
/// DarkFactory.Data can depend on it without depending on the MCP SDK —
/// the registry's logic is about validation, idempotency and diffing, none
/// of which should need a transport in scope to test.
/// </summary>
public interface IServerProbe
{
    /// <summary>
    /// Calls one tool and returns its raw JSON payload. Never throws for a
    /// remote failure: an unreachable server, a protocol error and a tool
    /// that returns <c>isError</c> are all outcomes to record, not
    /// exceptions to unwind through.
    /// </summary>
    Task<ProbeResult> CallAsync(
        string serverUrl,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        TimeSpan deadline,
        CancellationToken cancellationToken = default);
}

/// <param name="Ok">Whether the call completed and returned a payload.</param>
/// <param name="Json">The tool's JSON result when <paramref name="Ok"/>.</param>
/// <param name="Error">Why it failed, phrased for a human reading a registration error.</param>
/// <param name="Failure">Classification per docs/adr/0007, when the call failed.</param>
/// <param name="DurationMs">Wall-clock time, recorded against each conformance row.</param>
public sealed record ProbeResult(
    bool Ok,
    string? Json,
    string? Error,
    FailureClass? Failure,
    long DurationMs)
{
    public static ProbeResult Success(string json, long durationMs) => new(true, json, null, null, durationMs);

    public static ProbeResult Failed(string error, FailureClass failure, long durationMs) =>
        new(false, null, error, failure, durationMs);
}

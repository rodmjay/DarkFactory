using DarkFactory.Contracts;

namespace DarkFactory.Client;

/// <summary>Builds the call envelope (docs/conventions/envelope.md) attached to every outbound call.</summary>
public static class EnvelopeFactory
{
    public static Envelope Create(
        string runId,
        string stageId,
        string projectId,
        string idempotencyKey,
        string traceId,
        TimeSpan timeout,
        string? callbackToken = null) => new()
    {
        RunId = runId,
        StageId = stageId,
        ProjectId = projectId,
        IdempotencyKey = idempotencyKey,
        TraceId = traceId,
        Deadline = DateTimeOffset.UtcNow.Add(timeout),
        CallbackToken = callbackToken
    };
}

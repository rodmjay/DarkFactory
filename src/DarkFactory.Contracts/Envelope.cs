using System.Text.Json.Serialization;

namespace DarkFactory.Contracts;

/// <summary>
/// The call envelope attached to every outbound Dark Factory tool call.
/// Mirrors contracts/schemas/envelope.schema.json.
/// See docs/conventions/envelope.md and docs/adr/0005-call-envelope.md.
/// </summary>
public sealed record Envelope
{
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("stage_id")]
    public required string StageId { get; init; }

    [JsonPropertyName("project_id")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    [JsonPropertyName("trace_id")]
    public required string TraceId { get; init; }

    [JsonPropertyName("deadline")]
    public required DateTimeOffset Deadline { get; init; }

    [JsonPropertyName("callback_token")]
    public string? CallbackToken { get; init; }
}

/// <summary>
/// Failure classification. Mirrors the "failure_class" enum in
/// contracts/schemas/hookresult.schema.json.
/// See docs/adr/0007-failure-classes.md.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FailureClass>))]
public enum FailureClass
{
    Retryable,
    Permanent,
    NeedsHuman
}

/// <summary>
/// The result shape returned by hook points and convention tool calls.
/// Mirrors contracts/schemas/hookresult.schema.json.
/// </summary>
public sealed record HookResult
{
    [JsonPropertyName("envelope")]
    public required Envelope Envelope { get; init; }

    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("failure_class")]
    public FailureClass? FailureClass { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("job_id")]
    public string? JobId { get; init; }
}

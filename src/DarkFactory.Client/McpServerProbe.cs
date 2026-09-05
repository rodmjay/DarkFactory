using System.Diagnostics;
using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DarkFactory.Client;

/// <summary>
/// <see cref="IServerProbe"/> over Streamable HTTP MCP. This is what turns
/// the registry's rules into actual wire calls (docs/adr/0001).
///
/// A fresh connection per call is deliberate here: probes are rare
/// (registration and conformance only), they target servers the factory has
/// not yet decided to trust, and a pooled session would keep such a server
/// attached to the factory between calls. Run-time calls, which are hot and
/// go to already-registered servers, get a pooled client when
/// <see cref="ISpokeClient"/> is implemented for the engine.
/// </summary>
public sealed class McpServerProbe(ILoggerFactory? loggerFactory = null) : IServerProbe
{
    public async Task<ProbeResult> CallAsync(
        string serverUrl,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        TimeSpan deadline,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var endpoint))
        {
            return ProbeResult.Failed($"'{serverUrl}' is not an absolute URL", FailureClass.Permanent, 0);
        }

        using var deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineSource.CancelAfter(deadline);

        try
        {
            await using var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = deadline,
                },
                loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

            await using var client = await McpClient.CreateAsync(
                transport, loggerFactory: loggerFactory, cancellationToken: deadlineSource.Token);

            var payload = new Dictionary<string, object?>(arguments.Count + 1);
            foreach (var (key, value) in arguments)
            {
                if (value is not null)
                {
                    payload[key] = value;
                }
            }

            // docs/adr/0005: every outbound call carries an envelope, this
            // one included. Its deadline is the same one bounding the
            // client, so the callee is told exactly how long the caller
            // will actually wait rather than being left to guess.
            payload["envelope"] = BuildEnvelope(deadline);

            var result = await client.CallToolAsync(toolName, payload, cancellationToken: deadlineSource.Token);

            if (result.IsError == true)
            {
                return ProbeResult.Failed(
                    $"{toolName} returned an error: {Truncate(ExtractText(result) ?? "(no message)")}",
                    FailureClass.Permanent,
                    stopwatch.ElapsedMilliseconds);
            }

            var json = ExtractJson(result);
            return json is null
                ? ProbeResult.Failed(
                    $"{toolName} returned no JSON payload", FailureClass.Permanent, stopwatch.ElapsedMilliseconds)
                : ProbeResult.Success(json, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline elapsed rather than the caller giving up.
            return ProbeResult.Failed(
                $"{toolName} did not respond within {deadline.TotalSeconds:0.#}s",
                FailureClass.Retryable,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreachable or misbehaving server is an outcome to record
            // against the registry, never an exception that unwinds through
            // registration — the whole point is to describe what went wrong.
            return ProbeResult.Failed(
                $"{toolName} failed: {ex.GetType().Name}: {ex.Message}",
                FailureClass.Retryable,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static Envelope BuildEnvelope(TimeSpan deadline)
    {
        var id = Guid.NewGuid().ToString("n");
        return EnvelopeFactory.Create(
            runId: $"conformance:{id}",
            stageId: "conformance",
            projectId: "conformance",
            idempotencyKey: id,
            traceId: Activity.Current?.TraceId.ToString() ?? id,
            timeout: deadline);
    }

    /// <summary>
    /// Prefers structured content, falling back to the first text block —
    /// which is how servers built on the TypeScript SDK's
    /// <c>{ content: [{ type: "text", text: "..." }] }</c> shape answer.
    /// </summary>
    private static string? ExtractJson(CallToolResult result)
    {
        if (result.StructuredContent is { } structured)
        {
            return structured.GetRawText();
        }

        var text = ExtractText(result);
        if (text is null)
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(text);
            return text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractText(CallToolResult result) =>
        result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";
}

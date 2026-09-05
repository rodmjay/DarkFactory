using System.Text.Json;
using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// Canned model responses. No test in this suite makes a live model call:
/// a suite whose results depend on what a model felt like saying today is
/// not a test suite, and the behaviour under test here — context assembly,
/// validation, the retry, what does and does not get persisted — is
/// entirely the factory's, not the model's.
///
/// The one live check lives in <c>FoundryIntegrationTests</c>, opt-in
/// behind an environment variable.
/// </summary>
internal sealed class FakeModelGateway : IModelGateway
{
    private readonly Queue<Func<ModelRequest, ModelCompletion>> _responses = new();

    public List<ModelRequest> Requests { get; } = [];

    public Task<ModelCompletion> CompleteAsync(
        ModelRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeModelGateway ran out of canned responses on call {Requests.Count} " +
                $"(deployment '{request.Deployment}'). The code under test called the model " +
                "more times than the test expected, which is itself worth knowing.");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }

    public FakeModelGateway Responds(string text)
    {
        _responses.Enqueue(request => new ModelCompletion(text, new ModelUsage(100, 50), request.Deployment)
        {
            LatencyMs = 12,
            Provider = "fake",
            ModelFamily = "fake-model",
        });
        return this;
    }

    /// <summary>For the fact-row tests, where the usage breakdown is the thing under test.</summary>
    public FakeModelGateway RespondsWithUsage(string text, ModelUsage usage)
    {
        _responses.Enqueue(request => new ModelCompletion(text, usage, request.Deployment)
        {
            LatencyMs = 12,
            Provider = "fake",
            ModelFamily = "fake-model",
        });
        return this;
    }

    /// <summary>Makes the inner gateway throw, so the recorder's failure path is exercised.</summary>
    public FakeModelGateway Fails(string message)
    {
        _responses.Enqueue(_ => throw new ModelGatewayException(message, FailureClass.Retryable));
        return this;
    }

    /// <summary>A well-formed architect response that has not settled — the common case.</summary>
    public FakeModelGateway RespondsChatting(string reply = "Tell me more about that.") =>
        Responds(JsonSerializer.Serialize(new { reply, settled = false, diff = (object?)null }));

    /// <summary>A well-formed architect response carrying a proposal.</summary>
    public FakeModelGateway RespondsProposing(object diff, string reply = "Here is what I think we agreed.") =>
        Responds(JsonSerializer.Serialize(new { reply, settled = true, diff }));

    /// <summary>Convenience: a single new node.</summary>
    public static object CreateDiff(
        string text, string kind = "rule", string layer = "domain") => new
        {
            creates = new[] { new { kind, layer, text } },
            revises = Array.Empty<object>(),
            retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(),
            edge_retires = Array.Empty<object>(),
        };
}

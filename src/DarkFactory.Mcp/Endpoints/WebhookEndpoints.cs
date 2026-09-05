namespace DarkFactory.Mcp.Endpoints;

/// <summary>
/// Async job completion callbacks (docs/conventions/envelope.md#async-jobs,
/// docs/adr/0006-four-channels.md). Webhooks flow only into and out of the
/// factory, never between spoke servers.
///
/// Signed-token verification and dispatch back into the engine's event bus
/// are step 2/3 work once the engine and job records exist; this slice
/// wires the route and shape so spoke servers have a stable address to
/// build against.
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/jobs/{jobId}", (string jobId) =>
            Results.Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                title: "Job callback ingress not yet implemented",
                detail: $"job_id={jobId} was received; dispatching it into a run is step 2/3 work."));

        return app;
    }
}

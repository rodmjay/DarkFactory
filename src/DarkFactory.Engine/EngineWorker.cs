namespace DarkFactory.Engine;

/// <summary>
/// The background worker that will poll the <c>runs</c> queue with
/// <c>FOR UPDATE SKIP LOCKED</c> and drive the checkpointed state machine
/// (docs/adr/0008-durable-orchestration.md): load checkpoint, dispatch
/// pre-hooks, run the stage agent, validate + persist the artifact,
/// dispatch post-hooks, emit <c>stage.completed</c>, advance.
///
/// The state machine, hook dispatcher, retry policy, and gate handling are
/// step 2 work (data model + migrations + engine, with tests proving
/// checkpoint/resume). This slice proves the worker process starts, logs a
/// heartbeat, and reports healthy via the same /health endpoint pattern as
/// the factory.
/// </summary>
public sealed class EngineWorker(ILogger<EngineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Dark Factory engine worker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Engine heartbeat at {Timestamp:o}. Run queue polling lands in step 2.", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

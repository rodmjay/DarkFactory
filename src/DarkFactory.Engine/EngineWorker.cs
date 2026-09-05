using DarkFactory.Core;
using Microsoft.Extensions.Options;

namespace DarkFactory.Engine;

/// <summary>
/// Polls for runnable work and drives it one stage at a time
/// (docs/adr/0008): claim, run the stage handler, checkpoint + outbox event
/// + advance in one transaction (RunStateMachine), repeat. A fresh DI scope
/// (and DbContext) per iteration, since EngineWorker itself is a singleton.
/// </summary>
public sealed class EngineWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EngineOptions> options,
    ILogger<EngineWorker> logger) : BackgroundService
{
    /// <summary>
    /// Test-only crash injection: if set to a StageId name, the worker
    /// exits immediately (no graceful shutdown, no lease release) right
    /// after successfully processing that stage — simulating a hard kill
    /// between "checkpoint committed" and "next stage starts." See
    /// tests/DarkFactory.Engine.Tests's crash/resume test.
    /// </summary>
    private const string CrashAfterStageEnvVar = "DARKFACTORY_CRASH_AFTER_STAGE";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var crashAfterStage = Environment.GetEnvironmentVariable(CrashAfterStageEnvVar);
        logger.LogInformation(
            "Dark Factory engine worker {WorkerId} starting (lease={LeaseSeconds}s, poll={PollSeconds}s).",
            options.Value.WorkerId, options.Value.LeaseDurationSeconds, options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            StageId? processedStage;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var stateMachine = scope.ServiceProvider.GetRequiredService<RunStateMachine>();
                processedStage = await stateMachine.TryProcessOneAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error while processing a run.");
                processedStage = null;
            }

            if (processedStage is { } stage)
            {
                logger.LogInformation("Processed stage {Stage}.", stage);

                if (crashAfterStage is not null && string.Equals(stage.ToString(), crashAfterStage, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "{EnvVar}={Stage} matched; exiting immediately to simulate a crash.",
                        CrashAfterStageEnvVar, stage);
                    Environment.Exit(137);
                }
            }
            else
            {
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
    }
}

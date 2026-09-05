namespace DarkFactory.Core;

/// <summary>The fixed six-stage order (docs/adr/0003-pipeline-skeleton.md).</summary>
public static class PipelineStages
{
    public static readonly StageId[] Order =
    [
        StageId.Intake,
        StageId.Spec,
        StageId.Plan,
        StageId.Implement,
        StageId.Verify,
        StageId.Ship
    ];

    public static bool IsLast(StageId stage) => stage == Order[^1];

    /// <summary>Throws if called on the last stage — check <see cref="IsLast"/> first.</summary>
    public static StageId Next(StageId stage)
    {
        var index = Array.IndexOf(Order, stage);
        if (index < 0 || index == Order.Length - 1)
        {
            throw new InvalidOperationException($"'{stage}' has no next stage.");
        }

        return Order[index + 1];
    }
}

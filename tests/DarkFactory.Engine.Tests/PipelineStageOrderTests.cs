using DarkFactory.Core;

namespace DarkFactory.Engine.Tests;

// Covers docs/adr/0003-pipeline-skeleton.md's fixed six-stage order, which
// RunStateMachine advances through. See CrashResumeTests for the
// checkpoint/resume proof and RunStateMachineHappyPathTests for the full
// walk through all six stages.
public class PipelineStageOrderTests
{
    [Fact]
    public void StageId_enum_declares_stages_in_pipeline_order()
    {
        var expected = new[]
        {
            StageId.Intake,
            StageId.Spec,
            StageId.Plan,
            StageId.Implement,
            StageId.Verify,
            StageId.Ship
        };

        Assert.Equal(expected, Enum.GetValues<StageId>());
        Assert.Equal(expected, PipelineStages.Order);
    }

    [Fact]
    public void Next_throws_on_the_last_stage()
    {
        Assert.True(PipelineStages.IsLast(StageId.Ship));
        Assert.Throws<InvalidOperationException>(() => PipelineStages.Next(StageId.Ship));
    }
}

using DarkFactory.Core;

namespace DarkFactory.Engine.Tests;

// Covers docs/adr/0003-pipeline-skeleton.md's fixed six-stage order, which
// the state machine (step 2) advances through.
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

        var actual = Enum.GetValues<StageId>();

        Assert.Equal(expected, actual);
    }
}

// The Postgres-backed state machine, hook dispatcher, retry policy, and
// gate handling are step 2 work (docs/adr/0008-durable-orchestration.md).
// That step must add a test here that starts a run, kills the worker
// mid-`implement`, restarts it, and asserts the run resumes from
// `implement` rather than `intake` — proving checkpoint/resume actually
// works rather than merely compiling.

namespace DarkFactory.Core;

/// <summary>The six fixed pipeline stages. See docs/adr/0003-pipeline-skeleton.md.</summary>
public enum StageId
{
    Intake,
    Spec,
    Plan,
    Implement,
    Verify,
    Ship
}

/// <summary>Failure classification that drives the engine's retry policy. See docs/adr/0007-failure-classes.md.</summary>
public enum FailureClass
{
    Retryable,
    Permanent,
    NeedsHuman
}

public enum RunStatus
{
    Pending,
    Running,
    AwaitingApproval,
    Failed,
    Cancelled,
    Completed
}

public enum GateStatus
{
    Waiting,
    Approved,
    Rejected
}

/// <summary>The two v1 human gates. See docs/adr/0003-pipeline-skeleton.md.</summary>
public enum GateKind
{
    SpecApproval,
    PrApproval
}

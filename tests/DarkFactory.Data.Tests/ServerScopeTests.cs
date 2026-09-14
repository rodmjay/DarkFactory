namespace DarkFactory.Data.Tests;

/// <summary>
/// Which project a server serves, as shown beside it. Read from the
/// handshake the server gave, never guessed from its name.
/// </summary>
public sealed class ServerScopeTests
{
    [Fact]
    public void ACorpusServerServesTheProjectItNames() =>
        Assert.Equal("drones", ServerScope.Of("corpus",
            """{"name":"moonbeam-specs","effective_config":{"areas":"drones","project":"drones"}}"""));

    [Fact]
    public void AWorkspaceServesTheRepoItNames() =>
        Assert.Equal("@moonbeam/drones", ServerScope.Of("workspace",
            """{"name":"dark-factory-workspace-mcp","effective_config":{"name":"@moonbeam/drones","root":"/workspace"}}"""));

    [Fact]
    public void AStandardsServerIsSharedWhateverItSays() =>
        Assert.Null(ServerScope.Of("standards", """{"effective_config":{"project":"drones"}}"""));

    [Fact]
    public void AServerThatNamesNothingHasNoScope()
    {
        Assert.Null(ServerScope.Of("corpus", """{"effective_config":{"areas":"drones, smashhit"}}"""));
        Assert.Null(ServerScope.Of("corpus", null));
        Assert.Null(ServerScope.Of("corpus", "not json"));
    }
}

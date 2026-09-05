namespace DarkFactory.Client;

/// <summary>
/// The factory's MCP client wrapper for talking to workspace/theme/plugin
/// servers (docs/adr/0001-hub-and-spoke-topology.md): applies the call
/// envelope, enforces the deadline, and classifies failures
/// (docs/adr/0007-failure-classes.md).
///
/// Connection management and per-convention tool calls (workspace.describe,
/// files.*, exec.*, vcs.*) land in step 3 alongside the front MCP surface
/// and the reference workspace server. This interface exists now so
/// DarkFactory.Engine can be written against a stable seam.
/// </summary>
public interface ISpokeClient
{
    Task<TResult> CallToolAsync<TResult>(
        string serverUrl,
        string toolName,
        object arguments,
        CancellationToken cancellationToken = default);
}

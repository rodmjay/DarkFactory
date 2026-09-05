using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// Placeholder tool proving the MCP server wiring end-to-end. The real front
/// surface (projects.*, work.*, plugins.*) lands in step 3 alongside the
/// reference workspace server, once the engine (step 2) exists to back it.
/// </summary>
[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "system.ping"), Description("Liveness check for the Dark Factory MCP server.")]
    public static string Ping() => "pong";
}

using System.ComponentModel;
using DarkFactory.Data;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// The conversation surface (docs/adr/0017) — the product, not a
/// job-submission form. Thin over <see cref="ConversationService"/>, which
/// is where context assembly, model output validation and proposal
/// persistence actually live.
/// </summary>
[McpServerToolType]
public static class ConversationTools
{
    [McpServerTool(Name = "df.conversations.start"),
     Description("Start a conversation with the project's architect agent.")]
    public static async Task<ConversationSummary> Start(
        ConversationService conversations,
        IConfiguration configuration,
        [Description("The project to converse about.")] string project_id,
        [Description("Optional title.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await Errors.Surfacing(() => conversations.StartAsync(
            project_id, title, ServerTools.ResolveOrgId(configuration), cancellationToken));

        return new ConversationSummary(
            conversation.Id, conversation.ProjectId, conversation.Title, conversation.Status.ToString());
    }

    [McpServerTool(Name = "df.conversations.turn"),
     Description("Send a message. Returns ADR-0021 payloads: a markdown reply, plus a spec_diff proposal when the conversation has settled.")]
    public static async Task<TurnResult> Turn(
        ConversationService conversations,
        IConfiguration configuration,
        [Description("The conversation id from df.conversations.start.")] string conversation_id,
        [Description("Your message.")] string message,
        CancellationToken cancellationToken = default)
    {
        var result = await Errors.Surfacing(() => conversations.TurnAsync(
            conversation_id, message, ServerTools.ResolveOrgId(configuration), cancellationToken));

        return new TurnResult(
            result.TurnId,
            result.Payloads,
            result.AmendmentId,
            // Exposed deliberately: "what did the model see when it said
            // that" should be one lookup away for anyone reviewing a
            // proposal, not an archaeology exercise (docs/adr/0023).
            result.ContextRef,
            result.Usage.TotalTokens);
    }
}

public sealed record ConversationSummary(string Id, string ProjectId, string? Title, string Status);

public sealed record TurnResult(
    string TurnId,
    IReadOnlyList<RenderPayload> Payloads,
    string? AmendmentId,
    string ContextRef,
    int TokensUsed);

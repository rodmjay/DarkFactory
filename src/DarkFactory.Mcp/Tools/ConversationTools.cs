using System.ComponentModel;
using System.Text.Json;
using DarkFactory.Core;
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

    [McpServerTool(Name = "df.conversations.list"),
     Description("A project's conversations, most recently active first, with turn and amendment counts.")]
    public static async Task<IReadOnlyList<ConversationListItem>> List(
        ConversationService conversations,
        TeamService teams,
        [Description("The project whose conversations to list.")] string project_id,
        CancellationToken cancellationToken = default)
    {
        var listings = await Errors.Surfacing(() => conversations.ListAsync(project_id, cancellationToken));
        var deployment = await ArchitectDeploymentAsync(teams, project_id, cancellationToken);
        return listings.Select(l => ConversationListItem.From(l, deployment)).ToList();
    }

    [McpServerTool(Name = "df.conversations.get"),
     Description("One conversation's thread: every turn with its ADR-0021 payloads as stored, and where each amendment it produced stands.")]
    public static async Task<ConversationDetail> Get(
        ConversationService conversations,
        TeamService teams,
        [Description("The conversation id.")] string conversation_id,
        CancellationToken cancellationToken = default)
    {
        var thread = await Errors.Surfacing(() => conversations.GetAsync(conversation_id, cancellationToken));
        var deployment = await ArchitectDeploymentAsync(teams, thread.Listing.Conversation.ProjectId, cancellationToken);

        return new ConversationDetail(
            ConversationListItem.From(thread.Listing, deployment),
            thread.Turns.Select(t => new TurnView(
                t.Id,
                t.Seq,
                t.Role.ToString().ToLowerInvariant(),
                t.Content,
                // Stored as the serialized payload list and handed back
                // byte-for-byte, so the thread renders exactly what the turn
                // answered with — nothing is re-derived on the way out.
                t.PayloadsJson is null ? null : JsonDocument.Parse(t.PayloadsJson).RootElement.Clone(),
                t.TokenUsage,
                t.CreatedAt)).ToList(),
            thread.Amendments.Select(a => new AmendmentStateView(
                a.Id, a.Status.ToString().ToLowerInvariant(), a.TurnId, a.CreatedAt, a.RejectedReason)).ToList());
    }

    /// <summary>
    /// The deployment the project's architect answers on, for the thread's
    /// header. A project without a team still has conversations to read, so
    /// its absence is a null here rather than an error.
    /// </summary>
    private static async Task<string?> ArchitectDeploymentAsync(
        TeamService teams, string projectId, CancellationToken cancellationToken)
    {
        try
        {
            return (await teams.ResolveAsync(projectId, AssignmentPoints.Conversation, cancellationToken)).Deployment;
        }
        catch (TeamNotConfiguredException)
        {
            return null;
        }
    }
}

public sealed record ConversationSummary(string Id, string ProjectId, string? Title, string Status);

public sealed record TurnResult(
    string TurnId,
    IReadOnlyList<RenderPayload> Payloads,
    string? AmendmentId,
    string ContextRef,
    int TokensUsed);

public sealed record ConversationListItem(
    string Id,
    string ProjectId,
    string? Title,
    string Status,
    // "conversation", or "intake" for an import's thread (docs/adr/0037).
    string Kind,
    int TurnCount,
    int Amendments,
    int AwaitingAmendments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Deployment)
{
    public static ConversationListItem From(ConversationListing listing, string? deployment) => new(
        listing.Conversation.Id,
        listing.Conversation.ProjectId,
        listing.Conversation.Title,
        listing.Conversation.Status.ToString().ToLowerInvariant(),
        listing.IsIntake ? "intake" : "conversation",
        listing.TurnCount,
        listing.Amendments,
        listing.AwaitingAmendments,
        listing.Conversation.CreatedAt,
        listing.UpdatedAt,
        deployment);
}

public sealed record TurnView(
    string Id,
    int Seq,
    string Role,
    string Content,
    JsonElement? Payloads,
    int? Tokens,
    DateTimeOffset At);

public sealed record AmendmentStateView(
    string Id, string Status, string? TurnId, DateTimeOffset CreatedAt, string? RejectedReason);

public sealed record ConversationDetail(
    ConversationListItem Conversation,
    IReadOnlyList<TurnView> Turns,
    IReadOnlyList<AmendmentStateView> Amendments);

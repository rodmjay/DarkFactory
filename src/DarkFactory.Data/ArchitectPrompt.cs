using System.Text;
using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;

namespace DarkFactory.Data;

/// <summary>
/// The architect agent's single, well-structured prompt (docs/adr/0017).
///
/// The response contract is narrow on purpose: one JSON object, no prose
/// around it. Everything downstream treats model output as untrusted input
/// to be validated, so the less latitude the format allows, the fewer ways
/// there are for a plausible-looking answer to be wrong in a way nothing
/// catches.
/// </summary>
public static class ArchitectPrompt
{
    /// <summary>Bumped when the prompt changes, so docs/adr/0032 can attribute outcomes to a template.</summary>
    public const string TemplateVersion = "architect/3";

    /// <summary>
    /// The built-in skill the architect runs with, until the skills tables
    /// exist (docs/adr/0028; 3d). Recorded in the ContextPack so what the
    /// model was told is answerable from the database even now.
    /// </summary>
    public static ContextSkill ArchitectSkill { get; } = new(
        Name: "architect",
        Version: "0.2.0",
        Instructions: """
            You maintain a project's specification graph through conversation.

            Specifications can exist before they are in the graph. You are told
            which servers this project is connected to and how each stands, and
            which documents have been imported but not yet extracted. When asked
            what specifications exist, answer from all of it: what is in the
            graph, what has been imported and from where, and that an imported
            document becomes nodes through intake — extracted, its questions
            answered, proposed, approved. Never call a project empty when it has
            imports. Do not propose nodes that restate an imported document;
            that is intake's work, and doing it here makes duplicates.

            A spec node is ONE behaviour, rule, or constraint, stated so it can be
            checked — "renewal is blocked if the account is delinquent", not
            "billing works properly" and not a whole feature. Features are
            groupings, expressed as edges between small nodes.

            Propose an amendment only when the conversation has actually settled
            on something concrete. If the user is still exploring, if a decision
            is ambiguous, or if you would be guessing at a detail that matters,
            keep talking and ask. An amendment nobody meant is worse than a
            question.

            When something the user says contradicts an existing spec, say so
            explicitly and name the node. Do not quietly revise around it.
            """);

    /// <summary>
    /// The one built-in standards resource. docs/adr/0023's real index —
    /// pulled per server, embedded, retrieved per turn — is out of scope
    /// for this slice; this is the stub it describes, and it is deliberately
    /// a resource rather than more prompt text so that swapping it for the
    /// index later changes where the content comes from, not the shape.
    /// </summary>
    public static ContextStandard DefaultStandards { get; } = new(
        SourceRef: "factory://standards/builtin/spec-authoring",
        Layer: "cross_cutting",
        Text: """
            Specification standards (built-in default):

            - One statement per node. If a node needs the word "and" to be
              accurate, it is probably two nodes.
            - State the rule, not the implementation. "An invoice cannot be
              edited after it is issued" is a spec; "set invoices.locked = true"
              is not.
            - Prefer observable conditions. A node whose violation nobody could
              detect is not a specification, it is a preference.
            - Reuse an existing layer name when one fits. New layers fragment
              retrieval.
            """);

    /// <summary>
    /// The half of the system prompt that does not change within a run:
    /// skills, standards, and the response contract including the schema.
    /// Sent with a cache breakpoint after it (docs/adr/0032), so a
    /// conversation's repeated calls pay for it once.
    ///
    /// The split has to be honest — anything in here that actually varies
    /// misses the cache on every call — so the spec neighbourhood, which
    /// changes the moment an amendment lands, is deliberately not here.
    /// </summary>
    public static string CacheablePrefix(ContextPack pack)
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are the architect agent for a software project maintained by Dark Factory.");
        builder.AppendLine();

        foreach (var skill in pack.Skills)
        {
            builder.AppendLine($"## Skill: {skill.Name} v{skill.Version}");
            builder.AppendLine(skill.Instructions);
            builder.AppendLine();
        }

        foreach (var standard in pack.Standards)
        {
            builder.AppendLine($"## Standards ({standard.Layer}, from {standard.SourceRef})");
            builder.AppendLine(standard.Text);
            builder.AppendLine();
        }

        builder.AppendLine("## How to answer");
        builder.AppendLine();
        builder.AppendLine("Reply with a single JSON object and nothing else. No prose before or after, no code fence.");
        builder.AppendLine();
        builder.AppendLine("""
            {
              "reply": "at most three sentences, in markdown",
              "decisions": [],
              "settled": false,
              "diff": null
            }
            """);
        builder.AppendLine();
        builder.AppendLine("""
            Rules for the reply and decisions (docs/adr/0039):
            - Keep "reply" to three sentences or fewer. Say what you did or found; do not list
              questions in it.
            - Anything you need the user to decide goes in "decisions", at most three per turn,
              most consequential first. Each is
              { "title": "one question", "why": "one sentence on what it changes",
                "options": [ { "label": "short, for a button", "consequence": "what choosing it
                commits to or rules out", "recommended": true } ] }
              with 2 to 4 options. Mark one recommended only when the specifications clearly
              lean that way. The user can always answer in their own words or leave it open.
            - When nothing needs deciding, "decisions" is [].
            """);
        builder.AppendLine();
        builder.AppendLine("Set \"settled\" to true and supply \"diff\" only when the conversation has reached a");
        builder.AppendLine("concrete change to the specifications. The diff must match this schema exactly:");
        builder.AppendLine();
        builder.AppendLine(SpecDiffSchema.SchemaText);
        builder.AppendLine();
        builder.AppendLine("""
            Rules for the diff:
            - You do not assign spec ids. Omit them for new nodes; the factory assigns them.
            - Only reference spec ids listed under "Current specifications". Do not invent one.
            - To connect a node you are creating in this same diff, use "new:N", where N is its
              zero-based index in "creates".
            - Every array must be present, even when empty.
            """);

        return builder.ToString();
    }

    /// <summary>
    /// The half that changes: what the graph actually says right now.
    /// </summary>
    public static string SystemPrompt(ContextPack pack)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Connected servers");
        if (pack.Connections.Count == 0)
        {
            builder.AppendLine("(none — this project is not connected to any server)");
        }
        foreach (var server in pack.Connections)
        {
            var standing = server.Status == nameof(ServerStatus.Unreachable)
                ? $"Unreachable since {Utc(server.UnreachableSince)}{(server.LastError is null ? "" : $" ({server.LastError})")}; the factory is retrying"
                : $"{server.Status}, last answered {Utc(server.LastSeenAt)}";
            // Which project the server serves, as it said itself — names like
            // `moonbeam-specs` are the same for every game, so without this
            // "is it the drones server?" has no answer here.
            var serves = server.Domain == StandardsIngestService.Domain
                ? ", shared by every project"
                : server.Scope is { } scope ? $", {scope} only" : "";
            builder.AppendLine($"- `{server.Name}` ({server.Domain}{serves}) — {standing} — {server.Url}");
        }
        builder.AppendLine();

        var imported = pack.Imports.Sum(i => i.Documents.Count);

        builder.AppendLine("## Current specifications");
        if (pack.SpecNeighborhood.Count == 0)
        {
            builder.AppendLine(imported == 0
                ? "(none yet — this project has no specifications)"
                : $"(no nodes in the graph yet — but {imported} imported document(s) are awaiting extraction; see below)");
        }
        else
        {
            foreach (var node in pack.SpecNeighborhood)
            {
                var retired = node.Retired ? " [RETIRED]" : "";
                builder.AppendLine($"- `{node.SpecId}` ({node.Kind}/{node.Layer}){retired}: {node.Text}");
            }
        }
        builder.AppendLine();

        if (pack.SpecEdges.Count > 0)
        {
            builder.AppendLine("## Relationships");
            foreach (var edge in pack.SpecEdges)
            {
                builder.AppendLine($"- `{edge.EdgeId}`: `{edge.FromSpecId}` {edge.Kind} `{edge.ToSpecId}`");
            }
            builder.AppendLine();
        }

        if (pack.Imports.Count > 0)
        {
            builder.AppendLine("## Imported, not yet in the graph");
            builder.AppendLine("These documents are this project's existing specifications. They become graph nodes");
            builder.AppendLine("only through intake; until then, discuss them from these summaries and do not restate them as nodes.");
            foreach (var import in pack.Imports)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"### Import \"{import.Name}\"{(import.Source is null ? "" : $" from `{import.Source}`")} — " +
                    $"{import.Documents.Count} document(s): {import.Extracted} extracted, {import.Proposed} proposed, " +
                    $"{import.OpenQuestions} open question(s)");
                foreach (var document in import.Documents)
                {
                    var summary = document.Summary == "" ? "" : $": {document.Summary}";
                    builder.AppendLine($"- `{document.SourceRef}` — {document.Title} ({document.Status}){summary}");
                }
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Utc(DateTimeOffset? at) =>
        at is { } value ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'") : "never";

    /// <summary>
    /// The retry. The model gets the actual violations rather than "that
    /// was invalid": schema errors and referential errors read identically
    /// here, because from the model's side they are the same kind of
    /// mistake — it produced something the factory cannot accept, and it
    /// needs to know precisely what.
    /// </summary>
    public static string RetryMessage(string previousResponse, SchemaValidationResult validation) =>
        $"""
         Your previous response could not be accepted.

         You replied:
         {Truncate(previousResponse)}

         These are the problems with it:
         {validation.AsBulletList()}

         Reply again with a single JSON object in the required format, correcting every
         problem listed. If you cannot produce a valid diff, set "settled" to false and
         "diff" to null, and explain in "reply" what you would need in order to proceed.
         """;

    /// <summary>Serialized ADR-0021 payloads for a turn that produced no valid proposal.</summary>
    public static string CouldNotProposeMarkdown(SchemaValidationResult validation) =>
        // Paragraphs are single lines: this is markdown, so it soft-wraps
        // when rendered, and hard-wrapping it here would split phrases
        // across lines for anyone reading or matching the raw text.
        $"""
         I could not express that as a valid amendment to the specification graph, so I have not proposed one — an invalid amendment is worse than none.

         What went wrong on the second attempt:

         {validation.AsBulletList()}

         Try rephrasing what you want, or ask me to propose a smaller change.
         """;

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000] + "…";

    /// <summary>
    /// Extracts the JSON object from a model response. Models wrap JSON in
    /// fences often enough that failing on it would be pedantry rather than
    /// safety — the content still has to validate, so accepting a fence
    /// costs nothing.
    /// </summary>
    public static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstNewline + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);
        return (closing < 0 ? body : body[..closing]).Trim();
    }
}

/// <summary>The architect's response envelope, before any of it is trusted.</summary>
public sealed record ArchitectResponse(string Reply, bool Settled, JsonElement? Diff, IReadOnlyList<DecisionPayload>? Decisions = null);

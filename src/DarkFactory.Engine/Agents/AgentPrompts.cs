using System.Text;
using System.Text.Json.Serialization;
using DarkFactory.Core;

namespace DarkFactory.Engine.Agents;

// What the plan and implement agents are asked for, and what they are asked
// to return. Narrow shapes on purpose: everything downstream treats model
// output as untrusted input, so the less latitude the format allows, the
// fewer ways a plausible-looking answer can be wrong in a way nothing
// catches.

public sealed record PlanStepDocument
{
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("target_paths")] public IReadOnlyList<string> TargetPaths { get; init; } = [];
    [JsonPropertyName("spec_ids")] public IReadOnlyList<string> SpecIds { get; init; } = [];
}

public sealed record PlanDocument
{
    [JsonPropertyName("steps")] public required IReadOnlyList<PlanStepDocument> Steps { get; init; }
    [JsonPropertyName("notes")] public string? Notes { get; init; }
}

public sealed record ImplementFileDocument
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
}

public sealed record ImplementDocument
{
    [JsonPropertyName("files")] public required IReadOnlyList<ImplementFileDocument> Files { get; init; }
    [JsonPropertyName("commit_message")] public required string CommitMessage { get; init; }
}

/// <summary>
/// The stage agents' prompts. Each is split into a stable prefix — the
/// role, the rules, the response contract — and a variable part carrying
/// the specs, the steers and the prior artifacts, so a run's repeated calls
/// pay for the stable half once (docs/adr/0032).
/// </summary>
public static class AgentPrompts
{
    public const string PlanTemplateVersion = "planner/1";
    public const string ImplementTemplateVersion = "implementer/1";

    // ---- shared -----------------------------------------------------------

    /// <summary>
    /// The specifications, steers and prior artifacts — everything that
    /// changes between calls within a run, and therefore everything that
    /// must stay out of the cacheable prefix.
    /// </summary>
    public static string Variable(StageContextPack pack)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Specifications this run implements");
        if (pack.Specs.Count == 0)
        {
            builder.AppendLine("(none — the run's snapshot is empty)");
        }
        else
        {
            foreach (var spec in pack.Specs)
            {
                builder.AppendLine($"- `{spec.SpecId}` ({spec.Kind}/{spec.Layer}): {spec.Text}");
            }
        }
        builder.AppendLine();

        if (pack.SpecEdges.Count > 0)
        {
            builder.AppendLine("## Relationships");
            foreach (var edge in pack.SpecEdges)
            {
                builder.AppendLine($"- `{edge.FromSpecId}` {edge.Kind} `{edge.ToSpecId}`");
            }
            builder.AppendLine();
        }

        if (pack.Steers.Count > 0)
        {
            // docs/adr/0015: guidance a human injected mid-run. Placed last
            // and labelled clearly, because the whole point of steering is
            // that it outranks the agent's own first instinct.
            builder.AppendLine("## Guidance from a human on this run — follow it");
            foreach (var steer in pack.Steers)
            {
                builder.AppendLine($"- {steer.Message}");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    // ---- plan -------------------------------------------------------------

    public static string PlanPrefix(StageContextPack pack)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are the planning agent for a software project maintained by Dark Factory.");
        builder.AppendLine();
        AppendStandards(builder, pack);
        builder.AppendLine("""
            Produce an ordered plan for making the code satisfy the specifications you are
            given. Each step names the files it expects to touch and the spec ids it serves.

            Keep the plan small and concrete. A step nobody could tell had been done is not
            a step. Do not plan work the specifications do not ask for.

            Reply with a single JSON object and nothing else — no prose, no code fence:

            {
              "steps": [
                { "description": "...", "target_paths": ["path/to/file"], "spec_ids": ["01..."] }
              ],
              "notes": null
            }

            Use only spec ids from the list you are given.
            """);
        return builder.ToString();
    }

    public static string PlanUserMessage(StageContextPack pack) =>
        "Plan the work needed to satisfy the specifications above.";

    // ---- implement --------------------------------------------------------

    public static string ImplementPrefix(StageContextPack pack)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are the implementing agent for a software project maintained by Dark Factory.");
        builder.AppendLine();
        AppendStandards(builder, pack);
        builder.AppendLine("""
            Write the files that make the code satisfy the specifications you are given,
            following the plan.

            Return the COMPLETE final content of every file you create or change. Never
            return a diff, a patch, or a fragment: the factory computes the diff itself, and
            a partial file would silently truncate what is already there.
            """);
        builder.AppendLine();
        builder.AppendLine(SpecReferences.Guidance);
        builder.AppendLine();
        builder.AppendLine("""
            Reply with a single JSON object and nothing else — no prose, no code fence:

            {
              "files": [ { "path": "relative/path.cs", "content": "the entire file" } ],
              "commit_message": "one line, imperative mood"
            }
            """);
        return builder.ToString();
    }

    public static string ImplementUserMessage(StageContextPack pack)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Implement the specifications above.");

        var plan = pack.PriorArtifacts.FirstOrDefault(a => a.Type == "Plan");
        if (plan is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"The plan for this run is artifact {plan.Ref}.");
        }

        return builder.ToString();
    }

    private static void AppendStandards(StringBuilder builder, StageContextPack pack)
    {
        foreach (var standard in pack.Standards)
        {
            builder.AppendLine($"## Standards ({standard.Layer}, from {standard.SourceRef})");
            builder.AppendLine(standard.Text);
            builder.AppendLine();
        }
    }
}

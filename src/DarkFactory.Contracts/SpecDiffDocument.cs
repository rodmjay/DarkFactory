using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkFactory.Contracts;

// The wire shape of a proposed amendment (contracts/schemas/specdiff.schema.json).
//
// This is what a model emits and what the ADR-0021 `spec_diff` component
// renders. It is deliberately NOT DarkFactory.Data.SpecDiff: that type
// carries canonical_text and content_json, which the factory derives, and a
// model must never be in a position to choose a node's content hash or to
// hand the factory a canonical form that disagrees with the text it
// displayed to the user.

public sealed record SpecDiffCreate
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("layer")] public required string Layer { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("rationale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rationale { get; init; }
}

public sealed record SpecDiffRevise
{
    [JsonPropertyName("spec_id")] public required string SpecId { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("rationale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rationale { get; init; }
}

public sealed record SpecDiffRetire
{
    [JsonPropertyName("spec_id")] public required string SpecId { get; init; }
    [JsonPropertyName("rationale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rationale { get; init; }
}

public sealed record SpecDiffEdgeAddDocument
{
    [JsonPropertyName("from_spec_id")] public required string FromSpecId { get; init; }
    [JsonPropertyName("to_spec_id")] public required string ToSpecId { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("rationale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rationale { get; init; }
}

public sealed record SpecDiffEdgeRetireDocument
{
    [JsonPropertyName("edge_id")] public required string EdgeId { get; init; }
    [JsonPropertyName("rationale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rationale { get; init; }
}

public sealed record SpecDiffDocument
{
    [JsonPropertyName("creates")] public required IReadOnlyList<SpecDiffCreate> Creates { get; init; }
    [JsonPropertyName("revises")] public required IReadOnlyList<SpecDiffRevise> Revises { get; init; }
    [JsonPropertyName("retires")] public required IReadOnlyList<SpecDiffRetire> Retires { get; init; }
    [JsonPropertyName("edge_adds")] public required IReadOnlyList<SpecDiffEdgeAddDocument> EdgeAdds { get; init; }
    [JsonPropertyName("edge_retires")] public required IReadOnlyList<SpecDiffEdgeRetireDocument> EdgeRetires { get; init; }

    public static SpecDiffDocument Empty { get; } = new()
    {
        Creates = [], Revises = [], Retires = [], EdgeAdds = [], EdgeRetires = [],
    };

    [JsonIgnore]
    public bool IsEmpty =>
        Creates.Count == 0 && Revises.Count == 0 && Retires.Count == 0
        && EdgeAdds.Count == 0 && EdgeRetires.Count == 0;

    [JsonIgnore]
    public int ChangeCount => Creates.Count + Revises.Count + Retires.Count + EdgeAdds.Count + EdgeRetires.Count;
}

/// <summary>
/// The gate in front of <c>df.specs.propose</c>. Model output is parsed,
/// never trusted: a proposal is persisted only if it validates here, and a
/// failure produces the violations verbatim so they can be fed back into
/// the model's next attempt.
/// </summary>
public static class SpecDiffSchema
{
    private static readonly SchemaValidator Validator =
        new("DarkFactory.Contracts.Schemas.specdiff.schema.json");

    public static string SchemaText => Validator.SchemaText;

    public static SchemaValidationResult Validate(string json) => Validator.Validate(json);

    public static SchemaValidationResult Validate(JsonElement element) => Validator.Validate(element);

    /// <summary>
    /// Validates, then binds. Leaves <paramref name="document"/> null on
    /// failure so an invalid diff cannot be used by accident.
    /// </summary>
    public static SchemaValidationResult TryParse(string json, out SpecDiffDocument? document)
    {
        document = null;

        var validation = Validator.Validate(json);
        if (!validation.IsValid)
        {
            return validation;
        }

        document = JsonSerializer.Deserialize<SpecDiffDocument>(json)!;
        return validation;
    }
}

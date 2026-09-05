using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DarkFactory.Contracts;

/// <summary>
/// The response to <c>df.describe()</c> — the mandatory handshake from
/// docs/adr/0018. Mirrors contracts/schemas/describe.schema.json, which is
/// the actual contract: this type is a convenience for reading a response
/// that has <em>already</em> been validated against the schema. Never treat
/// a successful deserialization as validation (see
/// <see cref="DescribeSchema"/>) — System.Text.Json will happily bind a
/// document missing every required field.
/// </summary>
public sealed record DescribeResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("convention_version")]
    public required string ConventionVersion { get; init; }

    /// <summary>Every version this server can speak, when it speaks more than one (docs/adr/0020).</summary>
    [JsonPropertyName("convention_versions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ConventionVersions { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("capabilities")]
    public required IReadOnlyList<string> Capabilities { get; init; }

    [JsonPropertyName("requires")]
    public required IReadOnlyList<string> Requires { get; init; }

    /// <summary>
    /// What this instance is pointed at. Stored in the registry and
    /// rendered in the dashboard, so it must never carry secrets.
    /// </summary>
    [JsonPropertyName("effective_config")]
    public required IReadOnlyDictionary<string, JsonElement> EffectiveConfig { get; init; }

    /// <summary>
    /// The forward-compatibility escape hatch (docs/adr/0020). Unknown
    /// top-level properties are rejected — that is what catches a
    /// <c>capabilties</c> typo — so a newer convention version puts fields
    /// an older factory has never heard of in here instead, where they are
    /// ignored rather than fatal.
    /// </summary>
    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>One schema violation, in a form that reads usefully in an error message.</summary>
public sealed record DescribeValidationError(string Location, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(Location) ? Message : $"{Location}: {Message}";
}

public sealed record DescribeValidationResult(bool IsValid, IReadOnlyList<DescribeValidationError> Errors)
{
    public static DescribeValidationResult Valid { get; } = new(true, []);

    /// <summary>A single line listing every violation, for the registration error the caller sees.</summary>
    public string Summarize() => string.Join("; ", Errors.Select(e => e.ToString()));
}

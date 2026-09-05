namespace DarkFactory.Contracts;

using System.Text.Json;

/// <summary>
/// Validates a <c>df.describe()</c> response against the published schema
/// (contracts/schemas/describe.schema.json).
///
/// This is the gate in front of the registry: a server that answers the
/// handshake but answers it wrongly is rejected, and the caller is told
/// exactly which parts were wrong. Deserializing into
/// <see cref="DescribeResponse"/> is not a substitute — System.Text.Json
/// enforces required members but not shape, so a document with a non-semver
/// version, a capability outside the df. root and a nonexistent domain
/// binds perfectly well.
/// </summary>
public static class DescribeSchema
{
    private static readonly SchemaValidator Validator =
        new("DarkFactory.Contracts.Schemas.describe.schema.json");

    public static string SchemaText => Validator.SchemaText;

    public static SchemaValidationResult Validate(string json) => Validator.Validate(json);

    public static SchemaValidationResult Validate(JsonElement element) => Validator.Validate(element);

    /// <summary>
    /// Parses and validates in one step. Leaves <paramref name="response"/>
    /// null whenever validation failed, so a caller cannot accidentally use
    /// an unvalidated response.
    /// </summary>
    public static SchemaValidationResult TryParse(string json, out DescribeResponse? response)
    {
        response = null;

        var validation = Validator.Validate(json);
        if (!validation.IsValid)
        {
            return validation;
        }

        response = JsonSerializer.Deserialize<DescribeResponse>(json)!;
        return validation;
    }
}

using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace DarkFactory.Contracts;

/// <summary>
/// Validates a <c>df.describe()</c> response against the published schema
/// (contracts/schemas/describe.schema.json), embedded in this assembly so
/// validation uses the same bytes the repo publishes.
///
/// This is the gate in front of the registry: a server that answers the
/// handshake but answers it wrongly is rejected, and the caller is told
/// exactly which parts were wrong. Deserializing into
/// <see cref="DescribeResponse"/> is not a substitute — binding succeeds on
/// documents this schema rejects.
/// </summary>
public static class DescribeSchema
{
    private const string ResourceName = "DarkFactory.Contracts.Schemas.describe.schema.json";

    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    private static readonly EvaluationOptions Options = new()
    {
        // The list format gives one flat entry per violation with its
        // instance location, which is what makes a registration failure
        // message actionable instead of a nested blob.
        OutputFormat = OutputFormat.List,
    };

    public static string SchemaText
    {
        get
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>Validates raw JSON text. Unparseable input is a validation failure, not an exception.</summary>
    public static DescribeValidationResult Validate(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new DescribeValidationResult(false, [new DescribeValidationError("", $"not valid JSON: {ex.Message}")]);
        }

        using (document)
        {
            return Validate(document.RootElement);
        }
    }

    public static DescribeValidationResult Validate(JsonElement element)
    {
        var results = Schema.Value.Evaluate(element, Options);
        if (results.IsValid)
        {
            return DescribeValidationResult.Valid;
        }

        var errors = Flatten(results).ToList();
        if (errors.Count == 0)
        {
            // Belt and braces: an invalid result with no annotated errors
            // would otherwise produce an empty "here's what was wrong".
            errors.Add(new DescribeValidationError("", "did not match the describe schema"));
        }

        return new DescribeValidationResult(false, errors);
    }

    /// <summary>
    /// Parses and validates in one step. Returns null for
    /// <paramref name="response"/> whenever validation failed, so callers
    /// cannot accidentally use an unvalidated response.
    /// </summary>
    public static DescribeValidationResult TryParse(string json, out DescribeResponse? response)
    {
        response = null;

        var validation = Validate(json);
        if (!validation.IsValid)
        {
            return validation;
        }

        response = JsonSerializer.Deserialize<DescribeResponse>(json)!;
        return validation;
    }

    private static IEnumerable<DescribeValidationError> Flatten(EvaluationResults results)
    {
        if (results.Errors is not null)
        {
            foreach (var (keyword, message) in results.Errors)
            {
                var location = results.InstanceLocation.ToString();
                yield return new DescribeValidationError(
                    string.IsNullOrEmpty(location) ? "(root)" : location,
                    $"{message} [{keyword}]");
            }
        }

        if (results.Details is null)
        {
            yield break;
        }

        foreach (var detail in results.Details)
        {
            foreach (var error in Flatten(detail))
            {
                yield return error;
            }
        }
    }
}

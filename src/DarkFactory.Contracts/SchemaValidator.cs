using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace DarkFactory.Contracts;

/// <summary>
/// Validates JSON against one of the published schemas in
/// <c>contracts/schemas/</c>, embedded in this assembly so validation uses
/// the same bytes the repo publishes.
///
/// Two things are validated this way and they are validated for the same
/// reason: a <c>df.describe</c> response comes from a third-party server,
/// and a spec diff comes from a language model. Neither is trusted, both
/// get told precisely what was wrong, and in both cases nothing is stored
/// until it passes.
/// </summary>
public sealed class SchemaValidator(string resourceName)
{
    private readonly Lazy<JsonSchema> _schema = new(() => JsonSchema.FromText(ReadResource(resourceName)));

    private static readonly EvaluationOptions Options = new()
    {
        // One flat entry per violation with its instance location, which is
        // what makes a failure message actionable rather than a nested blob
        // — and, for the model retry path, what gets fed back into the
        // prompt.
        OutputFormat = OutputFormat.List,
    };

    public string SchemaText => ReadResource(resourceName);

    /// <summary>Validates raw JSON text. Unparseable input is a validation failure, not an exception.</summary>
    public SchemaValidationResult Validate(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new SchemaValidationResult(false, [new SchemaValidationError("", $"not valid JSON: {ex.Message}")]);
        }

        using (document)
        {
            return Validate(document.RootElement);
        }
    }

    public SchemaValidationResult Validate(JsonElement element)
    {
        var results = _schema.Value.Evaluate(element, Options);
        if (results.IsValid)
        {
            return SchemaValidationResult.Valid;
        }

        var errors = Flatten(results).ToList();
        if (errors.Count == 0)
        {
            // Belt and braces: an invalid result with no annotated errors
            // would otherwise produce an empty "here's what was wrong".
            errors.Add(new SchemaValidationError("", "did not match the schema"));
        }

        return new SchemaValidationResult(false, errors);
    }

    private static string ReadResource(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<SchemaValidationError> Flatten(EvaluationResults results)
    {
        if (results.Errors is not null)
        {
            foreach (var (keyword, message) in results.Errors)
            {
                var location = results.InstanceLocation.ToString();
                yield return new SchemaValidationError(
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

/// <summary>One schema violation, in a form that reads usefully in an error message.</summary>
public sealed record SchemaValidationError(string Location, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(Location) ? Message : $"{Location}: {Message}";
}

public sealed record SchemaValidationResult(bool IsValid, IReadOnlyList<SchemaValidationError> Errors)
{
    public static SchemaValidationResult Valid { get; } = new(true, []);

    /// <summary>A single line listing every violation, for an error the caller sees.</summary>
    public string Summarize() => string.Join("; ", Errors.Select(e => e.ToString()));

    /// <summary>
    /// A bulleted list, for feeding back into a model prompt on retry.
    /// Fed verbatim: the model gets the same information a human author
    /// would, rather than a vague "that was invalid, try again".
    /// </summary>
    public string AsBulletList() => string.Join("\n", Errors.Select(e => $"- {e}"));
}

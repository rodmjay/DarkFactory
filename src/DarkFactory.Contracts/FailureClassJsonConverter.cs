using System.Text.Json;
using System.Text.Json.Serialization;
using DarkFactory.Core;

namespace DarkFactory.Contracts;

/// <summary>
/// Serializes <see cref="FailureClass"/> as the lowercase/snake_case strings
/// docs/conventions/envelope.md and contracts/schemas/hookresult.schema.json
/// actually specify ("retryable" | "permanent" | "needs_human") — not the
/// C# enum member names, which a plain JsonStringEnumConverter would emit
/// as "Retryable" / "Permanent" / "NeedsHuman" and mismatch the published
/// convention every spoke server is written against.
/// </summary>
public sealed class FailureClassJsonConverter : JsonConverter<FailureClass>
{
    public override FailureClass Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "retryable" => FailureClass.Retryable,
            "permanent" => FailureClass.Permanent,
            "needs_human" => FailureClass.NeedsHuman,
            var other => throw new JsonException($"Unknown failure_class '{other}'."),
        };

    public override void Write(Utf8JsonWriter writer, FailureClass value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            FailureClass.Retryable => "retryable",
            FailureClass.Permanent => "permanent",
            FailureClass.NeedsHuman => "needs_human",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        });
}

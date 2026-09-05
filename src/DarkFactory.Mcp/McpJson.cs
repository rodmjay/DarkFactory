using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DarkFactory.Mcp;

/// <summary>
/// How the front MCP surface serializes tool results.
///
/// snake_case, matching every published schema in <c>contracts/schemas/</c>.
/// A tool result is the same wire those schemas describe, and a caller
/// should not have to know which half of a response was hand-modelled and
/// which was generated from a C# record.
///
/// Types with explicit <c>[JsonPropertyName]</c> attributes — the
/// schema-backed contracts in DarkFactory.Contracts — keep their own names,
/// because an attribute beats a policy. That is the intended precedence: a
/// published contract is fixed, and this only decides the shape of
/// everything that has not been given one.
/// </summary>
public static class McpJson
{
    public static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Required, not optional. The SDK marks these options read-only, and
        // a read-only JsonSerializerOptions with no resolver throws at the
        // first use — which is during startup, so the host does not boot at
        // all. Constructing from JsonSerializerDefaults.Web does not supply
        // one.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}

using System.Text.Json;

namespace DarkFactory.Data;

/// <summary>
/// Which part of a workspace a server serves, read from what it said in its
/// own handshake (docs/adr/0038, as amended). A person asking "is this
/// connected to the drones server?" could not tell from the names —
/// `moonbeam-specs` is the same name for every game — so the answer is
/// shown wherever servers are.
///
/// Null means the server serves no single project: a standards server
/// (shared by the org, by design) or a server that named none.
/// </summary>
public static class ServerScope
{
    public static string? Of(string domain, string? liveDescribeJson)
    {
        if (string.Equals(domain, StandardsIngestService.Domain, StringComparison.Ordinal) || liveDescribeJson is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(liveDescribeJson);
            if (!TryGet(document.RootElement, "effective_config", "EffectiveConfig", out var config))
            {
                return null;
            }

            // A corpus server says which project; a workspace names the repo.
            if (TryString(config, "project") is { } project)
            {
                return project;
            }

            return string.Equals(domain, "workspace", StringComparison.Ordinal) ? TryString(config, "name") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGet(JsonElement element, string name, string alternative, out JsonElement value) =>
        element.TryGetProperty(name, out value) || element.TryGetProperty(alternative, out value);

    private static string? TryString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}

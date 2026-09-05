using System.Text.Json.Serialization;
using DarkFactory.Contracts;

namespace DarkFactory.Data;

public sealed record FieldDisagreement(
    [property: JsonPropertyName("manifest")] string Manifest,
    [property: JsonPropertyName("live")] string Live);

/// <summary>
/// What the static manifest claims versus what <c>df.describe()</c> actually
/// reported (docs/adr/0018). Computed once at registration and stored on the
/// server row — a disagreement the dashboard shows must be the one
/// registration observed, not one recomputed later against a describe
/// response that may have moved since.
/// </summary>
public sealed record ManifestLiveDiff(
    [property: JsonPropertyName("missing_capabilities")] IReadOnlyList<string> MissingCapabilities,
    [property: JsonPropertyName("extra_capabilities")] IReadOnlyList<string> ExtraCapabilities,
    [property: JsonPropertyName("missing_requires")] IReadOnlyList<string> MissingRequires,
    [property: JsonPropertyName("extra_requires")] IReadOnlyList<string> ExtraRequires,
    [property: JsonPropertyName("domain")] FieldDisagreement? Domain,
    [property: JsonPropertyName("convention_version")] FieldDisagreement? ConventionVersion)
{
    /// <summary>
    /// Derived, never stored: persisting it would put a value in the
    /// database that could contradict the fields it is computed from.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty =>
        MissingCapabilities.Count == 0
        && ExtraCapabilities.Count == 0
        && MissingRequires.Count == 0
        && ExtraRequires.Count == 0
        && Domain is null
        && ConventionVersion is null;

    public static ManifestLiveDiff Compute(DescribeResponse manifest, DescribeResponse live)
    {
        var manifestCaps = manifest.Capabilities.ToHashSet(StringComparer.Ordinal);
        var liveCaps = live.Capabilities.ToHashSet(StringComparer.Ordinal);
        var manifestRequires = manifest.Requires.ToHashSet(StringComparer.Ordinal);
        var liveRequires = live.Requires.ToHashSet(StringComparer.Ordinal);

        return new ManifestLiveDiff(
            MissingCapabilities: Sorted(manifestCaps.Except(liveCaps)),
            ExtraCapabilities: Sorted(liveCaps.Except(manifestCaps)),
            MissingRequires: Sorted(manifestRequires.Except(liveRequires)),
            ExtraRequires: Sorted(liveRequires.Except(manifestRequires)),
            Domain: Disagreement(manifest.Domain, live.Domain),
            ConventionVersion: Disagreement(manifest.ConventionVersion, live.ConventionVersion));
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) =>
        values.OrderBy(v => v, StringComparer.Ordinal).ToList();

    private static FieldDisagreement? Disagreement(string manifest, string live) =>
        string.Equals(manifest, live, StringComparison.Ordinal) ? null : new FieldDisagreement(manifest, live);

    /// <summary>A one-line human summary, used in list output and event payloads.</summary>
    public string Summarize()
    {
        var parts = new List<string>();
        if (MissingCapabilities.Count > 0)
        {
            parts.Add($"claims but does not report: {string.Join(", ", MissingCapabilities)}");
        }
        if (ExtraCapabilities.Count > 0)
        {
            parts.Add($"reports but does not claim: {string.Join(", ", ExtraCapabilities)}");
        }
        if (MissingRequires.Count > 0)
        {
            parts.Add($"requires claimed but not reported: {string.Join(", ", MissingRequires)}");
        }
        if (ExtraRequires.Count > 0)
        {
            parts.Add($"requires reported but not claimed: {string.Join(", ", ExtraRequires)}");
        }
        if (Domain is not null)
        {
            parts.Add($"domain: manifest '{Domain.Manifest}' vs live '{Domain.Live}'");
        }
        if (ConventionVersion is not null)
        {
            parts.Add($"convention_version: manifest '{ConventionVersion.Manifest}' vs live '{ConventionVersion.Live}'");
        }
        return parts.Count == 0 ? "manifest and live describe agree" : string.Join("; ", parts);
    }
}

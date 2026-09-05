using System.Security.Cryptography;
using System.Text;
using DarkFactory.Core;

namespace DarkFactory.Data;

public sealed class ArtifactUrlOptions
{
    public const string SectionName = "Artifacts";

    /// <summary>
    /// The base URL a workspace server can reach the factory on. Inside
    /// Compose that is the service name (<c>http://factory:8080</c>); in
    /// hosted mode it is the public ingress. It is deliberately not derived
    /// from the incoming request: the URL is minted for a *different* host
    /// to fetch, and what the factory looks like from the outside is not
    /// something an inbound MCP call can tell us.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// HMAC key for signing. A missing key is fatal rather than defaulted:
    /// a predictable signing key is the same as no signing at all, and this
    /// endpoint serves customer code.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Short. The URL exists to be handed to one server for one call; it
    /// has no reason to outlive the stage that minted it, and a long-lived
    /// bearer URL for customer code is a liability.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed record ArtifactUrlClaims(string ArtifactId, string OrgId, string ProjectId, long ExpiresAtUnix);

public enum ArtifactUrlStatus { Valid, BadSignature, Expired, WrongScope }

/// <summary>
/// Turns <c>factory://artifacts/{id}</c> into a URL a workspace server can
/// actually fetch, and validates it on the way back in.
///
/// This is the first thing that lets a spoke pull from the hub
/// (docs/adr/0001), so it carries the same tenancy rules as everything else
/// (docs/adr/0010, docs/adr/0011): the signature covers the org and project
/// as well as the artifact, and the endpoint re-checks that the artifact
/// actually belongs to the scope the URL claims. A signed URL is therefore
/// only ever good for one artifact, for one project, for a few minutes —
/// it is not a capability to read the artifact store.
/// </summary>
public sealed class ArtifactUrlSigner
{
    private readonly ArtifactUrlOptions _options;

    public ArtifactUrlSigner(ArtifactUrlOptions options)
    {
        _options = options;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                $"{ArtifactUrlOptions.SectionName}:SigningKey is not configured. Artifact URLs are handed to " +
                "workspace servers to fetch customer code with; an unsigned or predictably-signed one is not " +
                "a URL, it is an open door.");
        }

        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            throw new InvalidOperationException(
                $"{ArtifactUrlOptions.SectionName}:PublicBaseUrl is not configured. It is the address a " +
                "workspace server reaches this factory on — inside Compose, http://factory:8080.");
        }
    }

    public const string RoutePrefix = "/artifacts";

    /// <summary>Mints a scoped, expiring URL for one artifact.</summary>
    public string Sign(Artifact artifact, DateTimeOffset? now = null)
    {
        var expires = (now ?? DateTimeOffset.UtcNow).Add(_options.Lifetime).ToUnixTimeSeconds();
        var claims = new ArtifactUrlClaims(artifact.Id, artifact.OrgId, artifact.ProjectId, expires);

        var baseUrl = _options.PublicBaseUrl!.TrimEnd('/');
        return $"{baseUrl}{RoutePrefix}/{Uri.EscapeDataString(artifact.Id)}" +
               $"?org={Uri.EscapeDataString(claims.OrgId)}" +
               $"&project={Uri.EscapeDataString(claims.ProjectId)}" +
               $"&exp={claims.ExpiresAtUnix}" +
               $"&sig={Compute(claims)}";
    }

    /// <summary>
    /// Checks the signature and expiry. Deliberately does not look at the
    /// database — <see cref="ScopeMatches"/> is the second half, and
    /// separating them keeps "is this URL genuine" and "does it point where
    /// it claims" from being conflated.
    /// </summary>
    public ArtifactUrlStatus Validate(ArtifactUrlClaims claims, string signature, DateTimeOffset? now = null)
    {
        var expected = Compute(claims);

        // Fixed-time comparison: a signature check that leaks timing is a
        // signature check that can be walked byte by byte.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature)))
        {
            return ArtifactUrlStatus.BadSignature;
        }

        return DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAtUnix) < (now ?? DateTimeOffset.UtcNow)
            ? ArtifactUrlStatus.Expired
            : ArtifactUrlStatus.Valid;
    }

    /// <summary>
    /// The artifact must really live in the org and project the URL claims.
    /// Without this a validly-signed URL for one project would still serve
    /// an artifact whose id happened to be guessed from another — the
    /// signature proves we minted it, not that it points where it says.
    /// </summary>
    public static bool ScopeMatches(Artifact artifact, ArtifactUrlClaims claims) =>
        string.Equals(artifact.OrgId, claims.OrgId, StringComparison.Ordinal)
        && string.Equals(artifact.ProjectId, claims.ProjectId, StringComparison.Ordinal);

    private string Compute(ArtifactUrlClaims claims)
    {
        // Newline-separated with no field able to contain a newline, so two
        // different claim sets cannot produce the same signing input.
        var payload = $"{claims.ArtifactId}\n{claims.OrgId}\n{claims.ProjectId}\n{claims.ExpiresAtUnix}";
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.SigningKey!), Encoding.UTF8.GetBytes(payload));
        return Base64Url(mac);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

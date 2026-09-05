using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The signed URL is the whole authorisation for an artifact fetch: a
/// workspace server holds no factory credential, so if the URL is wrong the
/// factory's artifact store is exposed. These tests are about what the
/// signature actually binds.
/// </summary>
public sealed class ArtifactUrlTests
{
    private static ArtifactUrlSigner Signer(TimeSpan? lifetime = null) => new(new ArtifactUrlOptions
    {
        PublicBaseUrl = "http://factory:8080",
        SigningKey = "test-signing-key",
        Lifetime = lifetime ?? TimeSpan.FromMinutes(15),
    });

    private static Artifact Artifact(string id = "a1", string org = "org_1", string project = "proj_1") => new()
    {
        Id = id,
        OrgId = org,
        ProjectId = project,
        RunId = "run_1",
        Type = "ChangeSet",
        ContentJson = "diff --git a/x b/x",
        ContentType = ArtifactContentTypes.Patch,
        Sha256 = new string('0', 64),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static (ArtifactUrlClaims Claims, string Signature) Parse(string url)
    {
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Last());
        return (new ArtifactUrlClaims(id, query["org"]!, query["project"]!, long.Parse(query["exp"]!)),
                query["sig"]!);
    }

    [Fact]
    public void AMintedUrlValidates()
    {
        var (claims, signature) = Parse(Signer().Sign(Artifact()));
        Assert.Equal(ArtifactUrlStatus.Valid, Signer().Validate(claims, signature));
    }

    [Fact]
    public void TheUrlPointsAtTheConfiguredBaseNotTheRequestHost()
    {
        // It is minted for a *different* host to fetch. What the factory
        // looks like from the outside is not something an inbound call can
        // tell us.
        Assert.StartsWith("http://factory:8080/artifacts/", Signer().Sign(Artifact()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("org")]
    [InlineData("project")]
    [InlineData("artifact")]
    [InlineData("expiry")]
    public void EveryClaimIsCoveredBySignature(string tampered)
    {
        var url = Signer().Sign(Artifact());
        var (claims, signature) = Parse(url);

        var forged = tampered switch
        {
            "org" => claims with { OrgId = "org_2" },
            "project" => claims with { ProjectId = "proj_2" },
            "artifact" => claims with { ArtifactId = "a2" },
            _ => claims with { ExpiresAtUnix = claims.ExpiresAtUnix + 86_400 },
        };

        // Editing any part of a URL you were handed invalidates it. Without
        // this, a URL for your own project would be a lever for reading
        // someone else's.
        Assert.Equal(ArtifactUrlStatus.BadSignature, Signer().Validate(forged, signature));
    }

    [Fact]
    public void AnExpiredUrlIsRejected()
    {
        var signer = Signer(TimeSpan.FromMinutes(15));
        var (claims, signature) = Parse(signer.Sign(Artifact()));

        Assert.Equal(ArtifactUrlStatus.Expired,
            signer.Validate(claims, signature, DateTimeOffset.UtcNow.AddMinutes(16)));
    }

    [Fact]
    public void ADifferentKeyDoesNotValidate()
    {
        var (claims, signature) = Parse(Signer().Sign(Artifact()));

        var otherFactory = new ArtifactUrlSigner(new ArtifactUrlOptions
        {
            PublicBaseUrl = "http://factory:8080",
            SigningKey = "a different key",
        });

        Assert.Equal(ArtifactUrlStatus.BadSignature, otherFactory.Validate(claims, signature));
    }

    [Fact]
    public void ScopeIsCheckedAgainstTheArtifactNotJustTheSignature()
    {
        // A genuinely-signed URL still has to point where it claims. The
        // signature proves the factory minted it; only this proves it names
        // the artifact it says it does.
        var claims = new ArtifactUrlClaims("a1", "org_1", "proj_1", 0);

        Assert.True(ArtifactUrlSigner.ScopeMatches(Artifact(org: "org_1", project: "proj_1"), claims));
        Assert.False(ArtifactUrlSigner.ScopeMatches(Artifact(org: "org_2", project: "proj_1"), claims));
        Assert.False(ArtifactUrlSigner.ScopeMatches(Artifact(org: "org_1", project: "proj_2"), claims));
    }

    [Fact]
    public void RefusingToStartWithoutAKeyIsTheWholePoint()
    {
        var noKey = Assert.Throws<InvalidOperationException>(() => new ArtifactUrlSigner(
            new ArtifactUrlOptions { PublicBaseUrl = "http://factory:8080" }));
        Assert.Contains("SigningKey", noKey.Message, StringComparison.Ordinal);

        var noBase = Assert.Throws<InvalidOperationException>(() => new ArtifactUrlSigner(
            new ArtifactUrlOptions { SigningKey = "k" }));
        Assert.Contains("PublicBaseUrl", noBase.Message, StringComparison.Ordinal);
    }
}

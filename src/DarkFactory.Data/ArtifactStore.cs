using System.Security.Cryptography;
using System.Text;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// Where "artifacts by reference" (docs/adr/0004) actually resolve to. v1
/// stores bodies inline in Postgres, content-addressed by Sha256; hosted
/// mode can swap the body storage for blob storage behind this same
/// interface without any caller changing how it passes refs around.
/// Signed URLs are stubbed as "factory://artifacts/{id}" locally — nothing
/// resolves that scheme over the network yet.
/// </summary>
public interface IArtifactStore
{
    Task<Artifact> PutAsync(
        string orgId,
        string projectId,
        string runId,
        string type,
        string contentJson,
        CancellationToken cancellationToken = default);

    Task<Artifact?> GetAsync(string idOrRef, CancellationToken cancellationToken = default);
}

public static class ArtifactRef
{
    private const string Scheme = "factory://artifacts/";

    public static string Format(string artifactId) => Scheme + artifactId;

    /// <summary>Accepts either a bare artifact id or a "factory://artifacts/{id}" ref.</summary>
    public static string ToId(string idOrRef) =>
        idOrRef.StartsWith(Scheme, StringComparison.Ordinal) ? idOrRef[Scheme.Length..] : idOrRef;
}

public sealed class PostgresArtifactStore(DarkFactoryDbContext dbContext) : IArtifactStore
{
    public async Task<Artifact> PutAsync(
        string orgId,
        string projectId,
        string runId,
        string type,
        string contentJson,
        CancellationToken cancellationToken = default)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid().ToString("n"),
            OrgId = orgId,
            ProjectId = projectId,
            RunId = runId,
            Type = type,
            ContentJson = contentJson,
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentJson))).ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Artifacts.Add(artifact);
        await dbContext.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    public Task<Artifact?> GetAsync(string idOrRef, CancellationToken cancellationToken = default) =>
        dbContext.Artifacts.FirstOrDefaultAsync(a => a.Id == ArtifactRef.ToId(idOrRef), cancellationToken);
}

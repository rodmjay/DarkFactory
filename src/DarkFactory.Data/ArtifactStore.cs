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
    /// <summary>Stores an artifact produced by a run stage (docs/adr/0004).</summary>
    Task<Artifact> PutAsync(
        string orgId,
        string projectId,
        string runId,
        string type,
        string contentJson,
        string contentType = ArtifactContentTypes.Json,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an artifact belonging to a conversation rather than a run —
    /// a ContextPack. Separate method rather than a nullable runId on the
    /// one above, so a caller cannot pass null by accident and silently
    /// orphan a run artifact.
    /// </summary>
    Task<Artifact> PutForConversationAsync(
        string orgId,
        string projectId,
        string conversationId,
        string type,
        string contentJson,
        CancellationToken cancellationToken = default);

    Task<Artifact?> GetAsync(string idOrRef, CancellationToken cancellationToken = default);
}

public static class ArtifactContentTypes
{
    public const string Json = "application/json";

    /// <summary>A unified diff, served verbatim so `git apply` can read it off the wire.</summary>
    public const string Patch = "text/x-patch";

    public const string Markdown = "text/markdown";
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
    public Task<Artifact> PutAsync(
        string orgId,
        string projectId,
        string runId,
        string type,
        string contentJson,
        string contentType = ArtifactContentTypes.Json,
        CancellationToken cancellationToken = default) =>
        StoreAsync(orgId, projectId, runId, null, type, contentJson, contentType, cancellationToken);

    public Task<Artifact> PutForConversationAsync(
        string orgId,
        string projectId,
        string conversationId,
        string type,
        string contentJson,
        CancellationToken cancellationToken = default) =>
        StoreAsync(orgId, projectId, null, conversationId, type, contentJson,
            ArtifactContentTypes.Json, cancellationToken);

    private async Task<Artifact> StoreAsync(
        string orgId,
        string projectId,
        string? runId,
        string? conversationId,
        string type,
        string contentJson,
        string contentType,
        CancellationToken cancellationToken)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid().ToString("n"),
            OrgId = orgId,
            ProjectId = projectId,
            RunId = runId,
            ConversationId = conversationId,
            Type = type,
            ContentJson = contentJson,
            ContentType = contentType,
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

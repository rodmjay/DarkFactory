using System.Text;
using DarkFactory.Data;
using Microsoft.AspNetCore.Mvc;

namespace DarkFactory.Mcp.Endpoints;

/// <summary>
/// Serves artifact bodies to workspace servers over HTTP — the first time
/// anything pulls <em>from</em> the factory rather than being called by it
/// (docs/adr/0001), and therefore the first place the hub has an inbound
/// surface that isn't MCP.
///
/// It is unauthenticated in the usual sense and deliberately so: the caller
/// is a workspace server that holds no factory credential. What stands in
/// for authentication is the signed URL, which binds one artifact to one org
/// and project for a few minutes (see <see cref="ArtifactUrlSigner"/>). The
/// endpoint therefore does two independent checks — the signature is
/// genuine, and the artifact really is in the scope the signature claims —
/// because the first alone would let a valid signature for one project
/// serve an artifact id belonging to another.
/// </summary>
public static class ArtifactEndpoints
{
    public static void MapArtifactEndpoints(this WebApplication app)
    {
        app.MapGet($"{ArtifactUrlSigner.RoutePrefix}/{{id}}", async (
            string id,
            [FromQuery] string? org,
            [FromQuery] string? project,
            [FromQuery] long? exp,
            [FromQuery] string? sig,
            ArtifactUrlSigner? signer,
            IArtifactStore artifacts,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var log = loggerFactory.CreateLogger("DarkFactory.Artifacts");

            if (signer is null)
            {
                // Artifact URLs are unconfigured, so none were ever minted,
                // so any request here is spurious.
                return Results.NotFound();
            }

            if (org is null || project is null || exp is null || sig is null)
            {
                return Results.BadRequest(new { error = "org, project, exp and sig are all required." });
            }

            var claims = new ArtifactUrlClaims(id, org, project, exp.Value);
            var status = signer.Validate(claims, sig);

            if (status != ArtifactUrlStatus.Valid)
            {
                // One response for every rejection. Distinguishing "expired"
                // from "forged" here would tell an attacker which half of
                // the URL to work on; the logs make the distinction for us.
                log.LogWarning("Rejected artifact request for {ArtifactId}: {Status}", id, status);
                return Results.NotFound();
            }

            var artifact = await artifacts.GetAsync(id, cancellationToken);
            if (artifact is null || !ArtifactUrlSigner.ScopeMatches(artifact, claims))
            {
                if (artifact is not null)
                {
                    // A genuinely-signed URL pointing outside its own scope
                    // is worth shouting about: it means either an id
                    // collision or someone editing a URL they were given.
                    log.LogWarning(
                        "Artifact {ArtifactId} is org={ActualOrg}/project={ActualProject} but the URL claimed " +
                        "org={ClaimedOrg}/project={ClaimedProject}",
                        id, artifact.OrgId, artifact.ProjectId, claims.OrgId, claims.ProjectId);
                }
                return Results.NotFound();
            }

            return Results.File(
                Encoding.UTF8.GetBytes(artifact.ContentJson),
                artifact.ContentType,
                // A patch piped into `git apply` wants a filename; the type
                // decides the extension.
                fileDownloadName: $"{artifact.Id}{Extension(artifact.ContentType)}");
        })
        .WithName("GetArtifact")
        .ExcludeFromDescription();
    }

    private static string Extension(string contentType) => contentType switch
    {
        ArtifactContentTypes.Patch => ".patch",
        ArtifactContentTypes.Markdown => ".md",
        _ => ".json",
    };
}

using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public sealed record ProjectRegistration(Project Project, Team Team, ServerRegistration Server);

/// <summary>
/// Registering a project (docs/adr/0013): bind a workspace server, derive a
/// name from what that server says about itself, and seed the project's
/// standing team (docs/adr/0028).
///
/// The team is seeded here rather than lazily, because a project that
/// exists but has nobody assigned to talk to is a setup step someone
/// forgot to automate — the first conversation would fail with a
/// configuration error that the user had no way to anticipate.
/// </summary>
public sealed class ProjectService(DarkFactoryDbContext db, ServerRegistry registry, TeamService teams)
{
    public async Task<ProjectRegistration> RegisterAsync(
        string orgId, string workspaceMcpUrl, string? name = null, CancellationToken cancellationToken = default)
    {
        // The workspace server goes through the same handshake, schema
        // validation and conformance check as any other server
        // (docs/adr/0018). A project is not allowed to be bound to
        // something the registry would have refused.
        var registration = await registry.RegisterAsync(
            orgId, workspaceMcpUrl, manifestJson: null, projectId: null, ServerTier.BuiltIn, cancellationToken);

        if (!string.Equals(registration.Live.Domain, "workspace", StringComparison.Ordinal))
        {
            throw new ServerRegistrationException(
                $"'{workspaceMcpUrl}' is a '{registration.Live.Domain}' server, not a workspace server. " +
                "A project needs somewhere to keep its code.");
        }

        var existing = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrgId == orgId && p.WorkspaceMcpUrl == workspaceMcpUrl, cancellationToken);
        if (existing is not null)
        {
            // Same idempotency rule as the registry: one workspace URL is
            // one project, and re-registering it re-seeds nothing.
            var team = await teams.SeedDefaultTeamAsync(existing.Id, orgId, cancellationToken);
            return new ProjectRegistration(existing, team, registration);
        }

        var workspace = ReadWorkspaceConfig(registration.Live);
        var desired = ProjectNaming.Slugify(name ?? workspace.Name ?? registration.Live.Name);

        var taken = (await db.Projects.AsNoTracking()
            .Where(p => p.OrgId == orgId)
            .Select(p => p.Name)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var project = new Project
        {
            Id = Ulid.NewUlid(),
            OrgId = orgId,
            Name = ProjectNaming.ResolveCollision(desired, taken),
            WorkspaceMcpUrl = workspaceMcpUrl,
            WorkspaceName = workspace.Name,
            WorkspaceRoot = workspace.Root,
            StackHints = workspace.StackHints,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        var seeded = await teams.SeedDefaultTeamAsync(project.Id, orgId, cancellationToken);
        return new ProjectRegistration(project, seeded, registration);
    }

    public async Task<IReadOnlyList<Project>> ListAsync(string orgId, CancellationToken cancellationToken = default) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.OrgId == orgId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// A workspace server puts its identity in <c>effective_config</c>
    /// (docs/conventions/workspace.md). Read defensively: effective_config
    /// is an open object by contract, so absent or wrongly-typed fields are
    /// a normal case, not a reason to fail a registration that already
    /// passed conformance.
    /// </summary>
    private static WorkspaceConfig ReadWorkspaceConfig(DescribeResponse describe)
    {
        string? name = null, root = null;
        string[] stackHints = [];

        if (describe.EffectiveConfig.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String)
        {
            name = n.GetString();
        }
        if (describe.EffectiveConfig.TryGetValue("root", out var r) && r.ValueKind == JsonValueKind.String)
        {
            root = r.GetString();
        }
        if (describe.EffectiveConfig.TryGetValue("stack_hints", out var s) && s.ValueKind == JsonValueKind.Array)
        {
            stackHints = s.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();
        }

        return new WorkspaceConfig(name, root, stackHints);
    }

    private sealed record WorkspaceConfig(string? Name, string? Root, string[] StackHints);
}

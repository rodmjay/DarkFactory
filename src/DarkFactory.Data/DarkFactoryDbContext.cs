using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>EF Core context for the factory's own state (never customer code).</summary>
public sealed class DarkFactoryDbContext(DbContextOptions<DarkFactoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<StageCheckpoint> StageCheckpoints => Set<StageCheckpoint>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<Gate> Gates => Set<Gate>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    // docs/adr/0016, docs/adr/0017 (the spec graph and the conversation it grows from).
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Turn> Turns => Set<Turn>();
    public DbSet<SpecNode> SpecNodes => Set<SpecNode>();
    public DbSet<SpecRevision> SpecRevisions => Set<SpecRevision>();
    public DbSet<SpecEdge> SpecEdges => Set<SpecEdge>();
    public DbSet<SpecSnapshot> SpecSnapshots => Set<SpecSnapshot>();
    public DbSet<SnapshotMember> SnapshotMembers => Set<SnapshotMember>();
    public DbSet<SnapshotEdge> SnapshotEdges => Set<SnapshotEdge>();
    public DbSet<Amendment> Amendments => Set<Amendment>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Provenance> Provenance => Set<Provenance>();

    // docs/adr/0018 (servers) and docs/adr/0023 (standards index) — schema
    // only in this slice; df.servers.register and indexing are step 3b+.
    // docs/adr/0028 — the project's standing team. Minimal in this slice:
    // skills and team snapshotting land in 3d.
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<ModelCall> ModelCalls => Set<ModelCall>();

    public DbSet<Server> Servers => Set<Server>();
    public DbSet<ConformanceResult> ConformanceResults => Set<ConformanceResult>();
    public DbSet<StandardsIndexEntry> StandardsIndex => Set<StandardsIndexEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(64);
            e.Property(p => p.OrgId).HasMaxLength(64);
            e.Property(p => p.Name).HasMaxLength(128);
            e.Property(p => p.WorkspaceMcpUrl).HasMaxLength(512);
            // docs/adr/0013: names are unique per org so collision suffixing
            // (-2, -3, ...) has something concrete to check against.
            e.HasIndex(p => new { p.OrgId, p.Name }).IsUnique();
            e.HasIndex(p => new { p.OrgId, p.WorkspaceMcpUrl }).IsUnique();
        });

        modelBuilder.Entity<WorkItem>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.ProjectId);
            // No navigation properties by design (Core stays plain POCOs),
            // but EF still needs to know this dependency exists: without
            // it, SaveChanges has no reason to insert Project before
            // WorkItem and will happily violate the FK at the database
            // level the first time a caller Adds both in one call.
            e.HasOne<Project>().WithMany().HasForeignKey(w => w.ProjectId);
        });

        modelBuilder.Entity<Run>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.CurrentStage).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.LeasedBy).HasMaxLength(128);
            e.HasIndex(r => r.ProjectId);
            // Backs the claim query's WHERE (docs/adr/0008: lease, not just lock).
            e.HasIndex(r => new { r.Status, r.LeaseExpiresAt });
            e.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(r => r.WorkItemId);
            // docs/adr/0004 (amended): nullable until step 3c's df.work.create
            // actually seeds runs from a snapshot.
            e.HasOne<SpecSnapshot>().WithMany().HasForeignKey(r => r.SnapshotId).IsRequired(false);
        });

        modelBuilder.Entity<StageCheckpoint>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Stage).HasConversion<string>().HasMaxLength(32);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(c => c.RunId);
            // One row per (run, stage, attempt): re-processing the same
            // attempt after a resume must be an update, never a duplicate.
            e.HasIndex(c => new { c.RunId, c.Stage, c.Attempt }).IsUnique();
            e.HasOne<Run>().WithMany().HasForeignKey(c => c.RunId);
        });

        modelBuilder.Entity<Artifact>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Sha256).HasMaxLength(64);
            e.HasIndex(a => a.RunId);
            e.HasIndex(a => a.ConversationId);
            e.Property(a => a.ContentType).HasMaxLength(64);
            // Both optional, exactly one set: a run artifact (docs/adr/0004)
            // or a conversation's ContextPack. The database enforces the
            // "exactly one" with a CHECK constraint — see
            // Migrations/0004_teams_and_context_packs.sql.
            e.HasOne<Run>().WithMany().HasForeignKey(a => a.RunId).IsRequired(false);
            e.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId).IsRequired(false);
        });

        modelBuilder.Entity<Gate>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(g => g.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(g => g.RunId);
            e.HasOne<Run>().WithMany().HasForeignKey(g => g.RunId);
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Type).HasMaxLength(64);
            e.HasIndex(ev => new { ev.RunId, ev.CreatedAt });
            // The outbox publisher's poll query (docs/adr/0008): a partial
            // index keeps "unpublished events" cheap to find forever, not
            // just while the table is small.
            e.HasIndex(ev => ev.PublishedAt).HasFilter("published_at IS NULL");
            e.HasOne<Run>().WithMany().HasForeignKey(ev => ev.RunId);
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.FailureClass).HasConversion<string?>().HasMaxLength(32);
            e.HasIndex(a => new { a.ProjectId, a.RunId });
            e.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId);
            e.HasOne<Run>().WithMany().HasForeignKey(a => a.RunId);
        });

        ConfigureSpecGraph(modelBuilder);
    }

    private static void ConfigureSpecGraph(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(c => c.ProjectId);
            e.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId);
        });

        modelBuilder.Entity<Turn>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Role).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(t => new { t.ConversationId, t.Seq }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(t => t.ConversationId);
        });

        modelBuilder.Entity<Provenance>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.ActorType).HasConversion<string>().HasMaxLength(16);
            // Optional: a snapshot's or a directly-authored revision's
            // provenance may not trace to a conversation/turn at all.
            e.HasOne<Conversation>().WithMany().HasForeignKey(p => p.ConversationId).IsRequired(false);
            e.HasOne<Turn>().WithMany().HasForeignKey(p => p.TurnId).IsRequired(false);
        });

        modelBuilder.Entity<SpecNode>(e =>
        {
            e.HasKey(n => n.SpecId);
            e.Property(n => n.SpecId).HasMaxLength(26);
            e.HasIndex(n => new { n.ProjectId, n.Layer });
            e.HasOne<Project>().WithMany().HasForeignKey(n => n.ProjectId);
        });

        modelBuilder.Entity<SpecRevision>(e =>
        {
            // Content-addressed, keyed by (SpecId, Hash): identical content
            // for the same node is the same row (docs/adr/0016); the
            // database enforces immutability by revoking UPDATE/DELETE on
            // this table from the application role entirely — see
            // Migrations/0002_spec_graph.sql.
            e.HasKey(r => new { r.SpecId, r.Hash });
            e.Property(r => r.Hash).HasMaxLength(64);
            e.HasOne<SpecNode>().WithMany().HasForeignKey(r => r.SpecId);
            e.HasOne<Provenance>().WithMany().HasForeignKey(r => r.ProvenanceId);
        });

        modelBuilder.Entity<SpecEdge>(e =>
        {
            e.HasKey(edge => edge.Id);
            e.HasIndex(edge => new { edge.ProjectId, edge.FromSpecId });
            e.HasIndex(edge => new { edge.ProjectId, edge.ToSpecId });
            e.HasOne<Project>().WithMany().HasForeignKey(edge => edge.ProjectId);
            e.HasOne<SpecNode>().WithMany().HasForeignKey(edge => edge.FromSpecId);
            e.HasOne<SpecNode>().WithMany().HasForeignKey(edge => edge.ToSpecId);
            e.HasOne<Provenance>().WithMany().HasForeignKey(edge => edge.ProvenanceId);
        });

        modelBuilder.Entity<SpecSnapshot>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ProjectId, s.Name });
            e.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId);
            e.HasOne<Provenance>().WithMany().HasForeignKey(s => s.ProvenanceId);
        });

        modelBuilder.Entity<SnapshotMember>(e =>
        {
            e.HasKey(m => new { m.SnapshotId, m.SpecId });
            e.HasOne<SpecSnapshot>().WithMany().HasForeignKey(m => m.SnapshotId);
            // Deliberately no FK to SpecRevision: a member pins (SpecId,
            // RevisionHash) as a value pair, not a navigable relationship —
            // the pair is looked up directly when resolving a snapshot.
        });

        modelBuilder.Entity<SnapshotEdge>(e =>
        {
            e.HasKey(m => new { m.SnapshotId, m.EdgeId });
            e.HasOne<SpecSnapshot>().WithMany().HasForeignKey(m => m.SnapshotId);
            e.HasOne<SpecEdge>().WithMany().HasForeignKey(m => m.EdgeId);
        });

        modelBuilder.Entity<Amendment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(a => new { a.ProjectId, a.Status });
            e.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId);
            e.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId);
            e.HasOne<Turn>().WithMany().HasForeignKey(a => a.TurnId).IsRequired(false);
        });

        modelBuilder.Entity<Approval>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.TargetType).HasConversion<string>().HasMaxLength(16);
            e.Property(a => a.Decision).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(a => new { a.TargetType, a.TargetId });
            // No FK on (TargetType, TargetId): it's polymorphic (amendment
            // or gate), which a single-table foreign key can't express.
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.ProjectId);
            // One active team per project: docs/adr/0028 leaves multiple
            // teams open, but v1 must never be ambiguous about which one a
            // conversation resolves against, so the partial unique index
            // makes a second active team unrepresentable rather than merely
            // discouraged.
            e.HasIndex(t => t.ProjectId).HasFilter("is_active").IsUnique().HasDatabaseName("ux_teams_project_id_active");
            e.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId);
        });

        modelBuilder.Entity<TeamMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.TeamId, m.Role }).IsUnique();
            e.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamId);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasKey(a => a.Id);
            // One member per point per team: an assignment map with two
            // answers for "who plans?" is not a map.
            e.HasIndex(a => new { a.TeamId, a.Point }).IsUnique();
            e.HasOne<Team>().WithMany().HasForeignKey(a => a.TeamId);
            e.HasOne<TeamMember>().WithMany().HasForeignKey(a => a.TeamMemberId);
        });

        modelBuilder.Entity<ModelCall>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.RunId);
            // Backs the budget question: what has this member spent on this
            // run? (docs/adr/0032 — budgets read from here, not a ledger.)
            e.HasIndex(c => new { c.RunId, c.TeamMemberId });
            // Backs the roll-ups that feed the dashboard and billing.
            e.HasIndex(c => new { c.OrgId, c.CreatedAt });
            e.HasIndex(c => new { c.ProjectId, c.CreatedAt });
            e.HasOne<Run>().WithMany().HasForeignKey(c => c.RunId).IsRequired(false);
            e.HasOne<TeamMember>().WithMany().HasForeignKey(c => c.TeamMemberId).IsRequired(false);
        });

        modelBuilder.Entity<Server>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Tier).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(s => s.OrgId);
            // docs/conventions/describe.md: registration is idempotent by
            // (org_id, url). The unique index is what makes that true even
            // if two registrations race — the second one conflicts rather
            // than quietly producing a duplicate server.
            e.HasIndex(s => new { s.OrgId, s.Url }).IsUnique();
        });

        modelBuilder.Entity<ConformanceResult>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(c => new { c.ServerId, c.ConformanceRunId });
            e.HasOne<Server>().WithMany().HasForeignKey(c => c.ServerId);
        });

        modelBuilder.Entity<StandardsIndexEntry>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ServerId, s.Layer });
            e.HasOne<Server>().WithMany().HasForeignKey(s => s.ServerId);
        });
    }
}

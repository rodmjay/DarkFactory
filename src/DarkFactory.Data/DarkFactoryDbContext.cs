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
            e.HasOne<Run>().WithMany().HasForeignKey(a => a.RunId);
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
    }
}

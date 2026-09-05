using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// EF Core context for the factory's own state (never customer code).
/// Schema/entity configuration and the first migration are step 2 work
/// (data model + migrations + engine). This is deliberately thin for the
/// repo-skeleton slice: enough to prove the Npgsql provider wires up and to
/// back a real "SELECT 1"-style health check.
/// </summary>
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
}

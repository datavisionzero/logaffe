using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema.
/// </summary>
/// <remarks>
/// EF Core owns every table and the migrations that apply themselves on
/// startup — including the log entry table's, once its shape is settled — and it
/// serves everything except the log entries themselves, which are written with
/// Npgsql's binary <c>COPY</c> and read with hand-written SQL (ADR 0003).
/// </remarks>
public sealed class LogaffeDbContext(DbContextOptions<LogaffeDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<IngestToken> IngestTokens => Set<IngestToken>();

    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();

    /// <summary>
    /// The one account, and a set with no row in it while the installation is
    /// unclaimed. That there can be no second one is the table's own doing —
    /// see <c>OperatorConfiguration</c>.
    /// </summary>
    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<BackupCode> BackupCodes => Set<BackupCode>();

    /// <summary>
    /// The one row an installation holds about itself: when it last became
    /// claimable. It is written by the start that created the schema and by
    /// Host Recovery, and by nothing else (ADR 0034).
    /// </summary>
    public DbSet<ClaimWindow> ClaimWindows => Set<ClaimWindow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogaffeDbContext).Assembly);
}

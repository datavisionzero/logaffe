using Logaffe.Domain.Entries;
using Logaffe.Domain.Hosts;
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
/// startup — including the log entry table's — and it serves everything except
/// the log entries themselves, which are written with Npgsql's binary
/// <c>COPY</c> and read with hand-written SQL (ADR 0003). That is why
/// <see cref="LogEntry"/> is configured and has no set below: it is declared
/// here and served nowhere, and a <c>DbSet</c> over it would be an invitation
/// to the idiom that ADR keeps off this path.
/// </remarks>
public sealed class LogaffeDbContext(DbContextOptions<LogaffeDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>
    /// The headings the projects are listed under. A group carries a name and
    /// nothing else, and a set with no rows in it is an installation that has
    /// never needed one (ADR 0039).
    /// </summary>
    public DbSet<Group> Groups => Set<Group>();

    /// <summary>
    /// The machines the operator runs projects on. Unlike the log entry table,
    /// the sample tables below are both declared and served here: the log path
    /// goes around EF Core because eleven thousand entries a second earn it
    /// (ADR 0003), and a handful of hosts writing a few rows a minute earn
    /// nothing of the sort.
    /// </summary>
    public DbSet<Host> Hosts => Set<Host>();

    public DbSet<Sample> Samples => Set<Sample>();

    public DbSet<FilesystemReading> FilesystemReadings => Set<FilesystemReading>();

    public DbSet<IngestToken> IngestTokens => Set<IngestToken>();

    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();

    public DbSet<HostToken> HostTokens => Set<HostToken>();

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
    public DbSet<ClaimGuard> ClaimGuards => Set<ClaimGuard>();

    /// <summary>
    /// The one row of what the operator has set for the whole installation, and
    /// a set with no row in it until something has been set or read.
    /// </summary>
    public DbSet<InstallationSettings> InstallationSettings => Set<InstallationSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The two the trigram index over the rendered message needs, declared
        // where the migration can create them: pg_trgm for the operator class,
        // btree_gin so the same GIN index can lead with the project.
        modelBuilder.HasPostgresExtension("btree_gin");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogaffeDbContext).Assembly);
    }
}

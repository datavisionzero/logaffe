using Logaffe.Domain.Alerts;
using Logaffe.Domain.Entries;
using Logaffe.Domain.History;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Identities;
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

    /// <summary>
    /// What each project received in each hour, counted as the deliveries
    /// arrived rather than by asking the entry table afterwards (ADR 0047). It
    /// is declared and served here for the sample tables' reason: twenty
    /// projects writing a row an hour each is nowhere near what makes ADR 0003
    /// go around EF Core.
    /// </summary>
    public DbSet<Tally> Tallies => Set<Tally>();

    /// <summary>
    /// What the installation remembers about the conditions that have already
    /// fired: one row per subject per condition, and none at all on an
    /// installation nothing has ever been said about (ADR 0050).
    /// </summary>
    public DbSet<ConditionState> ConditionStates => Set<ConditionState>();

    public DbSet<IngestToken> IngestTokens => Set<IngestToken>();

    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();

    public DbSet<HostToken> HostTokens => Set<HostToken>();

    /// <summary>
    /// Everything that acts: the users and the agents, in one table split on a
    /// discriminator (ADR 0052). It is empty on an installation that has not
    /// been bootstrapped, which is the one question the bootstrap asks.
    /// </summary>
    public DbSet<Identity> Identities => Set<Identity>();

    /// <summary>
    /// Which user reaches which project — the one filter every read narrows to
    /// (ADR 0055).
    /// </summary>
    public DbSet<ProjectAccess> ProjectAccess => Set<ProjectAccess>();

    /// <summary>
    /// The links an installation has outstanding: invitations, password
    /// recoveries and changes of address (ADR 0053).
    /// </summary>
    public DbSet<OneTimeSecret> OneTimeSecrets => Set<OneTimeSecret>();

    /// <summary>
    /// What was changed on this installation and by whom — everything that
    /// changes configuration, and never an entry.
    /// </summary>
    public DbSet<Change> History => Set<Change>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<BackupCode> BackupCodes => Set<BackupCode>();

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

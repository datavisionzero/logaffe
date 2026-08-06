using Logaffe.Domain.Projects;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogaffeDbContext).Assembly);
}

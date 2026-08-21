using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class TallyConfiguration : IEntityTypeConfiguration<Tally>
{
    public void Configure(EntityTypeBuilder<Tally> builder)
    {
        builder.ToTable("project_tally");

        // Natural, like the sample table's and for the same reasons the entry
        // table's is not: nothing here is written with binary COPY and nothing
        // here is paged with a cursor, so a synthetic identity would have no
        // work to do.
        //
        // What it buys is that one project's hour is one row by the database's
        // doing rather than by the flush being careful — the flush adds to what
        // it finds, and a key that allowed two rows for one hour would make the
        // history depend on how often the installation restarted.
        builder.HasKey(t => new { t.ProjectId, t.Hour }).HasName("pk_project_tally");

        // No foreign key to the project, deliberately, and it is the entry
        // table's reason rather than the sample table's: a project is deleted at
        // once and what counted it follows in the background (ADR 0019). The
        // rows a deleted project leaves are unreachable on their way out —
        // nothing reads this table except by naming a project — and
        // SweepExpiredTallies is what takes them.
        builder.Property(t => t.ProjectId).HasColumnName("project_id");

        // The receipt clock, truncated to the hour by the domain before it ever
        // reaches here (`Tallying.HourOf`). The column is an ordinary
        // timestamptz: what makes it an hour is the rule, not the type.
        builder.Property(t => t.Hour).HasColumnName("hour");

        builder.Property(t => t.Entries).HasColumnName("entries");
        builder.Property(t => t.AtErrorOrAbove).HasColumnName("at_error_or_above");
    }
}

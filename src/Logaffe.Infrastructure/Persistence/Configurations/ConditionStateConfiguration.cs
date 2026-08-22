using Logaffe.Domain.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class ConditionStateConfiguration : IEntityTypeConfiguration<ConditionState>
{
    public void Configure(EntityTypeBuilder<ConditionState> builder)
    {
        builder.ToTable("alert_condition_state");

        // Natural, like the tally's: there is one row per subject per condition
        // and nothing pages or copies into this table, so a synthetic identity
        // would have no work to do. What the key buys is that a restarting
        // installation cannot end up with two opinions about when a condition
        // last fired.
        builder.HasKey(s => new { s.SubjectId, s.Condition })
            .HasName("pk_alert_condition_state");

        // The project for two of the conditions and the machine for the third,
        // and no foreign key to either — the tally's arrangement for the tally's
        // reason (ADR 0019). What a deleted project or host leaves behind is a
        // row nothing reads: the pass walks the projects that exist, and the
        // disk is read off the host the installation names.
        builder.Property(s => s.SubjectId).HasColumnName("subject_id");

        // The number rather than the name, and the numbers are written out in
        // the enum for exactly this: the column is what makes a condition's
        // history its own across a restart.
        builder.Property(s => s.Condition)
            .HasColumnName("condition")
            .HasConversion<short>()
            .IsRequired();

        builder.Property(s => s.Latched).HasColumnName("latched").IsRequired();

        builder.Property(s => s.NotifiedLevel).HasColumnName("notified_level").IsRequired();

        // Null on a row that exists because a condition cleared before it ever
        // fired, which is the ordinary shape of a busy installation nothing has
        // gone wrong on.
        builder.Property(s => s.NotifiedAt).HasColumnName("notified_at");
    }
}

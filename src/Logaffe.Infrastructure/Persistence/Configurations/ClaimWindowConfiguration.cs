using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class ClaimWindowConfiguration : IEntityTypeConfiguration<ClaimWindow>
{
    /// <inheritdoc cref="OperatorConfiguration"/>
    private const string OnlyWindow = "OnlyWindow";

    public void Configure(EntityTypeBuilder<ClaimWindow> builder)
    {
        builder.ToTable("claim_window");

        builder.HasKey(w => w.Id).HasName("pk_claim_window");
        builder.Property(w => w.Id).HasColumnName("id");

        // The installation's first run, or its last Host Recovery. It is the
        // whole of the row: the deadline is derived from it so that the two
        // cannot disagree (ADR 0034).
        builder.Property(w => w.OpenedAt).HasColumnName("opened_at").IsRequired();

        // The same column the account table carries, for the same reason: two
        // containers starting at once both try to write the first run, and the
        // database is what decides which of them did rather than a check either
        // could have run first.
        builder.Property<bool>(OnlyWindow)
            .HasColumnName("only_window")
            .HasDefaultValue(true);

        builder.HasIndex(OnlyWindow).IsUnique().HasDatabaseName("ix_claim_window_only_one");
    }
}

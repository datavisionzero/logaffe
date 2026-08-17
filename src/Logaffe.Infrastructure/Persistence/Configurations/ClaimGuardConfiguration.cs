using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class ClaimGuardConfiguration : IEntityTypeConfiguration<ClaimGuard>
{
    /// <inheritdoc cref="OperatorConfiguration"/>
    private const string OnlyGuard = "OnlyGuard";

    public void Configure(EntityTypeBuilder<ClaimGuard> builder)
    {
        builder.ToTable("claim_guard");

        builder.HasKey(g => g.Id).HasName("pk_claim_guard");
        builder.Property(g => g.Id).HasColumnName("id");

        // The installation's first run, or its last Host Recovery. The deadline
        // is derived from it rather than stored, so that the two cannot disagree
        // (ADR 0034).
        builder.Property(g => g.OpenedAt).HasColumnName("opened_at").IsRequired();

        // The hash of the secret this installation drew for itself, and null when
        // it drew none — window mode, or a secret that comes from configuration
        // and is therefore stored nowhere (ADR 0040). It is a hash and not the
        // secret: this row is verified against, never read back.
        builder.Property(g => g.DrawnSecretHash).HasColumnName("drawn_secret_hash");

        // The same column the account table carries, for the same reason: two
        // containers starting at once both try to write the first run, and the
        // database is what decides which of them did rather than a check either
        // could have run first.
        builder.Property<bool>(OnlyGuard)
            .HasColumnName("only_guard")
            .HasDefaultValue(true);

        builder.HasIndex(OnlyGuard).IsUnique().HasDatabaseName("ix_claim_guard_only_one");
    }
}

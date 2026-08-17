using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    /// <summary>
    /// The column that holds the table to one row. It carries no meaning and is
    /// not on the entity — it exists so that "there is exactly one operator" is
    /// something the database refuses to break rather than something every
    /// writer has to remember (ADR 0015).
    /// </summary>
    private const string OnlyOperator = "OnlyOperator";

    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("operator");

        builder.HasKey(o => o.Id).HasName("pk_operator");
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(Operator.PasswordHashMaxLength)
            .IsRequired();

        // The TOTP secret, sealed under the key on the host volume like a token
        // and for a different reason: a code cannot be computed without it
        // (ADR 0032). Both columns are nullable together, because the second
        // factor is the operator's to enrol and to remove (ADR 0041) and an
        // account that has none is an ordinary account.
        builder.Property(o => o.EncryptedSecondFactorSecret)
            .HasColumnName("second_factor_secret");

        builder.Property(o => o.SecondFactorEnrolledAt)
            .HasColumnName("second_factor_enrolled_at");

        builder.Property(o => o.ClaimedAt).HasColumnName("claimed_at").IsRequired();

        // Written by the default rather than by the writer, so that a second
        // INSERT collides here whatever it thinks it is doing. This is what
        // makes the claim atomic in the one way that matters: two claimants
        // racing both reach the last step, and the database decides
        // (ADR 0014).
        builder.Property<bool>(OnlyOperator)
            .HasColumnName("only_operator")
            .HasDefaultValue(true);

        builder.HasIndex(OnlyOperator).IsUnique().HasDatabaseName("ix_operator_only_one");
    }
}

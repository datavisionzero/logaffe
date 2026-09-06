using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
/// <remarks>
/// One table for both kinds, split on a discriminator column, because everything
/// that records <em>who</em> has to point at one place (ADR 0052). The columns a
/// user has and an agent does not are nullable, which is what one table costs
/// and is cheap at this shape: an installation holds a handful of these rows.
/// The check constraints below are what keeps that cheapness from turning into a
/// row that is neither one thing nor the other.
/// </remarks>
public sealed class IdentityConfiguration : IEntityTypeConfiguration<Identity>
{
    /// <summary>
    /// The discriminator, as the numbers <see cref="IdentityKind"/> is — the
    /// same choice <c>AgentTokenKind</c> made, so that every enum in this
    /// database reads the same way.
    /// </summary>
    private const string Kind = "kind";

    public void Configure(EntityTypeBuilder<Identity> builder)
    {
        builder.ToTable("identity", table =>
        {
            table.HasCheckConstraint("ck_identity_kind", """ "kind" in (0, 1) """);

            // An agent has an owner and is never an administrator; a user has no
            // owner and carries the columns an agent has no use for
            // (ADR 0052). Held here rather than only on the write path, because
            // this is the half a query written later cannot go around.
            table.HasCheckConstraint(
                "ck_identity_owner",
                """
                "kind" = 0 and "owner_id" is null
                or "kind" = 1 and "owner_id" is not null and not "administrator"
                """);

            table.HasCheckConstraint(
                "ck_identity_user",
                """
                "kind" = 0
                    and "email" is not null and "normalized_email" is not null
                    and "state" in (0, 1, 2)
                or "kind" = 1
                    and "email" is null and "normalized_email" is null
                    and "state" is null and "password_hash" is null
                    and "second_factor_secret" is null
                """);
        });

        builder.HasKey(i => i.Id).HasName("pk_identity");
        builder.Property(i => i.Id).HasColumnName("id");

        builder.HasDiscriminator<int>(Kind)
            .HasValue<User>((int)IdentityKind.User)
            .HasValue<Agent>((int)IdentityKind.Agent);

        builder.Property(i => i.Name)
            .HasColumnName("name")
            .HasMaxLength(Identity.NameMaxLength)
            .IsRequired();

        builder.Property(i => i.Administrator)
            .HasColumnName("administrator")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(User.EmailMaxLength);

        builder.Property(u => u.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(User.EmailMaxLength);

        // The last line rather than the first: the address is normalized before
        // the transaction, and this is what refuses two spellings of one address
        // when two requests arrive at once (ADR 0052).
        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ix_identity_normalized_email");

        builder.Property(u => u.State).HasColumnName("state").HasConversion<int>();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(User.PasswordHashMaxLength);

        // The TOTP secret, sealed under the key on the host volume like a token
        // and for a different reason: a code cannot be computed without it
        // (ADR 0057). Both columns are nullable together, because the second
        // factor is each user's to enrol and to remove (ADR 0041) and an account
        // that has none is an ordinary account.
        builder.Property(u => u.EncryptedSecondFactorSecret)
            .HasColumnName("second_factor_secret");

        builder.Property(u => u.SecondFactorEnrolledAt)
            .HasColumnName("second_factor_enrolled_at");
    }
}

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.Property(a => a.OwnerId).HasColumnName("owner_id");

        // An agent belongs to a user and goes when that user goes — which only
        // happens in Host Recovery, where everything goes anyway (ADR 0058).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.OwnerId)
            .HasConstraintName("fk_identity_owner")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.OwnerId).HasDatabaseName("ix_identity_owner");
    }
}

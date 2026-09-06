using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class OneTimeSecretConfiguration : IEntityTypeConfiguration<OneTimeSecret>
{
    public void Configure(EntityTypeBuilder<OneTimeSecret> builder)
    {
        builder.ToTable("one_time_secret");

        builder.HasKey(s => s.Id).HasName("pk_one_time_secret");
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();

        // Deactivating somebody leaves their outstanding links alone — the acts
        // refuse an inactive account — and Host Recovery takes them with the
        // identity, which is the only thing that removes one (ADR 0058).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("fk_one_time_secret_user")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Purpose)
            .HasColumnName("purpose")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(s => s.SecretHash).HasColumnName("secret_hash").IsRequired();

        // What a presented link is found by, and the only lookup this table has.
        // Unique because two secrets hashing the same would be one of them
        // opening the other's account.
        builder.HasIndex(s => s.SecretHash)
            .IsUnique()
            .HasDatabaseName("ix_one_time_secret_hash");

        builder.Property(s => s.PendingEmail)
            .HasColumnName("pending_email")
            .HasMaxLength(User.EmailMaxLength);

        builder.Property(s => s.PendingNormalizedEmail)
            .HasColumnName("pending_normalized_email")
            .HasMaxLength(User.EmailMaxLength);

        builder.Property(s => s.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(s => s.UsedAt).HasColumnName("used_at");

        // One live secret per user per purpose, held by the database rather than
        // by the act that writes one (ADR 0053). Partial, because a spent one is
        // history and history repeats.
        builder.HasIndex(s => new { s.UserId, s.Purpose })
            .IsUnique()
            .HasFilter("\"used_at\" is null")
            .HasDatabaseName("ix_one_time_secret_live");

        // What a reservation of an address is checked against, on the live rows
        // only. Not unique: two people may each have a spent change to one
        // address behind them, and only the live ones collide.
        builder.HasIndex(s => s.PendingNormalizedEmail)
            .HasFilter("\"used_at\" is null and \"pending_normalized_email\" is not null")
            .HasDatabaseName("ix_one_time_secret_pending");
    }
}

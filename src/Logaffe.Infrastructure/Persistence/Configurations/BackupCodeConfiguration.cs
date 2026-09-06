using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class BackupCodeConfiguration : IEntityTypeConfiguration<BackupCode>
{
    public void Configure(EntityTypeBuilder<BackupCode> builder)
    {
        builder.ToTable("backup_code");

        builder.HasKey(c => c.Id).HasName("pk_backup_code");
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();

        // As with a session: the account goes and its codes go with it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .HasConstraintName("fk_backup_code_user")
            .OnDelete(DeleteBehavior.Cascade);

        // The cascade's index, and the one every read here narrows by: a set of
        // codes belongs to one user and is only ever counted for that user.
        builder.HasIndex(c => c.UserId).HasDatabaseName("ix_backup_code_user");

        // A single fast SHA-256, no salt, and never recoverable (ADR 0057).
        builder.Property(c => c.Hash).HasColumnName("hash").IsRequired();

        // Two codes hashing the same would make one of them unspendable, which
        // is a set its holder would only discover was short at the worst
        // moment.
        builder.HasIndex(c => c.Hash).IsUnique().HasDatabaseName("ix_backup_code_hash");

        builder.Property(c => c.IssuedAt).HasColumnName("issued_at").IsRequired();

        // Null until the code is spent, and a timestamp rather than a deletion
        // afterwards: "how many remain" is a filtered count, and a used code
        // stays visibly used (ADR 0057).
        builder.Property(c => c.UsedAt).HasColumnName("used_at");
    }
}

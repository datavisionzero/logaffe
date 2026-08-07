using Logaffe.Domain.Operators;
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

        builder.Property(c => c.OperatorId).HasColumnName("operator_id").IsRequired();

        // As with a session: the account goes and its codes go with it.
        builder.HasOne<Operator>()
            .WithMany()
            .HasForeignKey(c => c.OperatorId)
            .HasConstraintName("fk_backup_code_operator")
            .OnDelete(DeleteBehavior.Cascade);

        // The cascade's index, named for this database's convention. Counting
        // what remains does not use it — with one account that count is the
        // table.
        builder.HasIndex(c => c.OperatorId).HasDatabaseName("ix_backup_code_operator");

        // A single fast SHA-256, no salt, and never recoverable (ADR 0032).
        builder.Property(c => c.Hash).HasColumnName("hash").IsRequired();

        // Two codes hashing the same would make one of them unspendable, which
        // is a set the operator would only discover was short at the worst
        // moment.
        builder.HasIndex(c => c.Hash).IsUnique().HasDatabaseName("ix_backup_code_hash");

        builder.Property(c => c.IssuedAt).HasColumnName("issued_at").IsRequired();

        // Null until the code is spent, and a timestamp rather than a deletion
        // afterwards: "how many remain" is a filtered count, and a used code
        // stays visibly used (ADR 0032).
        builder.Property(c => c.UsedAt).HasColumnName("used_at");
    }
}

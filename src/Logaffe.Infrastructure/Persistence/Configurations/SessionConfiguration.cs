using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("session");

        builder.HasKey(s => s.Id).HasName("pk_session");
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.OperatorId).HasColumnName("operator_id").IsRequired();

        // Host Recovery removes the account and the sessions go with it, which
        // is `docs/setup.md`'s "existing sessions end, since the account they
        // belong to no longer exists" — the database's doing rather than a step
        // the command has to remember.
        builder.HasOne<Operator>()
            .WithMany()
            .HasForeignKey(s => s.OperatorId)
            .HasConstraintName("fk_session_operator")
            .OnDelete(DeleteBehavior.Cascade);

        // Named here rather than left to the convention, so that every index in
        // this database reads the same way. It serves the cascade and nothing
        // else: with one account, every row in this table is the operator's, so
        // no query narrows by this column.
        builder.HasIndex(s => s.OperatorId).HasDatabaseName("ix_session_operator");

        builder.Property(s => s.SecretHash).HasColumnName("secret_hash").IsRequired();

        // Unique because two sessions answering to one secret is a fault rather
        // than a state — nothing looks a session up by this, it is compared in
        // constant time against the handful an account holds.
        builder.HasIndex(s => s.SecretHash).IsUnique().HasDatabaseName("ix_session_secret");

        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();

        // What the sliding deadline is measured from, and half of what the
        // operator is shown.
        builder.Property(s => s.LastUsedAt).HasColumnName("last_used_at").IsRequired();

        builder.Property(s => s.LastSeenFrom)
            .HasColumnName("last_seen_from")
            .HasMaxLength(Session.SeenFromMaxLength)
            .IsRequired();
    }
}

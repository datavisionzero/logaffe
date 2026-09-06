using Logaffe.Domain.Identities;
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

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();

        // Host Recovery removes every identity and the sessions go with them —
        // the database's doing rather than a step the command has to remember
        // (ADR 0058).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("fk_session_user")
            .OnDelete(DeleteBehavior.Cascade);

        // It serves the cascade and the list: a person is shown their own
        // sessions and never anybody else's, so this column is narrowed by on
        // every read but the one authentication makes.
        builder.HasIndex(s => s.UserId).HasDatabaseName("ix_session_user");

        builder.Property(s => s.SecretHash).HasColumnName("secret_hash").IsRequired();

        // Unique because two sessions answering to one secret is a fault rather
        // than a state — nothing looks a session up by this, it is compared in
        // constant time against the handful an installation holds.
        builder.HasIndex(s => s.SecretHash).IsUnique().HasDatabaseName("ix_session_secret");

        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();

        // What the idle deadline is measured from, and half of what a person is
        // shown. The absolute deadline is measured from `started_at`, which
        // nothing writes twice.
        builder.Property(s => s.LastUsedAt).HasColumnName("last_used_at").IsRequired();

        builder.Property(s => s.LastSeenFrom)
            .HasColumnName("last_seen_from")
            .HasMaxLength(Session.SeenFromMaxLength)
            .IsRequired();
    }
}

using Logaffe.Domain.History;
using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    public void Configure(EntityTypeBuilder<Change> builder)
    {
        builder.ToTable("change");

        // A bigint the database assigns, which is both the identity and the
        // order: the rows are read newest first and resumed on this, and there
        // is nothing to break a tie on because there are no ties.
        builder.HasKey(c => c.Id).HasName("pk_change");
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(c => c.ActorId).HasColumnName("actor_id").IsRequired();

        // The identity the row names. It cascades because Host Recovery removes
        // every identity (ADR 0058) — a history pointing at somebody who no
        // longer exists would be a list of rows nobody can read, and that
        // command is the one act that takes the whole installation's people at
        // once.
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(c => c.ActorId)
            .HasConstraintName("fk_change_actor")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.ActorKind)
            .HasColumnName("actor_kind")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(c => c.ActorName)
            .HasColumnName("actor_name")
            .HasMaxLength(Change.TextMaxLength)
            .IsRequired();

        builder.Property(c => c.At).HasColumnName("at").IsRequired();

        builder.Property(c => c.Subject)
            .HasColumnName("subject")
            .HasConversion<int>()
            .IsRequired();

        // No foreign key, deliberately. The row has to survive the thing it
        // points at — *who deleted project X* is the question this exists to
        // answer — so what it carries is the identity as it was and the name as
        // it read.
        builder.Property(c => c.SubjectId).HasColumnName("subject_id");

        builder.Property(c => c.SubjectName)
            .HasColumnName("subject_name")
            .HasMaxLength(Change.TextMaxLength)
            .IsRequired();

        builder.Property(c => c.Act).HasColumnName("act").HasConversion<int>().IsRequired();

        builder.Property(c => c.Field)
            .HasColumnName("field")
            .HasMaxLength(Change.TextMaxLength);

        builder.Property(c => c.From)
            .HasColumnName("moved_from").HasMaxLength(Change.TextMaxLength);
        builder.Property(c => c.To).HasColumnName("moved_to").HasMaxLength(Change.TextMaxLength);

        // Everything one thing had done to it, which is the second question
        // asked of this table after "what happened lately" — and the first is
        // the primary key walked backwards.
        builder.HasIndex(c => new { c.Subject, c.SubjectId })
            .HasDatabaseName("ix_change_subject");
    }
}

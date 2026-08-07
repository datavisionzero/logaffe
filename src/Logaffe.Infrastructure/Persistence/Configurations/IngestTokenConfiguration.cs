using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// Names are written out rather than derived from a convention, so that what is
/// in the database reads the same as the DDL in <c>docs/storage.md</c>.
/// </summary>
public sealed class IngestTokenConfiguration : IEntityTypeConfiguration<IngestToken>
{
    public void Configure(EntityTypeBuilder<IngestToken> builder)
    {
        builder.ToTable("ingest_token");

        builder.HasKey(t => t.Id).HasName("pk_ingest_token");
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.ProjectId).HasColumnName("project_id").IsRequired();

        // The project is deleted at once and everything hanging off it follows
        // (ADR 0019). Senders holding one of these get 401 from their next
        // delivery and carry on writing locally.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .HasConstraintName("fk_ingest_token_project")
            .OnDelete(DeleteBehavior.Cascade);

        // Named here rather than left to the convention, so that every index in
        // this database reads the same way. It serves the operator's view of a
        // project's tokens — one row, or two mid-rotation — and the cascade.
        builder.HasIndex(t => t.ProjectId).HasDatabaseName("ix_ingest_token_project");

        builder.Property(t => t.Identifier)
            .HasColumnName("identifier")
            .HasMaxLength(TokenIdentifier.Length)
            .HasConversion(
                identifier => identifier.Value,
                value => TokenIdentifier.Create(value))
            .IsRequired();

        // The index authentication runs on: one lookup per delivery, flat in the
        // number of tokens an installation holds (ADR 0031). Unique because two
        // rows answering to one identifier would make which of them was meant a
        // question the ingest path has no way to answer.
        builder.HasIndex(t => t.Identifier)
            .IsUnique()
            .HasDatabaseName("ix_ingest_token_identifier");

        builder.Property(t => t.EncryptedSecret).HasColumnName("secret").IsRequired();

        builder.Property(t => t.IssuedAt).HasColumnName("issued_at").IsRequired();

        // Null until the token has admitted a delivery, which is what makes a
        // token that was issued and never deployed tell itself apart from one
        // that has gone quiet.
        builder.Property(t => t.LastUsedAt).HasColumnName("last_used_at");
    }
}

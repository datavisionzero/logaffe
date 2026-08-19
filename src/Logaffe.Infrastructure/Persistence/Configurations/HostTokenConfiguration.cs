using Logaffe.Domain.Hosts;
using Logaffe.Domain.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class HostTokenConfiguration : IEntityTypeConfiguration<HostToken>
{
    public void Configure(EntityTypeBuilder<HostToken> builder)
    {
        builder.ToTable("host_token");

        builder.HasKey(t => t.Id).HasName("pk_host_token");
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.HostId).HasColumnName("host_id").IsRequired();

        // The host goes and its token goes with it. A collector still holding
        // one gets 401 from its next delivery and carries on doing nothing else.
        builder.HasOne<Host>()
            .WithMany()
            .HasForeignKey(t => t.HostId)
            .HasConstraintName("fk_host_token_host")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.HostId).HasDatabaseName("ix_host_token_host");

        builder.Property(t => t.Identifier)
            .HasColumnName("identifier")
            .HasMaxLength(TokenIdentifier.Length)
            .HasConversion(
                identifier => identifier.Value,
                value => TokenIdentifier.Create(value))
            .IsRequired();

        // The index authentication runs on, unique for the ingest token's
        // reason: two rows answering to one identifier would make which of them
        // was meant a question the sample path has no way to answer.
        builder.HasIndex(t => t.Identifier)
            .IsUnique()
            .HasDatabaseName("ix_host_token_identifier");

        builder.Property(t => t.EncryptedSecret).HasColumnName("secret").IsRequired();

        builder.Property(t => t.IssuedAt).HasColumnName("issued_at").IsRequired();

        builder.Property(t => t.LastUsedAt).HasColumnName("last_used_at");
    }
}

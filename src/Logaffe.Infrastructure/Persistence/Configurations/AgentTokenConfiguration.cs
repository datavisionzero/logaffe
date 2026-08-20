using Logaffe.Domain.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class AgentTokenConfiguration : IEntityTypeConfiguration<AgentToken>
{
    public void Configure(EntityTypeBuilder<AgentToken> builder)
    {
        builder.ToTable("agent_token");

        builder.HasKey(t => t.Id).HasName("pk_agent_token");
        builder.Property(t => t.Id).HasColumnName("id");

        // Deliberately not unique: the name is a label for the operator's list,
        // and two agents that call themselves the same thing is their business.
        builder.Property(t => t.Name)
            .HasColumnName("name")
            .HasMaxLength(AgentToken.NameMaxLength)
            .IsRequired();

        // On the row as well as in the prefix, and stored as the number the
        // enum is: the prefix is written by whoever presents the token, so it
        // says which half of the surface a call is aimed at and this says what
        // the token actually is (ADR 0046). Reading is zero, which is what every
        // agent token issued before this column existed became.
        builder.Property(t => t.Kind).HasColumnName("kind").IsRequired();

        builder.Property(t => t.MayDestroy).HasColumnName("may_destroy").IsRequired();

        builder.Property(t => t.Identifier)
            .HasColumnName("identifier")
            .HasMaxLength(TokenIdentifier.Length)
            .HasConversion(
                identifier => identifier.Value,
                value => TokenIdentifier.Create(value))
            .IsRequired();

        builder.HasIndex(t => t.Identifier)
            .IsUnique()
            .HasDatabaseName("ix_agent_token_identifier");

        builder.Property(t => t.EncryptedSecret).HasColumnName("secret").IsRequired();

        builder.Property(t => t.IssuedAt).HasColumnName("issued_at").IsRequired();

        builder.Property(t => t.LastUsedAt).HasColumnName("last_used_at");
    }
}

using Logaffe.Domain.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class HostConfiguration : IEntityTypeConfiguration<Host>
{
    public void Configure(EntityTypeBuilder<Host> builder)
    {
        // `host` is not reserved in Postgres, so it needs none of the treatment
        // `project_group` gets.
        builder.ToTable("host");

        builder.HasKey(h => h.Id).HasName("pk_host");
        builder.Property(h => h.Id).HasColumnName("id");

        builder.Property(h => h.Name)
            .HasColumnName("name")
            .HasMaxLength(Host.NameMaxLength)
            .IsRequired();

        // Unique across the installation rather than within something, because
        // a host sits in nothing: there is no group beside it to resolve two
        // machines called `web`, which is exactly the ambiguity the rule guards
        // against.
        builder.HasIndex(h => h.Name).IsUnique().HasDatabaseName("ix_host_name");

        builder.Property(h => h.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

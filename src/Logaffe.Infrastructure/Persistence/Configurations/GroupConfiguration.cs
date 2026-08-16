using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// Names are written out rather than derived from a convention, so that what is
/// in the database reads the same as the DDL in <c>docs/storage.md</c>.
/// </summary>
/// <remarks>
/// The table is <c>project_group</c> and not <c>group</c>, which is a reserved
/// word: EF quotes every identifier it writes and would not have minded, but an
/// operator reading the database by hand would have to quote it in every
/// statement they type.
/// </remarks>
public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("project_group");

        builder.HasKey(g => g.Id).HasName("pk_project_group");
        builder.Property(g => g.Id).HasColumnName("id");

        builder.Property(g => g.Name)
            .HasColumnName("name")
            .HasMaxLength(Group.NameMaxLength)
            .IsRequired();

        // Unique for the operator reading two headings that both say `shop`,
        // which is the same reason a project's name is unique within its group.
        builder.HasIndex(g => g.Name).IsUnique().HasDatabaseName("ix_project_group_name");

        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

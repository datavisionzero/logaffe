using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// Names are written out rather than derived from a convention, so that what is
/// in the database reads the same as the DDL in <c>docs/storage.md</c>.
/// </summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project");

        builder.HasKey(p => p.Id).HasName("pk_project");
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(Project.NameMaxLength)
            .IsRequired();

        builder.Property(p => p.GroupId).HasColumnName("group_id");

        // Unique for the operator reaching for one of two projects called `api`
        // at three in the morning, not for a technical reason — and unique
        // within the group, because a group named beside the project resolves
        // exactly the ambiguity the rule guards against (ADR 0039).
        //
        // `AreNullsDistinct(false)` is what keeps the projects in no group under
        // the rule at all: Postgres treats nulls as distinct by default, so
        // without it every ungrouped project would be unique by virtue of having
        // no group and two of them could both be called `api`.
        builder.HasIndex(p => new { p.GroupId, p.Name })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ix_project_group_id_name");

        // The group goes and its projects stay, in no group. Deleting a group
        // destroys nothing, and this is where that is actually true rather than
        // a sentence in an operation (ADR 0039). There is no navigation property
        // on either side: a project points at an identity, and the group is its
        // own aggregate.
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_project_project_group");

        builder.Property(p => p.Retention)
            .HasColumnName("retention_days")
            .HasConversion(
                retention => retention.Days,
                days => RetentionWindow.OfDays(days))
            .IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

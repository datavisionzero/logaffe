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

        // Unique for the operator reaching for one of two projects called `api`
        // at three in the morning, not for a technical reason.
        builder.HasIndex(p => p.Name).IsUnique().HasDatabaseName("ix_project_name");

        builder.Property(p => p.Retention)
            .HasColumnName("retention_days")
            .HasConversion(
                retention => retention.Days,
                days => RetentionWindow.OfDays(days))
            .IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

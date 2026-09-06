using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class ProjectAccessConfiguration : IEntityTypeConfiguration<ProjectAccess>
{
    public void Configure(EntityTypeBuilder<ProjectAccess> builder)
    {
        builder.ToTable("project_access");

        // The pair is the key: one user reaches one project once, and a second
        // grant of the same pair is the same state rather than a second row
        // (ADR 0055). A synthetic id would have bought a second way to say the
        // same thing and a unique index to keep it honest.
        builder.HasKey(a => new { a.ProjectId, a.UserId }).HasName("pk_project_access");

        builder.Property(a => a.ProjectId).HasColumnName("project_id");
        builder.Property(a => a.UserId).HasColumnName("user_id");

        // A deleted project takes its assignments with it, as it takes its
        // tokens (ADR 0019), and Host Recovery takes them with the identities
        // (ADR 0058). Neither is a step a command has to remember.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(a => a.ProjectId)
            .HasConstraintName("fk_project_access_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .HasConstraintName("fk_project_access_user")
            .OnDelete(DeleteBehavior.Cascade);

        // By user on every request, which is the read that has to be cheap; the
        // key's leading column already serves the other direction.
        builder.HasIndex(a => a.UserId).HasDatabaseName("ix_project_access_user");

        // Who handed it out and when. Nothing reads them today, and they are
        // here because "who gave this person access" is a question a multi-user
        // installation gets asked and the row is the only place the answer could
        // be.
        builder.Property(a => a.GrantedBy).HasColumnName("granted_by").IsRequired();
        builder.Property(a => a.GrantedAt).HasColumnName("granted_at").IsRequired();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class InstallationSettingsConfiguration
    : IEntityTypeConfiguration<InstallationSettings>
{
    /// <inheritdoc cref="ClaimGuardConfiguration"/>
    private const string OnlySettings = "OnlySettings";

    public void Configure(EntityTypeBuilder<InstallationSettings> builder)
    {
        builder.ToTable("installation_settings");

        builder.HasKey(s => s.Id).HasName("pk_installation_settings");
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.SampleRetentionDays)
            .HasColumnName("sample_retention_days")
            .IsRequired();

        // The machine the installation is on, and which of its filesystems holds
        // the database.
        builder.Property(s => s.HostId).HasColumnName("host_id");

        // The host goes and the installation is left on none — the project's
        // arrangement exactly, so that deleting a machine is never refused by
        // this row and never leaves it pointing at something gone. What the
        // set-null cannot do is take the mount with it, so the two are read as a
        // pair and a mount without a host means nothing.
        builder.HasOne<Domain.Hosts.Host>()
            .WithMany()
            .HasForeignKey(s => s.HostId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_installation_settings_host");

        // For the set-null above and for nothing else: the row is read whole and
        // there is exactly one of it, so nothing looks an installation up by the
        // machine it is on.
        builder.HasIndex(s => s.HostId).HasDatabaseName("ix_installation_settings_host");

        builder.Property(s => s.MountPath)
            .HasColumnName("mount_path")
            .HasMaxLength(Domain.Hosts.MountPath.MaxLength);

        builder.Property<bool>(OnlySettings)
            .HasColumnName("only_settings")
            .HasDefaultValue(true);

        builder.HasIndex(OnlySettings)
            .IsUnique()
            .HasDatabaseName("ix_installation_settings_only_one");
    }
}

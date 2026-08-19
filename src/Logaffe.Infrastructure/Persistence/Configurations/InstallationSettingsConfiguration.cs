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

        builder.Property<bool>(OnlySettings)
            .HasColumnName("only_settings")
            .HasDefaultValue(true);

        builder.HasIndex(OnlySettings)
            .IsUnique()
            .HasDatabaseName("ix_installation_settings_only_one");
    }
}

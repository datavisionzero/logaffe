using Logaffe.Domain.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class FilesystemReadingConfiguration : IEntityTypeConfiguration<FilesystemReading>
{
    public void Configure(EntityTypeBuilder<FilesystemReading> builder)
    {
        builder.ToTable("filesystem_reading");

        // The mount completes the key, which is what makes one delivery naming
        // one path twice a conflict rather than two rows.
        builder.HasKey(r => new { r.HostId, r.ReceiptTime, r.MountPath })
            .HasName("pk_filesystem_reading");

        builder.Property(r => r.HostId).HasColumnName("host_id");
        builder.Property(r => r.ReceiptTime).HasColumnName("receipt_time");

        builder.Property(r => r.MountPath)
            .HasColumnName("mount_path")
            .HasMaxLength(MountPath.MaxLength)
            .HasConversion(
                path => path.Value,
                value => MountPath.Create(value));

        // No cascade, for the reason the sample table has none: the sweep is
        // what takes these when a host is deleted.
        builder.HasOne<Host>()
            .WithMany()
            .HasForeignKey(r => r.HostId)
            .HasConstraintName("fk_filesystem_reading_host")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(r => r.Used).HasColumnName("used");
        builder.Property(r => r.Total).HasColumnName("total");
    }
}

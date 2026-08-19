using Logaffe.Domain.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IngestTokenConfiguration"/>
public sealed class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> builder)
    {
        builder.ToTable("host_sample");

        // Natural, unlike the entry table's. The two reasons that table hands
        // out a bigint are both absent here: binary COPY has to carry the value
        // with the row, and the cursor of docs/querying.md needs a unique
        // tie-break — and samples are written through EF and never paged.
        //
        // What the natural key buys is that the one-per-minute rule is enforced
        // by the database rather than trusted of the collector: a delivery that
        // arrives twice for one minute is a conflict, not a second row that
        // quietly doubles a machine on the band.
        builder.HasKey(s => new { s.HostId, s.ReceiptTime }).HasName("pk_host_sample");

        builder.Property(s => s.HostId).HasColumnName("host_id");
        builder.Property(s => s.ReceiptTime).HasColumnName("receipt_time");

        // The host goes and its samples follow in the background, exactly as a
        // deleted project's entries do (ADR 0019) — so no cascade here. The
        // sweep is what takes them, and until it does nothing can reach them:
        // every read of samples names a host, and that host is gone.
        builder.HasOne<Host>()
            .WithMany()
            .HasForeignKey(s => s.HostId)
            .HasConstraintName("fk_host_sample_host")
            .OnDelete(DeleteBehavior.NoAction);

        // `real` rather than `double precision`: a share of an interval and a
        // load average are reported to two decimal places by the machine itself,
        // and four bytes against eight over every row of the largest of these
        // tables is worth more than precision nobody has.
        builder.Property(s => s.Cpu).HasColumnName("cpu").HasColumnType("real");

        builder.Property(s => s.MemoryUsed).HasColumnName("memory_used");
        builder.Property(s => s.MemoryTotal).HasColumnName("memory_total");

        builder.Property(s => s.Load1).HasColumnName("load_1").HasColumnType("real");
        builder.Property(s => s.Load5).HasColumnName("load_5").HasColumnType("real");
        builder.Property(s => s.Load15).HasColumnName("load_15").HasColumnType("real");
    }
}

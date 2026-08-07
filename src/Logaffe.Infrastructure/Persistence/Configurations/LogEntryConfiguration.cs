using Logaffe.Domain.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The one table that dominates this product, declared here and served nowhere.
/// </summary>
/// <remarks>
/// <para>
/// EF Core owns the schema of every table including this one, so that there
/// stays exactly one place that creates schema and one mechanism that upgrades
/// it. It does not own the traffic: entries are written with Npgsql's binary
/// <c>COPY</c> and read with hand-written SQL, which is the boundary ADR 0003
/// draws at this table rather than at a feature.
/// </para>
/// <para>
/// Every index below is a claim <c>docs/storage.md</c> makes, with a measured
/// size against it, and the query it exists for. Changing one of them means
/// re-reading that document and the hand-written SQL both — ADR 0003 names that
/// as the standing cost of this design.
/// </para>
/// </remarks>
public sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.ToTable("log_entry");

        // The identity is the ingestion path's, seeded from the high-water mark
        // at startup, because binary COPY has to carry the value with the row.
        builder.HasKey(e => e.Id).HasName("pk_log_entry");
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        // No foreign key to the project, deliberately. A project is deleted at
        // once and its entries follow afterwards in the background (ADR 0019);
        // a cascade would put millions of rows back inside the operator's
        // request, which is the thing that ADR rejected. The window in which
        // entries exist for a project that does not is unreachable data on its
        // way out — every query runs inside a project, and this one is gone.
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();

        // Two clocks, read by different things (ADR 0007), so each has an index
        // of its own further down.
        builder.Property(e => e.EventTime).HasColumnName("event_time").IsRequired();
        builder.Property(e => e.ReceiptTime).HasColumnName("receipt_time").IsRequired();

        // The numeric values are what is stored, and they are ordered so that a
        // threshold is a comparison — which is what the partial index below is
        // defined over.
        builder.Property(e => e.Level)
            .HasColumnName("level")
            .HasConversion<short>()
            .IsRequired();

        // The four promoted properties. All nullable: promotion asks nothing of
        // a sender, and a delivery carrying none of them is complete.
        builder.Property(e => e.LoggerName).HasColumnName("logger_name");
        builder.Property(e => e.Instance).HasColumnName("instance");
        builder.Property(e => e.TraceId).HasColumnName("trace_id");
        builder.Property(e => e.SpanId).HasColumnName("span_id");

        builder.Property(e => e.MessageTemplate).HasColumnName("message_template").IsRequired();
        builder.Property(e => e.RenderedMessage).HasColumnName("rendered_message").IsRequired();
        builder.Property(e => e.Exception).HasColumnName("exception");

        // Stored as the object it arrived as. Nothing indexes it and no filter
        // reaches inside it (ADR 0010), so jsonb here buys validity and the
        // ability to hand it back as data rather than any query shape.
        builder.Property(e => e.Properties).HasColumnName("properties").HasColumnType("jsonb");

        builder.Property(e => e.MessageTruncated)
            .HasColumnName("message_truncated")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(e => e.ExceptionTruncated)
            .HasColumnName("exception_truncated")
            .HasDefaultValue(false)
            .IsRequired();

        // Named through the overload that takes one rather than through
        // HasDatabaseName, because two of these are over the same columns and
        // the property list alone would make the second modify the first.

        // The page: newest first by event time, identity breaking ties, which is
        // exactly the order and exactly the cursor docs/querying.md promises. It
        // is what makes paging independent of depth.
        builder.HasIndex(e => new { e.ProjectId, e.EventTime, e.Id }, "ix_log_entry_paging")
            .IsDescending(false, true, true);

        // The other clock, serving the two things that run on receipt time: the
        // live tail asking what has arrived since it last asked (ADR 0009), and
        // the retention sweep deleting by the same clock (ADR 0023).
        builder.HasIndex(e => new { e.ProjectId, e.ReceiptTime, e.Id }, "ix_log_entry_receipt");

        // The second-largest object in the database, and it earns it: filtering
        // by logger name is what cuts framework noise from application output.
        // It stays large because repeated text is stored rather than interned
        // (ADR 0027), so full names sit in the keys.
        builder.HasIndex(e => new { e.ProjectId, e.LoggerName, e.EventTime }, "ix_log_entry_logger_name")
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.ProjectId, e.Instance, e.EventTime }, "ix_log_entry_instance")
            .IsDescending(false, false, true);

        // What keeps "gather the entries of one request" from scanning the
        // project. Without it that filter is precisely the unbounded read
        // ADR 0026 cuts off at five seconds, which would turn a promised filter
        // into an error. Its cost has not been measured.
        builder.HasIndex(e => new { e.ProjectId, e.TraceId, e.EventTime }, "ix_log_entry_trace")
            .IsDescending(false, false, true);

        // "Warning and above" is the question people actually ask, and a partial
        // index over exactly that predicate answers it while indexing only the
        // entries that can match — two per cent of the heap. The other
        // thresholds are not indexed: Error and above uses this one and filters,
        // and Debug and above is nearly every entry and belongs on the paging
        // index.
        builder.HasIndex(e => new { e.ProjectId, e.EventTime, e.Id }, "ix_log_entry_warning_and_above")
            .IsDescending(false, true, true)
            .HasFilter($"level >= {(short)Level.Warning}");

        // Search is a substring match over the rendered form alone (ADR 0010),
        // and this is the largest object in the database. Leading with the
        // project is what btree_gin is here for: without it a search inside one
        // project would walk the trigrams of every project in the installation,
        // and the separation the product promises would hold everywhere except
        // in the index that does the most work.
        builder.HasIndex(e => new { e.ProjectId, e.RenderedMessage }, "ix_log_entry_search")
            .HasMethod("gin")
            .HasOperators("", "gin_trgm_ops");
    }
}

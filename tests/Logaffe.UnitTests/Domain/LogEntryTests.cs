using Logaffe.Domain.Entries;

namespace Logaffe.UnitTests.Domain;

/// <summary>
/// The entry holds what the table holds. The one rule it enforces is about the
/// shape rather than about the delivery: a trace and a span are promoted as the
/// byte lengths they actually are, or they are not promoted.
/// </summary>
public sealed class LogEntryTests
{
    private static readonly DateTimeOffset Happened = new(2026, 8, 7, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void An_entry_carrying_only_what_is_required_is_complete()
    {
        // Nothing is asked of a sender beyond a template, a level and a clock —
        // this is the `curl` line of docs/ingestion.md as a row.
        var entry = Plain();

        Assert.Null(entry.LoggerName);
        Assert.Null(entry.Instance);
        Assert.Null(entry.TraceId);
        Assert.Null(entry.SpanId);
        Assert.Null(entry.Exception);
        Assert.Null(entry.Properties);
        Assert.False(entry.MessageTruncated);
        Assert.False(entry.ExceptionTruncated);
    }

    [Fact]
    public void A_trace_is_the_sixteen_bytes_a_trace_is()
    {
        var trace = new byte[LogEntry.TraceIdLength];
        var span = new byte[LogEntry.SpanIdLength];

        var entry = new LogEntry
        {
            Id = 1,
            ProjectId = Guid.CreateVersion7(),
            EventTime = Happened,
            ReceiptTime = Happened,
            Level = Level.Information,
            MessageTemplate = "Handled {Path}",
            RenderedMessage = "Handled /orders",
            TraceId = trace,
            SpanId = span,
            MessageTruncated = false,
            ExceptionTruncated = false,
        };

        Assert.Equal(trace, entry.TraceId);
        Assert.Equal(span, entry.SpanId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(32)]
    public void A_trace_of_another_length_is_not_a_trace(int length) =>
        // CLEF delivers thirty-two hex characters and logaffe stores the bytes.
        // Something that is not a trace id stays an ordinary property on the way
        // in, so nothing has to accept it into a column promising a shape it
        // does not have.
        Assert.Throws<ArgumentException>(() => Plain(trace: new byte[length]));

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void A_span_of_another_length_is_not_a_span(int length) =>
        Assert.Throws<ArgumentException>(() => Plain(span: new byte[length]));

    [Fact]
    public void The_levels_are_ordered_so_that_a_threshold_is_a_comparison() =>
        // The partial index of docs/storage.md is defined over `level >= 3`, so
        // "Warning and above" is a property of these numbers rather than only of
        // their names.
        Assert.Equal(3, (short)Level.Warning);

    private static LogEntry Plain(byte[]? trace = null, byte[]? span = null) => new()
    {
        Id = 1,
        ProjectId = Guid.CreateVersion7(),
        EventTime = Happened,
        ReceiptTime = Happened,
        Level = Level.Information,
        MessageTemplate = "Disk full on /dev/sda1",
        RenderedMessage = "Disk full on /dev/sda1",
        TraceId = trace,
        SpanId = span,
        MessageTruncated = false,
        ExceptionTruncated = false,
    };
}

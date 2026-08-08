using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Domain;

public sealed class TailCursorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 10, 5, 0, TimeSpan.Zero);

    [Fact]
    public void A_cursor_reads_back_as_the_position_it_was_written_from()
    {
        var cursor = new TailCursor(Now, 4711);

        Assert.True(TailCursor.TryParse(cursor.ToString(), out var read));
        Assert.Equal(cursor, read);
    }

    [Fact]
    public void No_cursor_is_a_tail_that_has_not_started_rather_than_a_malformed_one()
    {
        Assert.True(TailCursor.TryParse(null, out var none));
        Assert.Null(none);

        Assert.True(TailCursor.TryParse(string.Empty, out var empty));
        Assert.Null(empty);
    }

    [Theory]
    [InlineData("not a cursor")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_cursor_that_is_not_one_is_refused(string value)
    {
        // Heavier here than on a page: a tail that quietly restarted from
        // nowhere would either show the project's oldest entries as new arrivals
        // or show nothing at all while an outage runs.
        Assert.False(TailCursor.TryParse(value, out _));
    }

    [Fact]
    public void It_survives_a_query_string_unescaped()
    {
        var cursor = new TailCursor(Now, long.MaxValue).ToString();

        Assert.Equal(cursor, Uri.EscapeDataString(cursor));
    }

    [Fact]
    public void The_beginning_is_before_every_arrival_there_could_be()
    {
        // What a tail on an empty project starts from, and it has to be a real
        // position rather than an absent one: the poll after it is the same call
        // with the cursor it was given.
        Assert.True(new TailCursor(DateTimeOffset.UnixEpoch, 1).IsAfter(TailCursor.Beginning));
        Assert.True(TailCursor.TryParse(TailCursor.Beginning.ToString(), out var read));
        Assert.Equal(TailCursor.Beginning, read);
    }

    [Fact]
    public void A_position_is_later_by_its_time_and_then_by_its_identity()
    {
        var arrival = new TailCursor(Now, 12);

        Assert.True(arrival.IsAfter(new TailCursor(Now.AddSeconds(-1), long.MaxValue)));
        Assert.False(arrival.IsAfter(new TailCursor(Now.AddSeconds(1), 0)));

        // The half of the pair that is not the timestamp: one batch is received
        // in one act, and its entries share a receipt time.
        Assert.True(arrival.IsAfter(new TailCursor(Now, 11)));
        Assert.False(arrival.IsAfter(new TailCursor(Now, 12)));
        Assert.False(arrival.IsAfter(new TailCursor(Now, 13)));
    }

    [Fact]
    public void The_offset_it_was_written_with_is_not_part_of_the_position()
    {
        var utc = new TailCursor(Now, 4711);
        var elsewhere = new TailCursor(Now.ToOffset(TimeSpan.FromHours(2)), 4711);

        Assert.Equal(utc.ToString(), elsewhere.ToString());
    }

    [Fact]
    public void The_cursor_after_an_entry_is_when_it_arrived_and_not_when_it_happened()
    {
        var entry = new LogEntry
        {
            Id = 12,
            ProjectId = Guid.CreateVersion7(),

            // A sender that was disconnected: it happened ten minutes ago and it
            // arrived now, and the tail follows the receipt (ADR 0009).
            EventTime = Now.AddMinutes(-10),
            ReceiptTime = Now,
            Level = Level.Information,
            MessageTemplate = "Disk full",
            RenderedMessage = "Disk full",
            MessageTruncated = false,
            ExceptionTruncated = false,
        };

        Assert.Equal(new TailCursor(Now, 12), TailCursor.After(entry));
    }

    [Fact]
    public void A_position_on_one_clock_is_not_a_position_on_the_other()
    {
        // The two cursors share the form they are written in, so the encoded
        // string of one reads as the other — which is why they are two types and
        // why neither read ever takes the one it is not asking for.
        var page = new EntryCursor(Now, 4711);

        Assert.True(TailCursor.TryParse(page.ToString(), out var read));
        Assert.Equal(new TailCursor(Now, 4711), read);
    }
}

using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Domain;

public sealed class EntryCursorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 10, 5, 0, TimeSpan.Zero);

    [Fact]
    public void A_cursor_reads_back_as_the_position_it_was_written_from()
    {
        var cursor = new EntryCursor(Now, 4711);

        Assert.True(EntryCursor.TryParse(cursor.ToString(), out var read));
        Assert.Equal(cursor, read);
    }

    [Fact]
    public void The_offset_it_was_written_with_is_not_part_of_the_position()
    {
        // The same instant, said two ways. A cursor that kept the offset would
        // be two different cursors to one row, and the page after them would be
        // the same page under two names.
        var utc = new EntryCursor(Now, 4711);
        var elsewhere = new EntryCursor(Now.ToOffset(TimeSpan.FromHours(2)), 4711);

        Assert.Equal(utc.ToString(), elsewhere.ToString());
    }

    [Fact]
    public void No_cursor_is_the_first_page_rather_than_a_malformed_one()
    {
        // The ordinary case: nothing has been paged yet, and that is not a
        // caller getting something wrong.
        Assert.True(EntryCursor.TryParse(null, out var none));
        Assert.Null(none);

        Assert.True(EntryCursor.TryParse(string.Empty, out var empty));
        Assert.Null(empty);
    }

    [Theory]
    [InlineData("not a cursor")]

    // Decodes, but to half a pair — the case that would page from whatever the
    // other half happened to be.
    [InlineData("AAAAAAAAAAA")]

    // Decodes to more than a pair.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_cursor_that_is_not_one_is_refused(string value)
    {
        // Refused rather than ignored: paging on from a position nobody chose is
        // the one failure a cursor exists to prevent.
        Assert.False(EntryCursor.TryParse(value, out _));
    }

    [Fact]
    public void It_survives_a_query_string_unescaped()
    {
        // base64url, so that it goes into an address and comes back out of one.
        var cursor = new EntryCursor(Now, long.MaxValue).ToString();

        Assert.Equal(cursor, Uri.EscapeDataString(cursor));
    }

    [Fact]
    public void The_cursor_after_an_entry_is_that_entrys_place_in_the_order()
    {
        var entry = new LogEntry
        {
            Id = 12,
            ProjectId = Guid.CreateVersion7(),
            EventTime = Now,
            ReceiptTime = Now,
            Level = Level.Information,
            MessageTemplate = "Disk full",
            RenderedMessage = "Disk full",
            MessageTruncated = false,
            ExceptionTruncated = false,
        };

        // Both halves, because two entries sharing an event time is ordinary and
        // the identity is what makes the order total.
        Assert.Equal(new EntryCursor(Now, 12), EntryCursor.After(entry));
    }
}

using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// A delivery, taken.
/// </summary>
/// <remarks>
/// What is asked here is the batch rather than the line: how many of them there
/// may be, how large the whole may be, what one broken line costs the others, and
/// what the rows come out carrying that no sender supplied — the identity, the
/// project and the receipt.
/// </remarks>
public sealed class IngestBatchTests
{
    private static readonly DateTimeOffset Arrived = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingEntries _entries = new();
    private readonly HandingOutIds _ids = new();
    private readonly RunningTally _tally = new();
    private readonly StoppedClock _clock = new(Arrived);

    [Fact]
    public async Task Every_line_of_a_good_batch_is_stored()
    {
        var receipt = await TakeAsync(Line("first"), Line("second"), Line("third"));

        Assert.Equal(BatchOutcome.Stored, receipt.Outcome);
        Assert.Equal(3, receipt.Accepted);
        Assert.Equal(0, receipt.Rejected);
        Assert.Empty(receipt.Reasons);
        Assert.Equal(
            ["first", "second", "third"],
            _entries.Entries.Select(entry => entry.RenderedMessage));
    }

    [Fact]
    public async Task An_empty_body_is_a_delivery_that_stored_nothing()
    {
        var receipt = await TakeAsync();

        Assert.Equal(BatchOutcome.Stored, receipt.Outcome);
        Assert.Equal(0, receipt.Accepted);

        // Nothing is written and no block is taken. A batch with nothing in it
        // is not a reason to touch the table or the counter.
        Assert.Empty(_entries.Written);
        Assert.Empty(_ids.Blocks);
    }

    [Fact]
    public async Task One_broken_line_never_costs_the_others()
    {
        // The whole of ADR 0006 in one assertion: the sender will not retry and
        // will not look, so refusing the batch would be a permanent, silent loss
        // of everything that was fine.
        var receipt = await TakeAsync(Line("first"), "{ not json", Line("third"));

        Assert.Equal(2, receipt.Accepted);
        Assert.Equal(1, receipt.Rejected);
        Assert.Equal(["first", "third"], _entries.Entries.Select(entry => entry.RenderedMessage));
    }

    [Fact]
    public async Task A_rejection_is_reported_against_the_line_it_was_on()
    {
        var receipt = await TakeAsync(Line("first"), "{ not json", Line("third"), """{"@mt":"no clock"}""");

        Assert.Equal(
            [(2, "the line is not JSON"), (4, "@t is missing")],
            receipt.Reasons.Select(rejection => (rejection.Line, rejection.Reason)));
    }

    [Fact]
    public async Task Blank_lines_are_counted_for_the_line_number_and_are_not_entries()
    {
        // A body that a sender wrote with a trailing newline, or with one line
        // per flush and a blank between: neither is a defect, and a person
        // counting lines in their own file has to arrive at the same number.
        var receipt = await TakeAsync(Line("first"), string.Empty, "   ", "{ not json");

        Assert.Equal(1, receipt.Accepted);
        Assert.Equal(1, receipt.Rejected);
        Assert.Equal(4, receipt.Reasons.Single().Line);
    }

    [Fact]
    public async Task Only_the_first_few_reasons_come_back_and_all_of_them_are_counted()
    {
        var broken = Enumerable.Repeat("{ not json", IngestBatch.ReasonsReported + 10).ToArray();

        var receipt = await TakeAsync(broken);

        Assert.Equal(IngestBatch.ReasonsReported + 10, receipt.Rejected);
        Assert.Equal(IngestBatch.ReasonsReported, receipt.Reasons.Count);
    }

    [Fact]
    public async Task A_batch_at_the_cap_is_taken()
    {
        var receipt = await TakeAsync(
            [.. Enumerable.Range(0, Caps.EntriesPerBatch).Select(index => Line($"entry {index}"))]);

        Assert.Equal(BatchOutcome.Stored, receipt.Outcome);
        Assert.Equal(Caps.EntriesPerBatch, receipt.Accepted);
    }

    [Fact]
    public async Task A_batch_over_the_entry_cap_is_refused_whole()
    {
        var receipt = await TakeAsync(
            [.. Enumerable.Range(0, Caps.EntriesPerBatch + 1).Select(index => Line($"entry {index}"))]);

        Assert.Equal(BatchOutcome.OverTheHardLimit, receipt.Outcome);

        // Nothing stored, which is what "the whole batch is refused" means: the
        // 413 of docs/ingestion.md is one of the three cases where no part of a
        // delivery can be afforded.
        Assert.Empty(_entries.Written);
    }

    [Fact]
    public async Task A_broken_line_still_counts_against_the_entry_cap()
    {
        // The cap is on what a delivery may carry, not on what it got right. A
        // sender could otherwise send a million lines as long as they were bad.
        var lines = Enumerable.Repeat("{ not json", Caps.EntriesPerBatch + 1).ToArray();

        Assert.Equal(BatchOutcome.OverTheHardLimit, (await TakeAsync(lines)).Outcome);
    }

    [Fact]
    public async Task A_batch_over_the_size_cap_is_refused_whole()
    {
        // One line, well under the entry cap, and over the size cap on its own.
        var receipt = await TakeAsync(Line(new string('x', Caps.BatchBytes + 1)));

        Assert.Equal(BatchOutcome.OverTheHardLimit, receipt.Outcome);
        Assert.Empty(_entries.Written);
    }

    [Fact]
    public async Task The_identities_are_a_block_taken_once_for_what_is_being_stored()
    {
        var ids = new HandingOutIds(from: 4_000);
        var ingest = new IngestBatch(_entries, ids, _tally, _clock);

        await ingest.ExecuteAsync(
            Guid.CreateVersion7(),
            Body(Line("first"), "{ not json", Line("third")),
            TestContext.Current.CancellationToken);

        // One block, sized to what is actually stored rather than to what
        // arrived, and consecutive from where the table had got to.
        Assert.Equal([2], ids.Blocks);
        Assert.Equal([4_001, 4_002], _entries.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Every_row_carries_the_project_its_token_named()
    {
        var projectId = Guid.CreateVersion7();
        var ingest = new IngestBatch(_entries, _ids, _tally, _clock);

        await ingest.ExecuteAsync(
            projectId, Body(Line("first"), Line("second")), TestContext.Current.CancellationToken);

        Assert.All(_entries.Entries, entry => Assert.Equal(projectId, entry.ProjectId));
    }

    [Fact]
    public async Task The_whole_batch_shares_one_receipt_and_keeps_its_own_event_times()
    {
        await TakeAsync(
            """{"@t":"2026-08-07T09:15:00Z","@mt":"first"}""",
            """{"@t":"2026-08-07T09:16:00Z","@mt":"second"}""");

        // Retention counts from the one clock a sender cannot get wrong
        // (ADR 0007), and it is the batch's arrival rather than each line's.
        Assert.All(_entries.Entries, entry => Assert.Equal(Arrived, entry.ReceiptTime));
        Assert.Equal(
            [new DateTimeOffset(2026, 8, 7, 9, 15, 0, TimeSpan.Zero),
             new DateTimeOffset(2026, 8, 7, 9, 16, 0, TimeSpan.Zero)],
            _entries.Entries.Select(entry => entry.EventTime));
    }

    [Fact]
    public async Task A_store_that_cannot_be_reached_throws_through()
    {
        // Deliberately not swallowed here: it is the 503 of docs/ingestion.md,
        // and which HTTP answer a failure becomes is the adapter's to decide —
        // as is writing it to logaffe's own file log.
        _entries.Refusing = new InvalidOperationException("no connection");

        await Assert.ThrowsAsync<InvalidOperationException>(() => TakeAsync(Line("first")));
    }

    [Fact]
    public async Task A_body_ending_without_a_newline_still_delivers_its_last_line()
    {
        var ingest = new IngestBatch(_entries, _ids, _tally, _clock);

        var receipt = await ingest.ExecuteAsync(
            Guid.CreateVersion7(),
            new MemoryStream(Encoding.UTF8.GetBytes(Line("only"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, receipt.Accepted);
    }

    [Fact]
    public async Task A_body_written_with_carriage_returns_is_the_same_delivery()
    {
        var ingest = new IngestBatch(_entries, _ids, _tally, _clock);

        var receipt = await ingest.ExecuteAsync(
            Guid.CreateVersion7(),
            new MemoryStream(Encoding.UTF8.GetBytes($"{Line("first")}\r\n{Line("second")}\r\n")),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, receipt.Accepted);
    }

    [Fact]
    public async Task What_was_stored_is_counted_into_the_hour_the_batch_arrived_in()
    {
        await TakeAsync(Line("first"), "{ not json", Line("third"));

        var increment = Assert.Single(_tally.Take());

        // What was stored rather than what arrived: the count is the table's
        // history, and a line that was never a row is not part of it.
        Assert.Equal(2, increment.Entries);
        Assert.Equal(Tallying.HourOf(Arrived), increment.Hour);
    }

    [Fact]
    public async Task The_hour_is_the_receipt_and_never_what_the_sender_said()
    {
        // A sender a year out, on the one clock retention already refuses to
        // count from (ADR 0007). The tally follows the same clock, because a
        // history keyed on a wrong clock is a history of a machine nobody has.
        await TakeAsync("""{"@t":"2025-01-01T03:00:00Z","@mt":"long ago"}""");

        Assert.Equal(Tallying.HourOf(Arrived), Assert.Single(_tally.Take()).Hour);
    }

    [Fact]
    public async Task Error_and_fatal_are_counted_apart_and_nothing_below_them_is()
    {
        await TakeAsync(
            Line("plain"),
            Levelled("Warning", "nearly"),
            Levelled("Error", "broken"),
            Levelled("Fatal", "over"));

        var increment = Assert.Single(_tally.Take());

        Assert.Equal(4, increment.Entries);
        Assert.Equal(2, increment.AtErrorOrAbove);
    }

    [Fact]
    public async Task A_store_that_cannot_be_reached_counts_nothing()
    {
        _entries.Refusing = new InvalidOperationException("no connection");

        await Assert.ThrowsAsync<InvalidOperationException>(() => TakeAsync(Line("first")));

        // The tally is a count of what the table took, so a delivery that was
        // never stored is not one of them.
        Assert.Empty(_tally.Take());
    }

    [Fact]
    public async Task A_batch_with_nothing_in_it_counts_nothing()
    {
        await TakeAsync();

        Assert.Empty(_tally.Take());
    }

    private Task<BatchReceipt> TakeAsync(params string[] lines) =>
        new IngestBatch(_entries, _ids, _tally, _clock).ExecuteAsync(
            Guid.CreateVersion7(), Body(lines), TestContext.Current.CancellationToken);

    private static Stream Body(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', lines) + '\n'));

    private static string Line(string message) =>
        $$"""{"@t":"2026-08-07T09:15:00Z","@mt":{{JsonString(message)}}}""";

    private static string Levelled(string level, string message) =>
        $$"""{"@t":"2026-08-07T09:15:00Z","@l":"{{level}}","@mt":{{JsonString(message)}}}""";

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);
}

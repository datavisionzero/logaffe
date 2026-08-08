using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Logaffe.Client;

namespace Logaffe.UnitTests.Client;

/// <summary>
/// The delivery an application hands its log entries to.
/// </summary>
/// <remarks>
/// What is asked here is the promise rather than the plumbing: that sending
/// never throws and never blocks, that a full queue sheds the oldest and says
/// so, that a batch stays inside what the installation takes, that shutdown
/// gets one last chance to deliver, and that everything which goes wrong ends up
/// in the application's own log — because logaffe is additive and that log is
/// where the record already is.
/// </remarks>
public sealed class EntryDeliveryTests
{
    private static readonly Uri Installation = new("https://logs.example.com");
    private const string Token = "logaffe_ingest_019fe179_s3cret";

    private readonly TakingDeliveries _installation = new();
    private readonly Reported _reported = new();

    [Fact]
    public async Task A_batch_arrives_as_newline_delimited_clef_under_its_token()
    {
        await using var delivery = Delivery();

        delivery.Send(Entry("first"));

        var delivered = await _installation.TakeAsync(1);

        Assert.Equal($"Bearer {Token}", delivered.Authorization);
        Assert.Equal("application/x-ndjson", delivered.ContentType);
        Assert.Equal([Entry("first")], delivered.Lines);
    }

    [Fact]
    public async Task The_trailing_newline_a_formatter_writes_is_not_sent_twice()
    {
        await using var delivery = Delivery();

        delivery.Send(Entry("first") + "\n");
        delivery.Send(Entry("second") + "\r\n");

        var delivered = await _installation.TakeAsync(1);

        Assert.Equal([Entry("first"), Entry("second")], delivered.Lines);
        Assert.DoesNotContain("\n\n", delivered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entries_that_arrive_together_travel_together()
    {
        await using var delivery = Delivery();

        for (var n = 0; n < 50; n++)
        {
            delivery.Send(Entry($"entry {n}"));
        }

        var delivered = await _installation.TakeAsync(1);

        Assert.Equal(50, delivered.Lines.Length);
    }

    [Fact]
    public async Task A_batch_stops_at_the_thousand_entries_the_installation_takes()
    {
        // Long enough that the window is not what closes this batch: the count
        // is, which is the limit being asked about.
        await using var delivery = Delivery(batchInterval: TimeSpan.FromSeconds(30));

        for (var n = 0; n < 1_500; n++)
        {
            delivery.Send(Entry($"entry {n}"));
        }

        var delivered = await _installation.TakeAsync(1);

        Assert.Equal(1_000, delivered.Lines.Length);
    }

    [Fact]
    public async Task A_batch_worth_compressing_is_gzipped()
    {
        // Long enough to gather two hundred sends, short enough that the window
        // is what closes the batch -- two hundred is well under the count that
        // would close it on its own.
        await using var delivery = Delivery(batchInterval: TimeSpan.FromMilliseconds(250));

        for (var n = 0; n < 200; n++)
        {
            delivery.Send(Entry($"a message long enough to be worth compressing, number {n}"));
        }

        var delivered = await _installation.TakeAsync(1);

        Assert.True(delivered.Gzipped);
        Assert.Equal(200, delivered.Lines.Length);
    }

    [Fact]
    public async Task One_small_batch_is_not_worth_compressing()
    {
        await using var delivery = Delivery();

        delivery.Send(Entry("first"));

        Assert.False((await _installation.TakeAsync(1)).Gzipped);
    }

    [Fact]
    public async Task A_full_queue_sheds_the_oldest_and_says_how_many()
    {
        _installation.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var delivery = Delivery(queueCapacity: 4);

        // Holds the pump inside a delivery, so that everything sent from here
        // stays in the queue rather than being drained out of it.
        delivery.Send(Entry("in flight"));
        await _installation.TakeAsync(1);

        for (var n = 1; n <= 9; n++)
        {
            delivery.Send(Entry($"entry {n}"));
        }

        _installation.Gate.SetResult();

        var shed = await _reported.UntilAsync(what => what.Contains("were dropped"));
        Assert.Contains("5 entries", shed, StringComparison.Ordinal);

        // The four that survive are the four that arrived last.
        var delivered = await _installation.TakeAsync(2);
        Assert.Equal(
            [Entry("entry 6"), Entry("entry 7"), Entry("entry 8"), Entry("entry 9")],
            delivered.Lines);
    }

    [Fact]
    public async Task Sending_never_blocks_on_a_delivery_in_flight()
    {
        _installation.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var delivery = Delivery(queueCapacity: 16);

        delivery.Send(Entry("in flight"));
        await _installation.TakeAsync(1);

        // The queue is sixteen deep and the only reader is stuck, so all but the
        // last sixteen of these are dropped on the way in. None of them may wait
        // for that to be decided.
        var clock = Stopwatch.StartNew();

        for (var n = 0; n < 20_000; n++)
        {
            delivery.Send(Entry($"entry {n}"));
        }

        clock.Stop();
        _installation.Gate.SetResult();

        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(2),
            $"20 000 sends against a stalled delivery took {clock.Elapsed}.");
    }

    [Fact]
    public async Task An_installation_that_is_down_costs_the_batch_and_nothing_else()
    {
        _installation.Fails = new HttpRequestException("no route to host");

        await using var delivery = Delivery();

        delivery.Send(Entry("first"));

        var reported = await _reported.UntilAsync(what => what.Contains("were not delivered"));
        Assert.Contains("1 entries", reported, StringComparison.Ordinal);

        // Nothing is retried: the batch is gone, and the application still has
        // its own file (ADR 0006). The next delivery carries what was sent
        // after the failure and no trace of what was lost to it.
        _installation.Fails = null;
        delivery.Send(Entry("second"));

        Assert.Equal([Entry("second")], (await _installation.TakeAsync(2)).Lines);
    }

    [Fact]
    public async Task A_refused_token_says_so_in_the_application_log()
    {
        _installation.Status = HttpStatusCode.Unauthorized;

        await using var delivery = Delivery();

        delivery.Send(Entry("first"));

        var reported = await _reported.UntilAsync(what => what.Contains("token was refused"));
        Assert.Contains("revoked", reported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entries_the_installation_could_not_read_are_reported()
    {
        _installation.Receipt = """{"accepted":2,"rejected":3,"reasons":[]}""";

        await using var delivery = Delivery();

        delivery.Send(Entry("first"));

        var reported = await _reported.UntilAsync(what => what.Contains("were refused as unreadable"));
        Assert.Contains("3 of 1 entries", reported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shutting_down_delivers_what_is_still_queued()
    {
        // Nothing here would have been sent by the window; only the flush sends
        // it.
        var delivery = Delivery(batchInterval: TimeSpan.FromSeconds(30));

        delivery.Send(Entry("last words"));

        await delivery.DisposeAsync();

        Assert.Equal([Entry("last words")], _installation.Taken.Single().Lines);
    }

    [Fact]
    public async Task Sending_into_a_delivery_that_is_gone_throws_nothing()
    {
        var delivery = Delivery();

        await delivery.DisposeAsync();

        delivery.Send(Entry("nobody is listening"));
        delivery.Dispose();
    }

    [Fact]
    public async Task Nothing_the_application_hands_over_is_worth_throwing_about()
    {
        await using var delivery = Delivery();

        delivery.Send(null!);
        delivery.Send(string.Empty);
        delivery.Send("   ");
        delivery.Send("\n");
        delivery.Send(Entry("and one that counts"));

        Assert.Equal([Entry("and one that counts")], (await _installation.TakeAsync(1)).Lines);
    }

    [Fact]
    public async Task A_reporting_callback_that_throws_is_not_the_applications_problem()
    {
        _installation.Fails = new HttpRequestException("no route to host");

        await using var delivery = new EntryDelivery(
            new EntryDeliveryOptions
            {
                Installation = Installation,
                IngestToken = Token,
                BatchInterval = TimeSpan.FromMilliseconds(20),
                OnFailure = (_, _) => throw new InvalidOperationException("the log is on fire"),
            },
            new HttpClient(_installation));

        delivery.Send(Entry("first"));

        await _installation.TakeAsync(1);
    }

    private EntryDelivery Delivery(
        TimeSpan? batchInterval = null, int queueCapacity = 10_000) =>
        new(
            new EntryDeliveryOptions
            {
                Installation = Installation,
                IngestToken = Token,
                QueueCapacity = queueCapacity,
                BatchInterval = batchInterval ?? TimeSpan.FromMilliseconds(20),
                FlushTimeout = TimeSpan.FromSeconds(10),
                OnFailure = _reported.Add,
            },
            new HttpClient(_installation));

    private static string Entry(string message) =>
        $$"""{"@t":"2026-08-08T12:00:00.000Z","@mt":"{{message}}"}""";

    /// <summary>
    /// The application's own local log, which is where this client says
    /// everything it has to say.
    /// </summary>
    private sealed class Reported
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();

        public void Add(string what, Exception? failure) => _messages.Writer.TryWrite(what);

        public async Task<string> UntilAsync(Func<string, bool> matches)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await foreach (var message in _messages.Reader.ReadAllAsync(timeout.Token))
            {
                if (matches(message))
                {
                    return message;
                }
            }

            throw new InvalidOperationException("Nothing more was reported.");
        }
    }
}

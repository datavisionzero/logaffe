using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Logaffe.Client;

/// <summary>
/// Takes log entries from an application and delivers them, without ever making
/// that the application's problem.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the fire-and-forget promise <c>docs/ingestion.md</c>
/// makes, and it is one class because the Serilog sink and the
/// <c>ILoggerProvider</c> have to behave identically under stress
/// (<c>docs/codebase.md</c>). Both of them format CLEF and hand it here.
/// </para>
/// <para>
/// <b>What it promises.</b> <see cref="Send"/> never throws into the calling
/// application and never blocks it: it puts a line into a bounded queue and
/// returns. When that queue is full the <em>oldest</em> entries go, because a
/// logging component that slows an application down or fails it is worse than
/// one that loses the beginning of an outage. On shutdown what is queued gets
/// <see cref="EntryDeliveryOptions.FlushTimeout"/> to leave, and what does not
/// make it is lost — logaffe is additive, so the application still has its own
/// file, which is also where everything that goes wrong here is reported.
/// </para>
/// <para>
/// <b>Nothing is retried.</b> Delivery is fire-and-forget, there is no
/// acknowledgement a sender waits on and no receipt to store, so a refused or
/// failed batch is gone and is written to the application's log rather than
/// queued again
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0006-a-batch-is-accepted-in-part.md">ADR 0006</see>).
/// Retrying would turn an installation that is down into an application holding
/// an ever-growing queue of the past, which is the failure this design refuses.
/// </para>
/// <para>
/// <b>It says one thing before anything has been logged.</b> Building this sends
/// an empty batch under the token and reports a refusal of it, which is the one
/// request here that is not entries going out
/// (<see cref="ProbeAsync"/>). Like everything else on this path it happens in
/// the background, holds nothing up and never throws.
/// </para>
/// </remarks>
public sealed class EntryDelivery : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Where a delivery arrives. It is the installation's promise to everything
    /// already sending rather than a route that moves, so it is not a setting.
    /// </summary>
    private const string IngestPath = "/ingest";

    /// <summary>Newline-delimited JSON, one CLEF object per entry (ADR 0004).</summary>
    private const string ContentType = "application/x-ndjson";

    private const string Gzip = "gzip";

    /// <summary>
    /// The batch limits, which are product values: documented in
    /// <c>docs/ingestion.md</c> and the same in every installation rather than
    /// something a sender tunes. They are repeated here rather than shared
    /// because a package cannot reach into the server it talks to, and a client
    /// that batched above them would have every batch refused whole with
    /// <c>413</c>.
    /// </summary>
    private const int EntriesPerBatch = 1_000;

    /// <inheritdoc cref="EntriesPerBatch"/>
    private const int BatchBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Below this a body is sent as it is. Compressing a single small entry
    /// costs more bytes than it saves, and the cap the installation counts is on
    /// the decompressed body either way, so gzip buys nothing but bandwidth.
    /// </summary>
    private const int GzipThreshold = 4 * 1024;

    /// <summary>
    /// How long a delivery that keeps failing the same way stays quiet before it
    /// says so again.
    /// </summary>
    /// <remarks>
    /// An installation that is unreachable is unreachable for minutes at a time,
    /// and at the default batch interval the same failure is otherwise a line
    /// every second — after a night of it, the sender's own log holds nothing
    /// but this component complaining. Five minutes is a judgement rather than a
    /// measured figure: few enough lines that an outage does not bury the log,
    /// often enough that somebody reading during one can see it is still going
    /// on.
    /// </remarks>
    private static readonly TimeSpan RepeatFailureAfter = TimeSpan.FromMinutes(5);

    private readonly EntryDeliveryOptions _options;
    private readonly Channel<string> _queued;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _ingest;
    private readonly TimeProvider _time;
    private readonly Task _pump;

    private int _dropped;
    private int _disposed;

    // The outage under way, if one is: the cause that is repeating, when this
    // last said anything about it, and what it has cost since then and
    // altogether. Only the pump touches them — it is the single reader, and one
    // delivery's outcome is settled before the next one starts — so they need no
    // guard of their own.
    private string? _failing;
    private DateTimeOffset _said;
    private int _lostSinceSaid;
    private int _lostToTheOutage;

    /// <summary>
    /// Starts delivering, with an <see cref="HttpClient"/> of its own.
    /// </summary>
    public EntryDelivery(EntryDeliveryOptions options)
        : this(options, new HttpClient(), ownsHttp: true, TimeProvider.System)
    {
    }

    /// <summary>
    /// Starts delivering over a caller's <see cref="HttpClient"/>, which is not
    /// disposed with this and is how an application that manages its own
    /// handlers — or a test that substitutes one — supplies it.
    /// </summary>
    public EntryDelivery(EntryDeliveryOptions options, HttpClient http)
        : this(options, http, ownsHttp: false, TimeProvider.System)
    {
    }

    /// <summary>
    /// The same again with the clock supplied, which is how a test asks about
    /// the report that only comes after five minutes.
    /// </summary>
    internal EntryDelivery(EntryDeliveryOptions options, HttpClient http, TimeProvider time)
        : this(options, http, ownsHttp: false, time)
    {
    }

    private EntryDelivery(
        EntryDeliveryOptions options, HttpClient http, bool ownsHttp, TimeProvider time)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsHttp = ownsHttp;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _ingest = new Uri(options.Installation, IngestPath);

        // DropOldest is what makes Send neither block nor fail: the queue always
        // has room, and what it costs is the front of it. The callback is the
        // only way to know a drop happened at all, and an operator who is losing
        // entries has to be told.
        _queued = Channel.CreateBounded<string>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            itemDropped: _ => Interlocked.Increment(ref _dropped));

        _pump = Task.Run(PumpAsync);

        // Not awaited and not held: it reports or it says nothing, it swallows
        // everything, and a delivery disposed before it answers is one of the
        // things it says nothing about.
        _ = Task.Run(ProbeAsync);
    }

    /// <summary>
    /// Hands over one CLEF line. Returns immediately, and throws nothing.
    /// </summary>
    /// <remarks>
    /// A trailing newline is trimmed, because the formatters above this write
    /// one and the batch supplies its own separators.
    /// </remarks>
    public void Send(string clefLine)
    {
        if (string.IsNullOrWhiteSpace(clefLine))
        {
            return;
        }

        var line = clefLine.TrimEnd('\r', '\n');

        if (line.Length == 0)
        {
            return;
        }

        // False when the queue has been completed by disposal. Sending into a
        // disposed delivery is a race an application should not have to avoid,
        // so it is ignored rather than thrown.
        _queued.Writer.TryWrite(line);
    }

    /// <summary>
    /// Stops taking entries and gives what is queued
    /// <see cref="EntryDeliveryOptions.FlushTimeout"/> to leave.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _queued.Writer.TryComplete();

        // Blocking is the point: this is shutdown, and the alternative is an
        // application exiting with its last entries still in memory. The pump
        // awaits with ConfigureAwait(false) throughout, so there is no context
        // for this to deadlock against.
        try
        {
            _pump.Wait(_options.FlushTimeout);
        }
        catch (AggregateException)
        {
            // The pump reports its own failures; there is nobody above this to
            // tell, and throwing out of Dispose would fail an application that
            // is already on its way down.
        }

        Finish();
    }

    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _queued.Writer.TryComplete();

        try
        {
            await _pump.WaitAsync(_options.FlushTimeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Timed out, or the pump ended badly. Either way the entries that
            // were still queued are lost, which is what the timeout is for.
        }

        Finish();
    }

    private void Finish()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }

        ReportDrops();
        ReportAbandoned();
    }

    /// <summary>
    /// Fills a batch and sends it, for as long as entries keep arriving.
    /// </summary>
    /// <remarks>
    /// A batch closes on whichever comes first: the thousand entries the
    /// installation takes, the five mebibytes it takes, or
    /// <see cref="EntryDeliveryOptions.BatchInterval"/> after its first entry
    /// arrived. The last of those is what keeps an application's steady trickle
    /// from becoming one request per entry without making an operator wait to
    /// see it.
    /// </remarks>
    private async Task PumpAsync()
    {
        var reader = _queued.Reader;
        var batch = new List<string>(EntriesPerBatch);

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            batch.Clear();
            var bytes = 0;

            using var window = new CancellationTokenSource(_options.BatchInterval);

            while (batch.Count < EntriesPerBatch)
            {
                if (!reader.TryRead(out var line))
                {
                    if (!await WaitForMoreAsync(reader, window.Token).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                // One byte for the newline this line is joined with.
                var size = Encoding.UTF8.GetByteCount(line) + 1;

                if (size > BatchBytes)
                {
                    // No batching makes this deliverable, and holding it would
                    // block every entry behind it.
                    Report(
                        $"One entry of {size} bytes is past the {BatchBytes} byte limit a "
                        + "delivery may carry and was dropped.",
                        null);

                    continue;
                }

                if (bytes + size > BatchBytes)
                {
                    await DeliverAsync(batch).ConfigureAwait(false);
                    batch.Clear();
                    bytes = 0;
                }

                batch.Add(line);
                bytes += size;
            }

            if (batch.Count > 0)
            {
                await DeliverAsync(batch).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Whether anything more is coming before this batch's window closes.
    /// </summary>
    private static async Task<bool> WaitForMoreAsync(
        ChannelReader<string> reader, CancellationToken window)
    {
        try
        {
            // False once the writer is completed and the queue is empty, which
            // is disposal asking the pump to end.
            return await reader.WaitToReadAsync(window).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The window closed. What is in hand goes now.
            return false;
        }
    }

    /// <summary>
    /// One delivery, and the report of whatever it turned out to be.
    /// </summary>
    private async Task DeliverAsync(List<string> batch)
    {
        try
        {
            using var timeout = new CancellationTokenSource(_options.DeliveryTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, _ingest)
            {
                Content = Body(batch),
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.IngestToken);

            using var response = await _http
                .SendAsync(request, timeout.Token)
                .ConfigureAwait(false);

            // Before the receipt is read: the installation answered, which is
            // the end of an outage whatever the answer turns out to say.
            ReportRecovered();

            await ReadReceiptAsync(response, batch.Count, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            ReportFailed(batch.Count, failure);
        }
        finally
        {
            ReportDrops();
        }
    }

    /// <summary>
    /// Asks the installation once, as a sender configures this, whether it will
    /// take this token at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing else tells a sender that the address and the token are right. The
    /// first sign of a typo in an application's environment is a delivery that
    /// failed, which is a line somebody has to have been looking at, and the
    /// operator's own sign is a project that stays empty — which is the adoption
    /// path <c>VISION.md</c> measures everything on.
    /// </para>
    /// <para>
    /// <b>It only ever reports.</b> It does not hold the constructor, it never
    /// throws, and it delays no entry: logaffe observes an application and may
    /// not be the thing that keeps it from starting, which is as true of a token
    /// that expired overnight as of an installation coming up a second behind
    /// its sender in a shared restart. There is no setting for it either — a
    /// probe that cannot fail the application has nothing to switch off.
    /// </para>
    /// <para>
    /// <b>An empty <c>POST</c>, not a <c>HEAD</c>.</b> The endpoint
    /// authenticates before it touches a body, so an empty batch under a good
    /// token is a receipt over no entries that stores nothing, and under a bad
    /// one it is the refusal this is asking about. The ingest contract is not
    /// widened for a diagnostic.
    /// </para>
    /// <para>
    /// <b>Only <c>401</c> speaks.</b> A receipt says nothing, and so does
    /// everything in between — no route, a timeout, a <c>5xx</c>, a <c>429</c> —
    /// because those are passing states the ordinary delivery path reports on
    /// its own, and a probe complaining about an installation that is still
    /// starting would be noise in the one moment everything restarts together.
    /// </para>
    /// </remarks>
    private async Task ProbeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(_options.DeliveryTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, _ingest)
            {
                Content = Content([], compressed: false),
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.IngestToken);

            using var response = await _http
                .SendAsync(request, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                Report(
                    $"{_ingest} refused the ingest token, so nothing this application logs "
                    + "will be delivered. Check that it is the token of a project that still "
                    + "exists and has not been revoked.",
                    null);
            }
        }
        catch (Exception)
        {
            // On purpose, and for the reason the remarks give. What lands here
            // is the states this says nothing about, plus the disposal of a
            // client that this was still holding.
        }
    }

    /// <summary>
    /// The batch as the endpoint reads it: one CLEF object per line, gzipped
    /// once it is worth gzipping.
    /// </summary>
    private static HttpContent Body(List<string> batch)
    {
        var lines = new StringBuilder();

        foreach (var line in batch)
        {
            lines.Append(line).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(lines.ToString());

        if (bytes.Length < GzipThreshold)
        {
            return Content(bytes, compressed: false);
        }

        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return Content(buffer.ToArray(), compressed: true);
    }

    private static ByteArrayContent Content(byte[] body, bool compressed)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);

        if (compressed)
        {
            content.Headers.ContentEncoding.Add(Gzip);
        }

        return content;
    }

    /// <summary>
    /// What the installation said, for the application's own log.
    /// </summary>
    /// <remarks>
    /// The receipt is diagnostic and nothing here depends on it (ADR 0006), but
    /// entries the installation could not read are worth saying out loud: they
    /// are a defect in one code path of the sending application, and a counted
    /// rejection nobody is shown is a silent one.
    /// </remarks>
    private async Task ReadReceiptAsync(
        HttpResponseMessage response, int sent, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            Report(Refusal(response.StatusCode, sent), null);
            return;
        }

        try
        {
            using var body = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var receipt = await JsonDocument
                .ParseAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (receipt.RootElement.TryGetProperty("rejected", out var rejected)
                && rejected.TryGetInt32(out var count)
                && count > 0)
            {
                Report(
                    $"{count} of {sent} entries were refused as unreadable by the "
                    + "installation and were not stored.",
                    null);
            }
        }
        catch (Exception)
        {
            // A receipt that cannot be read costs nothing: the entries are
            // stored or they are not, and this was only going to say so.
        }
    }

    private string Refusal(HttpStatusCode status, int sent) => status switch
    {
        HttpStatusCode.Unauthorized =>
            $"The ingest token was refused, and {sent} entries were dropped. Check that it "
            + "is the token of a project that still exists and has not been revoked.",

        HttpStatusCode.RequestEntityTooLarge =>
            $"A batch of {sent} entries was past what {_ingest} accepts and was dropped.",

        HttpStatusCode.TooManyRequests =>
            $"{_ingest} is rate limiting this sender, and {sent} entries were dropped.",

        HttpStatusCode.ServiceUnavailable =>
            $"{_ingest} could not store {sent} entries and they are gone.",

        _ => $"{_ingest} answered {(int)status} and {sent} entries were dropped.",
    };

    /// <summary>
    /// Says a delivery failed: the first one at once, and after that at most
    /// once every <see cref="RepeatFailureAfter"/> for as long as the cause
    /// holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cause is the exception's type and message, so a host that cannot be
    /// resolved and a request that ran out of time are two outages rather than
    /// one, and a second cause arriving during the first is said out loud
    /// instead of being swallowed by the first one's quiet.
    /// </para>
    /// <para>
    /// What the repetition adds is the count. Every batch that went unreported
    /// was still a batch that was lost, and an operator reading the second line
    /// wants to know what the outage has cost since the first.
    /// </para>
    /// </remarks>
    private void ReportFailed(int entries, Exception failure)
    {
        var cause = $"{failure.GetType().Name}: {failure.Message}";
        var now = _time.GetUtcNow();

        if (_failing != cause)
        {
            _failing = cause;
            _said = now;
            _lostSinceSaid = 0;
            _lostToTheOutage = entries;

            Report($"{entries} entries were not delivered to {_ingest}.", failure);
            return;
        }

        _lostSinceSaid += entries;
        _lostToTheOutage += entries;

        if (now - _said < RepeatFailureAfter)
        {
            return;
        }

        Report(
            $"{_lostSinceSaid} further entries were not delivered to {_ingest}, which is "
            + "still failing the same way.",
            failure);

        _said = now;
        _lostSinceSaid = 0;
    }

    /// <summary>
    /// Says an outage ended, once, and says what it cost.
    /// </summary>
    /// <remarks>
    /// Whoever saw the first line is owed the last one. Without it the log holds
    /// the beginning of every outage and the end of none, and a reader arriving
    /// afterwards cannot tell an installation that came back in a minute from
    /// one that is still gone. What is counted is what the failed deliveries
    /// carried; entries the queue shed on top of that are counted separately by
    /// <see cref="ReportDrops"/>, and adding them here would say the same loss
    /// twice.
    /// </remarks>
    private void ReportRecovered()
    {
        if (_failing is null)
        {
            return;
        }

        var lost = _lostToTheOutage;

        _failing = null;
        _said = default;
        _lostSinceSaid = 0;
        _lostToTheOutage = 0;

        Report(
            $"Deliveries to {_ingest} are getting through again, and {lost} entries were "
            + "lost while they were not.",
            null);
    }

    /// <summary>
    /// Says how many entries the queue has shed since this last reported, which
    /// is the one thing a full queue would otherwise do silently.
    /// </summary>
    private void ReportDrops()
    {
        var dropped = Interlocked.Exchange(ref _dropped, 0);

        if (dropped > 0)
        {
            Report(
                $"{dropped} entries were dropped before they could be delivered, because "
                + $"the queue of {_options.QueueCapacity} was full. The installation is "
                + "either unreachable or slower than this application is logging.",
                null);
        }
    }

    /// <summary>
    /// Says how many entries the flush ran out of time on, which is the other
    /// way this parts with entries nobody asked it to lose.
    /// </summary>
    /// <remarks>
    /// What is said is that they were still queued, not what became of them: the
    /// pump is not stopped here, so with a client of this delivery's own they
    /// fail as it is disposed underneath them, and with a caller's they may yet
    /// arrive after <see cref="Dispose"/> has returned. Either way the sender
    /// asked to shut down and is owed the number.
    /// </remarks>
    private void ReportAbandoned()
    {
        if (_queued.Reader.CanCount && _queued.Reader.Count is var left and > 0)
        {
            Report(
                $"{left} entries were still queued when the flush timeout of "
                + $"{_options.FlushTimeout} ran out. Raise it, or accept that a shutdown "
                + "costs what an unreachable installation has left in hand.",
                null);
        }
    }

    /// <summary>
    /// Where a report goes when the sender named nowhere else.
    /// </summary>
    /// <remarks>
    /// Standard error, which is where a container's own log is, and it asks
    /// nothing of the application's logging stack — which is the reason
    /// <see cref="EntryDeliveryOptions.OnFailure"/> is a delegate in the first
    /// place. It is applied here rather than as a default on the options, so
    /// that a package above this one can still tell "nowhere named" from "named
    /// somewhere" and put its own channel under it: the Serilog sink decides
    /// exactly that way.
    /// </remarks>
    private static void ToStandardError(string what, Exception? failure) =>
        Console.Error.WriteLine(Describe(what, failure));

    /// <summary>
    /// The one line a built-in report renders as: what happened, and what the
    /// exception was and said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the stack trace.</b> A delivery fails in the same few frames every
    /// time and an unreachable installation fails once per batch, so the trace
    /// is the same paragraph repeated until it is the only thing left in the
    /// sender's log. The type and the message are what identify the fault, and
    /// a sender who wants the object itself writes an
    /// <see cref="EntryDeliveryOptions.OnFailure"/> and gets it unchanged — the
    /// delegate's signature is untouched, and only the two built-in renderings
    /// are shorter than they were.
    /// </para>
    /// <para>
    /// <b>Public because there is more than one built-in target.</b> Standard
    /// error is this class's, and the Serilog sink reports through Serilog's
    /// <c>SelfLog</c> instead because reporting through the logger would hand a
    /// failed delivery straight back to this. One rendering rather than two
    /// keeps them from drifting apart.
    /// </para>
    /// </remarks>
    /// <param name="what">What this had to say, already a whole sentence.</param>
    /// <param name="failure">The exception behind it, where there was one.</param>
    public static string Describe(string what, Exception? failure) =>
        failure is null
            ? $"logaffe: {what}"
            : $"logaffe: {what} {failure.GetType().Name}: {failure.Message}";

    private void Report(string what, Exception? failure)
    {
        try
        {
            (_options.OnFailure ?? ToStandardError).Invoke(what, failure);
        }
        catch (Exception)
        {
            // A reporting callback that throws has nowhere to be reported to,
            // and this promised the application it would not throw.
        }
    }
}

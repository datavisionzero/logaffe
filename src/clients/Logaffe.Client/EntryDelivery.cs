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

    private readonly EntryDeliveryOptions _options;
    private readonly Channel<string> _queued;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _ingest;
    private readonly Task _pump;

    private int _dropped;
    private int _disposed;

    /// <summary>
    /// Starts delivering, with an <see cref="HttpClient"/> of its own.
    /// </summary>
    public EntryDelivery(EntryDeliveryOptions options)
        : this(options, new HttpClient(), ownsHttp: true)
    {
    }

    /// <summary>
    /// Starts delivering over a caller's <see cref="HttpClient"/>, which is not
    /// disposed with this and is how an application that manages its own
    /// handlers — or a test that substitutes one — supplies it.
    /// </summary>
    public EntryDelivery(EntryDeliveryOptions options, HttpClient http)
        : this(options, http, ownsHttp: false)
    {
    }

    private EntryDelivery(EntryDeliveryOptions options, HttpClient http, bool ownsHttp)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsHttp = ownsHttp;
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

            await ReadReceiptAsync(response, batch.Count, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Report($"{batch.Count} entries were not delivered to {_ingest}.", failure);
        }
        finally
        {
            ReportDrops();
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
        Console.Error.WriteLine(
            failure is null ? $"logaffe: {what}" : $"logaffe: {what} {failure}");

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

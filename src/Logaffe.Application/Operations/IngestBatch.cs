using System.Buffers;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;

namespace Logaffe.Application.Operations;

/// <summary>
/// What became of a delivery.
/// </summary>
public enum BatchOutcome
{
    /// <summary>
    /// The batch was taken — in whole or in part. Every entry that could be read
    /// is in the table.
    /// </summary>
    Stored,

    /// <summary>
    /// The batch was over <see cref="Caps.EntriesPerBatch"/> or
    /// <see cref="Caps.BatchBytes"/>, and nothing in it was stored. It is one of
    /// the three refusals <c>docs/ingestion.md</c> allows, and the endpoint
    /// answers it <c>413</c>.
    /// </summary>
    OverTheHardLimit,
}

/// <summary>
/// One line that was not an entry, against the line of the body it was on.
/// </summary>
/// <param name="Line">
/// Counted from one over every line of the body, blank ones included, so that it
/// is the line number the sender's own file has.
/// </param>
public sealed record Rejection(int Line, string Reason);

/// <summary>
/// What a delivery is answered with. Nothing in a sender's control flow depends
/// on it: it exists so that a person debugging a new integration with
/// <c>curl</c> can see what is wrong
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0006-a-batch-is-accepted-in-part.md">ADR 0006</see>).
/// </summary>
public sealed record BatchReceipt(
    BatchOutcome Outcome, int Accepted, int Rejected, IReadOnlyList<Rejection> Reasons);

/// <summary>
/// Takes a delivery: reads the lines, keeps the ones that are entries, counts the
/// ones that are not, and writes what is left in one go.
/// </summary>
/// <remarks>
/// <para>
/// This is the hottest path in the product and the adoption barrier
/// <c>VISION.md</c> judges it by. It streams rather than buffering the body,
/// which is what makes <see cref="Caps.BatchBytes"/> a cap on what is read and
/// not merely a cap on what is accepted — a compression bomb is refused after
/// five mebibytes of it have been decompressed rather than after all of it has.
/// </para>
/// <para>
/// <b>A batch is accepted in part.</b> One broken line never costs the other 999:
/// delivery is fire-and-forget, so the sender will not retry and will not look,
/// and refusing the batch would be a permanent, silent loss (ADR 0006). The whole
/// batch goes only where no part of it can be trusted or afforded, and of those
/// cases this act decides one — the hard limits. The bad token and the rate limit
/// are refused before anything reaches here, and a store that cannot be reached
/// throws through it.
/// </para>
/// </remarks>
public sealed class IngestBatch(
    IEntries entries, IEntryIds ids, RunningTally tally, TimeProvider clock)
{
    /// <summary>
    /// How many reasons come back. It is "the first few" of
    /// <c>docs/ingestion.md</c>: enough that a sender breaking one field shows
    /// the shape of it, few enough that a batch where every line is wrong
    /// answers with a body and not with a second copy of the batch.
    /// </summary>
    public const int ReasonsReported = 5;

    /// <summary>
    /// How much of the body is pulled off the stream at a time. Nothing about
    /// it is a product value — it is the granularity at which the cap above is
    /// noticed, and the buffer is rented rather than allocated because this runs
    /// on every delivery.
    /// </summary>
    private const int ReadBuffer = 32 * 1024;

    private const byte Newline = (byte)'\n';

    public async Task<BatchReceipt> ExecuteAsync(
        Guid projectId, Stream body, CancellationToken cancellationToken)
    {
        // Taken once, before the body is read, because it is the moment the
        // batch arrived rather than the moment the last of it did — a slow
        // upload would otherwise spread one delivery across the clock that
        // retention counts from (ADR 0007).
        var receiptTime = clock.GetUtcNow();

        var read = new List<ClefLine>();
        var reasons = new List<Rejection>();
        var rejected = 0;
        var lineNumber = 0;

        var line = new ArrayBufferWriter<byte>();
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBuffer);
        try
        {
            var delivered = 0L;

            while (true)
            {
                var got = await body.ReadAsync(buffer.AsMemory(0, ReadBuffer), cancellationToken);
                if (got == 0)
                {
                    break;
                }

                // Measured after decompression, which is the whole of why the
                // cap cannot be walked around with a compression bomb.
                delivered += got;
                if (delivered > Caps.BatchBytes)
                {
                    return OverTheHardLimit;
                }

                var chunk = buffer.AsMemory(0, got);
                while (true)
                {
                    var newline = chunk.Span.IndexOf(Newline);
                    if (newline < 0)
                    {
                        line.Write(chunk.Span);
                        break;
                    }

                    line.Write(chunk.Span[..newline]);
                    chunk = chunk[(newline + 1)..];

                    if (!TakeLine())
                    {
                        return OverTheHardLimit;
                    }
                }
            }

            // The last line of a body that did not end in a newline. A body
            // that did leaves nothing here, and an empty line is not an entry.
            if (!TakeLine())
            {
                return OverTheHardLimit;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (read.Count > 0)
        {
            await StoreAsync(projectId, receiptTime, read, cancellationToken);
        }

        return new BatchReceipt(BatchOutcome.Stored, read.Count, rejected, reasons);

        bool TakeLine()
        {
            lineNumber++;

            var content = Trimmed(line.WrittenMemory);
            if (content.IsEmpty)
            {
                // A blank line is not an entry and not a rejection. A trailing
                // newline is how a sender ends a file, not a defect.
                line.Clear();
                return true;
            }

            if (read.Count + rejected == Caps.EntriesPerBatch)
            {
                return false;
            }

            if (ClefLine.TryRead(content, out var entry, out var reason))
            {
                read.Add(entry);
            }
            else
            {
                rejected++;

                // Counted always, reported for the first few. The count is what
                // says something is wrong; the reasons are what say what.
                if (reasons.Count < ReasonsReported)
                {
                    reasons.Add(new Rejection(lineNumber, reason));
                }
            }

            line.Clear();
            return true;
        }
    }

    private static BatchReceipt OverTheHardLimit { get; } =
        new(BatchOutcome.OverTheHardLimit, 0, 0, []);

    /// <summary>
    /// Gives the batch its identities and writes it. The block is reserved for
    /// exactly what is being stored, immediately before the write, so that the
    /// gap a failed write leaves is the smallest it can be — and gaps are
    /// irrelevant anyway (<c>docs/storage.md</c>).
    /// </summary>
    /// <remarks>
    /// The tally is moved last, after the write, and it is an interlocked add
    /// against a number in memory (ADR 0047) — no second write, no read, and
    /// nothing this path waits for. What it counts is what the table took, so a
    /// store that threw counts for nothing.
    /// </remarks>
    private async Task StoreAsync(
        Guid projectId,
        DateTimeOffset receiptTime,
        List<ClefLine> read,
        CancellationToken cancellationToken)
    {
        var first = await ids.ReserveAsync(read.Count, cancellationToken);

        var batch = new List<LogEntry>(read.Count);
        var atErrorOrAbove = 0L;

        for (var index = 0; index < read.Count; index++)
        {
            var entry = read[index];

            if (entry.Level >= Level.Error)
            {
                // Counted here rather than by a second pass over the batch: the
                // entries are already in hand and it is one comparison.
                atErrorOrAbove++;
            }

            batch.Add(new LogEntry
            {
                Id = first + index,
                ProjectId = projectId,
                EventTime = entry.EventTime,
                ReceiptTime = receiptTime,
                Level = entry.Level,
                LoggerName = entry.LoggerName,
                Instance = entry.Instance,
                TraceId = entry.TraceId,
                SpanId = entry.SpanId,
                MessageTemplate = entry.MessageTemplate,
                RenderedMessage = entry.RenderedMessage,
                Exception = entry.Exception,
                Properties = entry.Properties,
                MessageTruncated = entry.MessageTruncated,
                ExceptionTruncated = entry.ExceptionTruncated,
            });
        }

        await entries.WriteAsync(batch, cancellationToken);

        tally.Record(projectId, receiptTime, batch.Count, atErrorOrAbove);
    }

    /// <summary>
    /// A line without the whitespace around it, which is what makes a
    /// <c>\r\n</c> body and a <c>\n</c> body the same delivery.
    /// </summary>
    private static ReadOnlyMemory<byte> Trimmed(ReadOnlyMemory<byte> line)
    {
        var span = line.Span;
        var start = 0;
        var end = span.Length;

        while (start < end && IsBlank(span[start]))
        {
            start++;
        }

        while (end > start && IsBlank(span[end - 1]))
        {
            end--;
        }

        return line[start..end];
    }

    private static bool IsBlank(byte character) =>
        character is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}

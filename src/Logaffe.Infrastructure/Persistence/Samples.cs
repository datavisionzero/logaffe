using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The sample rows: what a delivery writes and what the sweep takes out.
/// </summary>
/// <remarks>
/// Through EF Core rather than around it. The log path earns a binary
/// <c>COPY</c> at eleven thousand entries a second (ADR 0003); twenty hosts
/// watching three filesystems each write eighty rows a minute, which is 1.3 a
/// second and earns nothing of the sort.
/// </remarks>
public sealed class Samples(LogaffeDbContext context) : ISamples
{
    public async Task WriteAsync(
        Sample sample,
        IReadOnlyList<FilesystemReading> filesystems,
        CancellationToken cancellationToken)
    {
        context.Samples.Add(sample);
        context.FilesystemReadings.AddRange(filesystems);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A host reporting twice for one minute, which the natural key
            // refuses. What is already there is the reading for that minute and
            // this one is the duplicate, so it is dropped rather than merged:
            // the two are readings of the same machine seconds apart, and which
            // of them is "right" is not a question with an answer.
            //
            // The context is cleared because a failed save leaves the added rows
            // tracked, and this scope may still be asked to write something else.
            context.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<Guid>> HostsWithSamplesAsync(
        CancellationToken cancellationToken) =>
        await context.Samples
            .Select(s => s.HostId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> RemoveReceivedBeforeAsync(
        Guid hostId,
        DateTimeOffset receivedBefore,
        int portion,
        CancellationToken cancellationToken)
    {
        // The filesystem readings first, because a sample without them is a
        // partial minute and the other order leaves one behind if the second
        // statement never runs. They are bounded by the same portion through the
        // samples they belong to rather than by a count of their own — a host
        // watching three mounts has three of these per sample.
        var moments = await context.Samples
            .Where(s => s.HostId == hostId && s.ReceiptTime < receivedBefore)
            .OrderBy(s => s.ReceiptTime)
            .Select(s => s.ReceiptTime)
            .Take(portion)
            .ToListAsync(cancellationToken);

        if (moments.Count == 0)
        {
            return 0;
        }

        var upTo = moments[^1];

        await context.FilesystemReadings
            .Where(r => r.HostId == hostId && r.ReceiptTime <= upTo)
            .ExecuteDeleteAsync(cancellationToken);

        return await context.Samples
            .Where(s => s.HostId == hostId && s.ReceiptTime <= upTo)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<long> CountReceivedBeforeAsync(
        DateTimeOffset receivedBefore, CancellationToken cancellationToken) =>
        context.Samples.LongCountAsync(s => s.ReceiptTime < receivedBefore, cancellationToken);
}

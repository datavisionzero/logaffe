using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logaffe.Infrastructure.Persistence.Log;

/// <summary>
/// The counter that gives entries their identities, seeded once from what the
/// table already holds.
/// </summary>
/// <remarks>
/// <para>
/// An installation is a <b>single writer</b> — one container, one ingestion
/// endpoint — which is what makes a number in memory sufficient where a
/// distributed writer would need a sequence or a UUIDv7. It is what
/// <c>docs/storage.md</c> asks for, and the reason it asks is that binary
/// <c>COPY</c> carries the value with the row: a sequence would mean a
/// <c>nextval</c> per entry or a round trip per batch on the hottest path in the
/// product.
/// </para>
/// <para>
/// <b>Seeded on the first delivery rather than at startup.</b> The high-water
/// mark is read out of a table a migration may be about to create, and the
/// migrations run as a hosted service — so asking at startup would mean ordering
/// this behind them for a number that is not needed until something delivers.
/// The read is one index scan and it happens once in the life of a process.
/// </para>
/// <para>
/// A block handed to a batch that then fails to store is simply gone. Gaps are
/// irrelevant: nothing counts these and nothing assumes they are dense. What is
/// load-bearing is that no two rows share one, because the cursor of
/// <c>docs/querying.md</c> is <c>(event_time, id)</c> and is only total because
/// of it — which is why the handing out is an interlocked add and not a read
/// followed by a write.
/// </para>
/// </remarks>
public sealed class EntryIds(IServiceScopeFactory scopes) : IEntryIds
{
    private readonly SemaphoreSlim _seeding = new(1, 1);

    private long _handedOut;
    private volatile bool _seeded;

    public async Task<long> ReserveAsync(int count, CancellationToken cancellationToken)
    {
        if (!_seeded)
        {
            await SeedAsync(cancellationToken);
        }

        // The block runs up to the value this returns, so the first of it is
        // that value less the block and one more.
        return Interlocked.Add(ref _handedOut, count) - count + 1;
    }

    /// <summary>
    /// Reads the high-water mark, behind a gate so that two deliveries arriving
    /// together seed once — the second waits here rather than reading a table
    /// the first has not finished asking about.
    /// </summary>
    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        await _seeding.WaitAsync(cancellationToken);
        try
        {
            if (_seeded)
            {
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<LogaffeDbContext>();

            // Coalesced, because an installation that has never received
            // anything has no maximum, and its first entry is one.
            _handedOut = await context.Database
                .SqlQuery<long>($"""select coalesce(max(id), 0) as "Value" from log_entry""")
                .SingleAsync(cancellationToken);

            // Last, and volatile: it is what every other caller reads instead of
            // taking this gate, so nothing may reach it before the mark is in
            // place.
            _seeded = true;
        }
        finally
        {
            _seeding.Release();
        }
    }
}

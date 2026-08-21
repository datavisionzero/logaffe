using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// What the database occupies, asked of the database.
/// </summary>
/// <remarks>
/// <para>
/// <c>pg_database_size</c> reads the catalogue rather than the tables, so it
/// costs the same on an installation holding forty million entries as on an
/// empty one — which is what makes it something a screen can ask while somebody
/// is typing. Summing the tables this installation declares would have been
/// arithmetic over a shape that can change, and it would have left out
/// everything Postgres holds beside them.
/// </para>
/// <para>
/// It is the whole database and not the entries, deliberately: the operator is
/// looking at this because of a disk, and a disk does not distinguish the log
/// table from its indexes, the samples, the tally or the space a sweep freed and
/// left claimed (ADR 0023).
/// </para>
/// </remarks>
public sealed class StoreFootprint(LogaffeDbContext context) : IStoreFootprint
{
    public async Task<long> HeldBytesAsync(CancellationToken cancellationToken)
    {
        // `Value` is the column name a scalar query is read back through, and
        // the cast is because Postgres answers this one as a numeric wide enough
        // for a database nobody will ever have.
        var held = await context.Database
            .SqlQuery<long>(
                $"""select pg_database_size(current_database())::bigint as "Value" """)
            .ToListAsync(cancellationToken);

        return held.Single();
    }
}

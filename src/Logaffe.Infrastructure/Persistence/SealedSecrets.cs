using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Takes the sample out of everything the installation holds sealed.
/// </summary>
/// <remarks>
/// The users' TOTP secrets first, because an installation may have those and no
/// tokens at all — somebody who was bootstrapped and has not made a project yet
/// is the case the token tables miss entirely, and those secrets are sealed
/// under the same key (ADR 0057). Then the ingest tokens, then the agent tokens,
/// which is the order in which an installation that has anything has them.
/// </remarks>
public sealed class SealedSecrets(LogaffeDbContext context) : ISealedSecrets
{
    public async Task<IReadOnlyList<byte[]>> SampleAsync(
        int count, CancellationToken cancellationToken)
    {
        var sample = new List<byte[]>(count);

        // Ordered by id so that the row-limiting operator has something to order
        // by, which is what every query in this file does and what keeps EF from
        // warning on a start an operator is reading.
        sample.AddRange(await context.Identities
            .OfType<Logaffe.Domain.Identities.User>()
            .Where(u => u.EncryptedSecondFactorSecret != null)
            .OrderBy(u => u.Id)
            .Select(u => u.EncryptedSecondFactorSecret!)
            .Take(count)
            .ToListAsync(cancellationToken));

        if (sample.Count < count)
        {
            sample.AddRange(await context.IngestTokens
                .OrderBy(t => t.Id)
                .Select(t => t.EncryptedSecret)
                .Take(count - sample.Count)
                .ToListAsync(cancellationToken));
        }

        if (sample.Count < count)
        {
            sample.AddRange(await context.AgentTokens
                .OrderBy(t => t.Id)
                .Select(t => t.EncryptedSecret)
                .Take(count - sample.Count)
                .ToListAsync(cancellationToken));
        }

        return sample;
    }
}

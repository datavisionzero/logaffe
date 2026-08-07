using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Takes the sample out of everything the installation holds sealed.
/// </summary>
/// <remarks>
/// The operator's TOTP secret first, because a claimed installation has exactly
/// one of those and may have no tokens at all — an operator who claimed an
/// installation and has not made a project yet is the case the token tables miss
/// entirely, and it is sealed under the same key (ADR 0032). Then the ingest
/// tokens, then the agent tokens, which is the order in which an installation
/// that has anything has them.
/// </remarks>
public sealed class SealedSecrets(LogaffeDbContext context) : ISealedSecrets
{
    public async Task<IReadOnlyList<byte[]>> SampleAsync(
        int count, CancellationToken cancellationToken)
    {
        var sample = await context.Operators
            .Select(o => o.EncryptedSecondFactorSecret)
            .Take(count)
            .ToListAsync(cancellationToken);

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

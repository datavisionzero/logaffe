using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Takes the sample out of the token tables.
/// </summary>
/// <remarks>
/// The ingest tokens first because an installation that has anything has those,
/// and the agent tokens only if that came up short. When the operator's TOTP
/// secret joins them it belongs in this sample too — it is sealed under the same
/// key (ADR 0032), and a claimed installation with no tokens at all is exactly
/// the case the ingest tokens alone would miss.
/// </remarks>
public sealed class SealedSecrets(LogaffeDbContext context) : ISealedSecrets
{
    public async Task<IReadOnlyList<byte[]>> SampleAsync(
        int count, CancellationToken cancellationToken)
    {
        var sample = await context.IngestTokens
            .OrderBy(t => t.Id)
            .Select(t => t.EncryptedSecret)
            .Take(count)
            .ToListAsync(cancellationToken);

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

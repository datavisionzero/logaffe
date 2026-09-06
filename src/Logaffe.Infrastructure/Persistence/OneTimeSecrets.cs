using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <inheritdoc cref="IOneTimeSecrets"/>
public sealed class OneTimeSecrets(LogaffeDbContext context) : IOneTimeSecrets
{
    public Task<OneTimeSecret?> FindAsync(byte[] hash, CancellationToken cancellationToken) =>
        context.OneTimeSecrets.SingleOrDefaultAsync(s => s.SecretHash == hash, cancellationToken);

    public async Task IssueAsync(
        OneTimeSecret secret, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Read, spend, add, save: one SaveChanges is one transaction, so there
        // is no moment at which two links of one purpose are live (ADR 0053).
        var live = await context.OneTimeSecrets
            .Where(s => s.UserId == secret.UserId
                && s.Purpose == secret.Purpose
                && s.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var previous in live)
        {
            previous.ReplacedAt(now);
        }

        context.OneTimeSecrets.Add(secret);

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsPendingAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.OneTimeSecrets.AnyAsync(
            s => s.PendingNormalizedEmail == normalizedEmail && s.UsedAt == null,
            cancellationToken);

    public async Task RecordAsync(OneTimeSecret secret, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}

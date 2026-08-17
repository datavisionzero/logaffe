using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The one row an installation holds about itself.
/// </summary>
/// <remarks>
/// The instant is here rather than in a file on the host volume beside the key
/// (ADR 0034), which is what makes the first run the run that created the
/// schema — and what keeps Host Recovery writing to one store rather than two
/// that can disagree. The drawn claim secret's hash is in the same row for the
/// same reasons (ADR 0040).
/// </remarks>
public sealed class Installation(LogaffeDbContext context) : IInstallation
{
    public Task<ClaimGuard?> ReadClaimGuardAsync(CancellationToken cancellationToken) =>
        context.ClaimGuards.SingleOrDefaultAsync(cancellationToken);

    public async Task<ClaimGuard> OpenClaimAsync(
        DateTimeOffset firstRunAt, CancellationToken cancellationToken)
    {
        var existing = await context.ClaimGuards.SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Every start after the first. This is what "a restart does not
            // extend it" is: the row is already there and nothing is written.
            return existing;
        }

        var guard = ClaimGuard.OpenedOnFirstRun(firstRunAt);
        context.ClaimGuards.Add(guard);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return guard;
        }
        catch (DbUpdateException)
        {
            // Two containers came up at once and the single-row unique index
            // decided it, exactly as it decides the claim itself. Whichever row
            // is there is the installation's guard, and this start reads it
            // rather than insisting on its own — including the secret the other
            // one drew, which is the one that was handed over.
            context.ChangeTracker.Clear();

            return await context.ClaimGuards.SingleAsync(cancellationToken);
        }
    }

    public async Task<ClaimGuard> ArmClaimAsync(
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        var guard = await context.ClaimGuards.SingleOrDefaultAsync(cancellationToken);

        if (guard is null)
        {
            // A database somebody created without ever starting the
            // installation. Host Recovery is the command that hands an
            // installation back, so it writes the row rather than refusing over
            // one that should have been there.
            guard = ClaimGuard.OpenedOnFirstRun(at);
            context.ClaimGuards.Add(guard);
        }
        else
        {
            guard.ArmAt(at);
        }

        await context.SaveChangesAsync(cancellationToken);

        return guard;
    }

    public Task RecordClaimAsync(ClaimGuard guard, CancellationToken cancellationToken)
    {
        // Attached already on every path that gets here — the guard being written
        // back is the one this context read or added a moment ago — so this is
        // the save and nothing else.
        context.ClaimGuards.Update(guard);

        return context.SaveChangesAsync(cancellationToken);
    }
}

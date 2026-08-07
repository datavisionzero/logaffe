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
/// that can disagree.
/// </remarks>
public sealed class Installation(LogaffeDbContext context) : IInstallation
{
    public Task<ClaimWindow?> ReadClaimWindowAsync(CancellationToken cancellationToken) =>
        context.ClaimWindows.SingleOrDefaultAsync(cancellationToken);

    public async Task<ClaimWindow> OpenClaimWindowAsync(
        DateTimeOffset firstRunAt, CancellationToken cancellationToken)
    {
        var existing = await context.ClaimWindows.SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Every start after the first. This is what "a restart does not
            // extend it" is: the row is already there and nothing is written.
            return existing;
        }

        var window = ClaimWindow.OpenedOnFirstRun(firstRunAt);
        context.ClaimWindows.Add(window);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return window;
        }
        catch (DbUpdateException)
        {
            // Two containers came up at once and the single-row unique index
            // decided it, exactly as it decides the claim itself. Whichever row
            // is there is the installation's window, and this start reads it
            // rather than insisting on its own.
            context.ChangeTracker.Clear();

            return await context.ClaimWindows.SingleAsync(cancellationToken);
        }
    }

    public async Task<ClaimWindow> ArmClaimWindowAsync(
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        var window = await context.ClaimWindows.SingleOrDefaultAsync(cancellationToken);

        if (window is null)
        {
            // A database somebody created without ever starting the
            // installation. Host Recovery is the command that hands an
            // installation back, so it writes the row rather than refusing over
            // one that should have been there.
            window = ClaimWindow.OpenedOnFirstRun(at);
            context.ClaimWindows.Add(window);
        }
        else
        {
            window.ArmAt(at);
        }

        await context.SaveChangesAsync(cancellationToken);

        return window;
    }
}

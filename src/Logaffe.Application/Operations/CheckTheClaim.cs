using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether this installation belongs to anybody, and whether a stranger can
/// still take it.
/// </summary>
/// <remarks>
/// It is the whole state of the only surface an unclaimed installation exposes,
/// and it is deliberately three plain facts rather than a screen name: which
/// screen to show is the single-page application's, and it has to show a
/// different one for a claim that can proceed, a claim that has lapsed, and an
/// installation that simply wants a sign-in.
/// </remarks>
/// <param name="ClosesAt">
/// When the window shuts, and <c>null</c> whenever there is nothing to count
/// down to. An operator whose window has lapsed does not need to know by how
/// much — they need the host command, and the screen names it.
/// </param>
public sealed record ClaimState(bool IsClaimed, bool WindowIsOpen, DateTimeOffset? ClosesAt);

/// <inheritdoc cref="ClaimState"/>
public sealed class CheckTheClaim(
    IOperators operators, IInstallation installation, TimeProvider clock)
{
    public async Task<ClaimState> ExecuteAsync(CancellationToken cancellationToken)
    {
        // Asked first, and asked as its own question rather than as a null check
        // on the account row: this path must not read a credential to answer
        // whether there is one.
        if (await operators.IsClaimedAsync(cancellationToken))
        {
            return new ClaimState(IsClaimed: true, WindowIsOpen: false, ClosesAt: null);
        }

        var window = await installation.ReadClaimWindowAsync(cancellationToken);
        var open = window is not null && window.IsOpenAt(clock.GetUtcNow());

        return new ClaimState(
            IsClaimed: false, WindowIsOpen: open, ClosesAt: open ? window!.ClosesAt : null);
    }
}

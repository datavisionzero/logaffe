using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether this installation belongs to anybody, and what a stranger would have
/// to have in hand to take it.
/// </summary>
/// <remarks>
/// It is the whole state of the only surface an unclaimed installation exposes,
/// and it is deliberately plain facts rather than a screen name: which screen to
/// show is the single-page application's, and it has to show a different one for a
/// claim that wants a secret, a claim that is simply open, a claim that has lapsed
/// and an installation that wants a sign-in.
/// </remarks>
/// <param name="CanBeClaimed">
/// Whether a claim would be considered at all. It is false on an installation
/// that has an operator, and on one in window mode whose window has closed —
/// which is the screen that names the host command.
/// </param>
/// <param name="NeedsSecret">
/// Whether the screen has to ask for the claim secret. It says nothing about
/// whether the asker knows one.
/// </param>
/// <param name="ClosesAt">
/// When the window shuts, and <c>null</c> whenever there is nothing to count down
/// to — which is every installation guarded by a secret, and every one whose
/// window has already lapsed. An operator whose window lapsed does not need to
/// know by how much; they need the host command, and the screen names it.
/// </param>
public sealed record ClaimState(
    bool IsClaimed, bool CanBeClaimed, bool NeedsSecret, DateTimeOffset? ClosesAt);

/// <inheritdoc cref="ClaimState"/>
public sealed class CheckTheClaim(
    IOperators operators,
    IInstallation installation,
    ClaimSettings settings,
    TimeProvider clock)
{
    public async Task<ClaimState> ExecuteAsync(CancellationToken cancellationToken)
    {
        // Asked first, and asked as its own question rather than as a null check
        // on the account row: this path must not read a credential to answer
        // whether there is one.
        if (await operators.IsClaimedAsync(cancellationToken))
        {
            return new ClaimState(
                IsClaimed: true, CanBeClaimed: false, NeedsSecret: false, ClosesAt: null);
        }

        var guard = await installation.ReadClaimGuardAsync(cancellationToken);
        if (guard is null)
        {
            // No first run was ever written, which is a database somebody made by
            // hand. Nothing admits a claim until a start writes the row.
            return new ClaimState(
                IsClaimed: false, CanBeClaimed: false, NeedsSecret: false, ClosesAt: null);
        }

        if (settings.Mode is ClaimMode.Secret)
        {
            // There is no deadline here and nothing to count down to: what
            // stands in front of the claim is the secret, and the screen asks
            // for it however long the installation has been standing.
            return new ClaimState(
                IsClaimed: false,
                CanBeClaimed: settings.SuppliedSecret is not null || guard.HasDrawnSecret,
                NeedsSecret: true,
                ClosesAt: null);
        }

        var open = guard.WindowIsOpenAt(clock.GetUtcNow());

        return new ClaimState(
            IsClaimed: false,
            CanBeClaimed: open,
            NeedsSecret: false,
            ClosesAt: open ? guard.WindowClosesAt : null);
    }
}

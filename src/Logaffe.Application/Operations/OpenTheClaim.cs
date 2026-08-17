using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// The guard as it stands after a start, and the secret this start drew if it
/// drew one.
/// </summary>
/// <param name="Drawn">
/// The secret, in the clear, on exactly the start that drew it and never again.
/// The caller says it out loud once — a container log is what somebody watching a
/// first start is already reading — and the file it was written to is what every
/// later start names instead.
/// </param>
public sealed record OpenedClaim(ClaimGuard Guard, ClaimSecret? Drawn);

/// <summary>
/// Writes the installation's first run, and draws the secret that guards its
/// claim when it is configured to have one of its own.
/// </summary>
/// <remarks>
/// <para>
/// The first run runs on every start and writes on exactly one of them. That is
/// what makes a restart not extend the window (<c>docs/setup.md</c>): the deadline
/// belongs to the installation rather than to the process, so nobody gains
/// anything by forcing one. Which start does the writing is the store's to decide,
/// and it decides it the way the claim itself is decided — by the row that is
/// already there (ADR 0034).
/// </para>
/// <para>
/// The secret is drawn on the same terms: once, on the start that finds none, and
/// only while the installation is unclaimed and configured to draw its own
/// (ADR 0040). An installation whose secret comes from configuration draws
/// nothing and stores nothing.
/// </para>
/// <para>
/// <b>The hash is written before the secret is handed over.</b> A failure between
/// the two leaves an installation whose secret nobody was given, which is a
/// locked door and one <c>logaffe recover</c> away; the other order would leave a
/// secret in a file that opens nothing, which reads to the operator as the
/// product being wrong.
/// </para>
/// </remarks>
public sealed class OpenTheClaim(
    IInstallation installation,
    IOperators operators,
    IClaimSecretHandover handover,
    ClaimSettings settings,
    TimeProvider clock)
{
    /// <summary>The guard as it stands after this start, whoever wrote it.</summary>
    public async Task<OpenedClaim> ExecuteAsync(CancellationToken cancellationToken)
    {
        var guard = await installation.OpenClaimAsync(clock.GetUtcNow(), cancellationToken);

        // Asked as its own question rather than as a null check on the account
        // row, so that this path does not read a credential to answer whether
        // there is one. An installation with an operator gets no secret drawn for
        // it: there is no re-claim while claimed, and a secret drawn now would be
        // a credential written to the volume of an installation in ordinary use.
        if (await operators.IsClaimedAsync(cancellationToken)
            || !settings.DrawsItsOwnSecret
            || guard.HasDrawnSecret)
        {
            return new OpenedClaim(guard, null);
        }

        var secret = ClaimSecret.Draw();
        guard.DrewSecret(secret);

        await installation.RecordClaimAsync(guard, cancellationToken);
        await handover.WriteAsync(secret, cancellationToken);

        return new OpenedClaim(guard, secret);
    }
}

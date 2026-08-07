using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the claimant is shown before they can finish: an authenticator to
/// enrol, a sheet of codes to keep, and the sealed copy that carries both back.
/// </summary>
/// <param name="SecondFactorSecret">
/// The secret in text, for anyone typing it into an app by hand rather than
/// scanning.
/// </param>
/// <param name="EnrolmentUri">The <c>otpauth:</c> address the QR code carries.</param>
/// <param name="BackupCodes">
/// Ten codes, shown once. Nothing keeps them afterwards — not the installation
/// and not this record, which lives for the length of one response
/// (ADR 0032).
/// </param>
/// <param name="Ticket">
/// The same material sealed under the installation's key, handed back with the
/// last step so that the installation knows it drew them (ADR 0035).
/// </param>
public sealed record Enrolment(
    string SecondFactorSecret,
    string EnrolmentUri,
    IReadOnlyList<BackupCodeText> BackupCodes,
    string Ticket);

/// <summary>
/// The state the enrolment was asked for in, and the enrolment when that state
/// allowed one.
/// </summary>
/// <remarks>
/// The state comes back whether or not anything was drawn, because the two ways
/// of refusing are two different screens — an installation that already has an
/// operator, and one whose window has lapsed — and the caller would otherwise
/// have to ask a second time to tell them apart.
/// </remarks>
public sealed record BegunEnrolment(ClaimState State, Enrolment? Enrolment);

/// <summary>
/// The step before the claim, which stores nothing.
/// </summary>
/// <remarks>
/// <para>
/// Every step before the last is a form with no effect (ADR 0014): the secret
/// drawn here is not enrolled, the codes are not the operator's, and abandoning
/// the flow at this point leaves the installation exactly as unclaimed as it
/// was. What makes that affordable without a half-claimed row is the ticket —
/// the material goes back to the browser, and the installation keeps only the
/// ability to recognize its own seal.
/// </para>
/// <para>
/// It is drawn here rather than in the last step because the operator has to
/// enrol an authenticator and write down a sheet of codes <em>before</em> they
/// can prove they have either, and proving it is what the last step asks for.
/// </para>
/// </remarks>
public sealed class BeginEnrolment(
    CheckTheClaim check,
    IInstallation installation,
    ISecondFactor secondFactor,
    ISecretCipher cipher)
{
    /// <param name="installationName">
    /// What an authenticator app should call this installation in its list.
    /// There is no username to put there (ADR 0015), so it is the address the
    /// operator reached it by — which only an adapter knows.
    /// </param>
    public async Task<BegunEnrolment> ExecuteAsync(
        string installationName, CancellationToken cancellationToken)
    {
        var state = await check.ExecuteAsync(cancellationToken);
        if (state.IsClaimed || !state.WindowIsOpen)
        {
            return new BegunEnrolment(state, null);
        }

        // Read again rather than carried out of the check, because the ticket
        // names the window it belongs to and that name has to be the row's own
        // value rather than the deadline derived from it.
        var window = await installation.ReadClaimWindowAsync(cancellationToken);
        if (window is null)
        {
            return new BegunEnrolment(state, null);
        }

        var secret = secondFactor.MintSecret();
        var codes = Enumerable.Range(0, BackupCode.SetSize)
            .Select(_ => BackupCodeText.Mint())
            .ToList();

        var ticket = new ClaimTicket(
            window.OpenedAt, secret, [.. codes.Select(code => code.Hash)]);

        return new BegunEnrolment(
            state,
            new Enrolment(
                secret,
                secondFactor.EnrolmentUri(secret, installationName),
                codes,
                ticket.SealedWith(cipher)));
    }
}

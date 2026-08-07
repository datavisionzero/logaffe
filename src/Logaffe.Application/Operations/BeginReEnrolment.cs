using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the operator is shown before a re-enrolment can be confirmed: an
/// authenticator to enrol, the sheet of codes that replaces theirs, and the
/// sealed copy that carries both back.
/// </summary>
/// <remarks>
/// It is <see cref="Enrolment"/> again, for the account that already exists. The
/// two are separate records rather than one because the tickets they carry are
/// bound to different things and neither caller should be able to hand its
/// ticket to the other's act.
/// </remarks>
/// <param name="BackupCodes">
/// Ten codes, shown once and not the operator's yet. Nothing keeps them: they
/// become rows only if the re-enrolment is confirmed, and the set they replace
/// is untouched until then.
/// </param>
public sealed record ReEnrolment(
    string SecondFactorSecret,
    string EnrolmentUri,
    IReadOnlyList<BackupCodeText> BackupCodes,
    string Ticket);

/// <summary>
/// The step before a re-enrolment, which stores nothing.
/// </summary>
/// <remarks>
/// <para>
/// It is the claim's first step for an installation that has an operator, and it
/// holds the same promise: the secret drawn here is not enrolled, the codes are
/// not the operator's, and abandoning the screen leaves the account exactly as it
/// was — the second factor that worked this morning still works this evening.
/// </para>
/// <para>
/// It asks for nothing but the session. Drawing a secret and ten codes proves
/// nothing and changes nothing, and demanding the password twice — once to see
/// the QR code and again to confirm it — would be asking for it at the step
/// where it does no work. The step that replaces the row is where every
/// credential is required.
/// </para>
/// </remarks>
public sealed class BeginReEnrolment(
    IOperators operators,
    ISecondFactor secondFactor,
    ISecretCipher cipher,
    TimeProvider clock)
{
    /// <param name="installationName">
    /// What an authenticator app should call this installation in its list.
    /// There is no username to put there (ADR 0015), so it is the address the
    /// operator reached it by — which only an adapter knows.
    /// </param>
    /// <returns>
    /// The enrolment to show, or <c>null</c> when there is no account to
    /// re-enrol — which behind a session means Host Recovery ran a moment ago.
    /// </returns>
    public async Task<ReEnrolment?> ExecuteAsync(
        string installationName, CancellationToken cancellationToken)
    {
        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null)
        {
            return null;
        }

        var secret = secondFactor.MintSecret();
        var codes = Enumerable.Range(0, BackupCode.SetSize)
            .Select(_ => BackupCodeText.Mint())
            .ToList();

        var ticket = new ReEnrolmentTicket(
            theOperator.Id,
            clock.GetUtcNow(),
            secret,
            [.. codes.Select(code => code.Hash)]);

        return new ReEnrolment(
            secret,
            secondFactor.EnrolmentUri(secret, installationName),
            codes,
            ticket.SealedWith(cipher));
    }
}

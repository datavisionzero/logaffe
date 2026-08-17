using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the operator is shown before an enrolment can be confirmed: an
/// authenticator to enrol, the sheet of codes that comes with it, and the sealed
/// copy that carries both back.
/// </summary>
/// <param name="BackupCodes">
/// Ten codes, shown once and not the operator's yet. Nothing keeps them: they
/// become rows only if the enrolment is confirmed, and whatever set was there
/// before is untouched until then.
/// </param>
public sealed record Enrolment(
    string SecondFactorSecret,
    string EnrolmentUri,
    IReadOnlyList<BackupCodeText> BackupCodes,
    string Ticket);

/// <summary>
/// The step before an enrolment, which stores nothing.
/// </summary>
/// <remarks>
/// <para>
/// It is one act whether the operator is enrolling a second factor for the first
/// time or replacing the phone that held the last one (ADR 0041), and it holds
/// the same promise either way: the secret drawn here is not enrolled, the codes
/// are not the operator's, and abandoning the screen leaves the account exactly as
/// it was — a second factor that worked this morning still works this evening,
/// and an account that had none still has none.
/// </para>
/// <para>
/// It asks for nothing but the session. Drawing a secret and ten codes proves
/// nothing and changes nothing, and demanding the password twice — once to see
/// the QR code and again to confirm it — would be asking for it at the step
/// where it does no work. The step that writes the row is where every credential
/// is required.
/// </para>
/// </remarks>
public sealed class BeginEnrolment(
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
    /// The enrolment to show, or <c>null</c> when there is no account to enrol
    /// for — which behind a session means Host Recovery ran a moment ago.
    /// </returns>
    public async Task<Enrolment?> ExecuteAsync(
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

        var ticket = new EnrolmentTicket(
            theOperator.Id,
            clock.GetUtcNow(),
            secret,
            [.. codes.Select(code => code.Hash)]);

        return new Enrolment(
            secret,
            secondFactor.EnrolmentUri(secret, installationName),
            codes,
            ticket.SealedWith(cipher));
    }
}

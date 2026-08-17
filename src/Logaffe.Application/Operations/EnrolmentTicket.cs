using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the installation drew for an enrolment in progress, carried by the
/// browser between the two requests and sealed so that only the installation can
/// read it.
/// </summary>
/// <remarks>
/// <para>
/// Enrolling a second factor fits in no single request: a secret has to be shown,
/// scanned and confirmed before it goes into the row, and nothing may be stored in
/// between — a half-written second factor is an operator locked out of their own
/// installation. So the values live in the browser, and this sealed copy is what
/// says the installation drew them at full entropy rather than the caller
/// (ADR 0036).
/// </para>
/// <para>
/// <b>It is the only ticket type there is.</b> The claim used to carry one of its
/// own, bound to the claim window it was drawn in (ADR 0035); with the second
/// factor out of the claim (ADR 0041) there is one enrolment path, and this
/// ticket is bound to the operator and a deadline — the account it belongs to can
/// be replaced underneath it, and every enrolment happens behind a full
/// credential.
/// </para>
/// </remarks>
/// <param name="OperatorId">
/// Whose enrolment this is. A ticket drawn before a Host Recovery is refused
/// after it, because the account that drew it is gone and the one that exists
/// now never saw it.
/// </param>
/// <param name="DrawnAt">
/// When it was drawn, which is what <see cref="Lifetime"/> is measured from.
/// </param>
public sealed record EnrolmentTicket(
    Guid OperatorId,
    DateTimeOffset DrawnAt,
    string SecondFactorSecret,
    IReadOnlyList<byte[]> BackupCodeHashes)
{
    /// <summary>
    /// The half hour the claim window also gives, and for the same reason: it is
    /// what one person needs to scan a code and type six digits.
    /// </summary>
    /// <remarks>
    /// A ticket left in a closed tab admits nothing on its own — the request
    /// that spends it carries the password as well, and the second factor in use
    /// when there is one — so this is a deadline on a secret nobody can use
    /// rather than the thing standing between an attacker and the account.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Seals the ticket under the key on the host volume and writes it in
    /// base64url, which is what survives a JSON body and a copy-paste.
    /// </summary>
    public string SealedWith(ISecretCipher cipher) =>
        Base64Url.EncodeToString(cipher.Encrypt(JsonSerializer.Serialize(this)));

    /// <summary>
    /// Opens a returned ticket, and refuses anything this installation did not
    /// seal — which needs the key, so a ticket cannot outlive the key that sealed
    /// it while the installation is serving.
    /// </summary>
    public static bool TryOpen(string? value, ISecretCipher cipher, out EnrolmentTicket ticket)
    {
        ticket = null!;

        if (value is null || !Base64Url.IsValid(value))
        {
            return false;
        }

        try
        {
            var opened = JsonSerializer.Deserialize<EnrolmentTicket>(
                cipher.Decrypt(Base64Url.DecodeFromChars(value)));

            if (opened is null
                || string.IsNullOrEmpty(opened.SecondFactorSecret)
                || opened.BackupCodeHashes.Count != BackupCode.SetSize
                || opened.BackupCodeHashes.Any(hash => hash.Length != BackupCode.HashLength)
                || opened.BackupCodeHashes.Select(Convert.ToHexString).Distinct().Count()
                    != BackupCode.SetSize)
            {
                return false;
            }

            ticket = opened;
            return true;
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether this is <paramref name="theOperator"/>'s and still current.
    /// </summary>
    public bool BelongsTo(Operator theOperator, DateTimeOffset now) =>
        OperatorId == theOperator.Id && now < DrawnAt + Lifetime;
}

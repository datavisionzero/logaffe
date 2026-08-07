using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the installation drew for a re-enrolment in progress, carried by the
/// browser between the two requests and sealed so that only the installation can
/// read it.
/// </summary>
/// <remarks>
/// <para>
/// A re-enrolment has the shape the claim already solved: a new secret has to be
/// shown, scanned and confirmed before it replaces the one in the row, and
/// nothing may be stored in between — a half-replaced second factor is an
/// operator locked out of their own installation. So it is solved the same way
/// (ADR 0035): the values live in the browser, and this sealed copy is what says
/// the installation drew them at full entropy rather than the caller.
/// </para>
/// <para>
/// <b>It is a second type rather than a generalization of
/// <see cref="ClaimTicket"/>.</b> The two are bound to different things — a
/// claim ticket to the window it was drawn in, because the installation's notion
/// of who may claim it changes there, and this one to the operator and a
/// deadline, because there is no window here and the account it belongs to can
/// be replaced underneath it. One type carrying both bindings would carry a
/// field that is empty in half its uses, and it would put the finished claim
/// path into the blast radius of every change made here.
/// </para>
/// </remarks>
/// <param name="OperatorId">
/// Whose re-enrolment this is. A ticket drawn before a Host Recovery is refused
/// after it, because the account that drew it is gone and the one that exists
/// now never saw it.
/// </param>
/// <param name="DrawnAt">
/// When it was drawn, which is what <see cref="Lifetime"/> is measured from.
/// </param>
public sealed record ReEnrolmentTicket(
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
    /// that spends it carries the password and the current second factor as
    /// well — so this is a deadline on a secret nobody can use rather than the
    /// thing standing between an attacker and the account.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <inheritdoc cref="ClaimTicket.SealedWith"/>
    public string SealedWith(ISecretCipher cipher) =>
        Base64Url.EncodeToString(cipher.Encrypt(JsonSerializer.Serialize(this)));

    /// <inheritdoc cref="ClaimTicket.TryOpen"/>
    public static bool TryOpen(string? value, ISecretCipher cipher, out ReEnrolmentTicket ticket)
    {
        ticket = null!;

        if (value is null || !Base64Url.IsValid(value))
        {
            return false;
        }

        try
        {
            var opened = JsonSerializer.Deserialize<ReEnrolmentTicket>(
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

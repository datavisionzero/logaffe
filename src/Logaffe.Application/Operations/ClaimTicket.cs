using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the installation drew for a claim in progress, carried by the browser
/// between the two requests and sealed so that only the installation can read
/// it.
/// </summary>
/// <remarks>
/// <para>
/// The claim stores nothing until its last step (ADR 0014) and the operator has
/// to see their authenticator secret and their ten backup codes before that
/// step. This is how both stay true: the values live in the browser, and this
/// sealed copy of them is what says the installation drew them rather than the
/// claimant (ADR 0035).
/// </para>
/// <para>
/// It carries the codes as hashes because a hash is what the row will hold and
/// the rows cannot be built yet — a backup code hangs off an operator who does
/// not exist until the claim completes. From the moment the sheet is shown, the
/// operator holds the only copy.
/// </para>
/// </remarks>
/// <param name="WindowOpenedAt">
/// The window this was drawn in. A ticket belongs to one window and is refused
/// against any other, so it cannot be carried across a Host Recovery.
/// </param>
public sealed record ClaimTicket(
    DateTimeOffset WindowOpenedAt,
    string SecondFactorSecret,
    IReadOnlyList<byte[]> BackupCodeHashes)
{
    /// <summary>
    /// Seals the ticket under the key on the host volume and writes it in
    /// base64url, which is what survives a JSON body and a copy-paste.
    /// </summary>
    public string SealedWith(ISecretCipher cipher) =>
        Base64Url.EncodeToString(cipher.Encrypt(JsonSerializer.Serialize(this)));

    /// <summary>
    /// Opens a presented ticket, or answers <c>false</c> for anything that is
    /// not one this installation sealed.
    /// </summary>
    /// <remarks>
    /// Everything that can go wrong here is the same answer: not base64url, not
    /// something the key opens, not the shape that was sealed, or a set of
    /// hashes that is not a set. There is nothing to tell apart — a ticket is
    /// either the one the installation handed out or it is somebody's guess.
    /// </remarks>
    public static bool TryOpen(string? value, ISecretCipher cipher, out ClaimTicket ticket)
    {
        ticket = null!;

        if (value is null || !Base64Url.IsValid(value))
        {
            return false;
        }

        try
        {
            var opened = JsonSerializer.Deserialize<ClaimTicket>(
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
}

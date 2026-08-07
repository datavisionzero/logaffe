using System.Security.Cryptography;
using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether the key on the host volume belongs to the database beside it.
/// </summary>
public enum KeyFit
{
    /// <summary>
    /// The installation holds no sealed secret, so there is nothing the key
    /// could be wrong about. A fresh installation, and the ordinary first start.
    /// </summary>
    NothingSealed,

    /// <summary>The key opens what is stored.</summary>
    Fits,

    /// <summary>
    /// The installation holds sealed secrets and the key opens none of them.
    /// Both stores are here and they are not two halves of one installation.
    /// </summary>
    DoesNotFit,
}

/// <summary>
/// The check behind <c>docs/operations.md</c>'s "neither store is useful without
/// the other".
/// </summary>
/// <remarks>
/// <para>
/// A database restored without its key is an installation whose every token is
/// undecryptable, and the operator otherwise discovers it at the moment they go
/// looking for one. The same thing happens more quietly when a volume is lost
/// and a start writes a fresh key beside a database that is still full: nothing
/// fails until something needs a secret.
/// </para>
/// <para>
/// It asks the strong question rather than the cheap one. "Was the key just
/// written?" would catch the lost volume alone; "does this key open what is
/// stored?" catches that, a restore that brought one half, a swapped volume and
/// a key file replaced by hand, and it is one decryption to ask.
/// </para>
/// <para>
/// A sample rather than one row, because a single unreadable row is a corrupt
/// row and a whole unreadable sample is a wrong key — and only the second is
/// worth refusing to start over.
/// </para>
/// </remarks>
public sealed class CheckTheKeyFits(ISealedSecrets secrets, ITokenCipher cipher)
{
    public const int SampleSize = 3;

    public async Task<KeyFit> ExecuteAsync(CancellationToken cancellationToken)
    {
        var sample = await secrets.SampleAsync(SampleSize, cancellationToken);

        if (sample.Count == 0)
        {
            return KeyFit.NothingSealed;
        }

        foreach (var sealedSecret in sample)
        {
            try
            {
                _ = cipher.Decrypt(sealedSecret);
                return KeyFit.Fits;
            }
            catch (CryptographicException)
            {
                // This one is unreadable. Whether that is the row or the key is
                // what the rest of the sample answers.
            }
        }

        return KeyFit.DoesNotFit;
    }
}

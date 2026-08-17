using Logaffe.Domain.Operators;

namespace Logaffe.Application.Ports;

/// <summary>
/// How this installation was told to guard its claim, which is the one piece of
/// configuration this layer reads.
/// </summary>
/// <remarks>
/// <para>
/// It is handed in rather than fetched, like everything else here: what a
/// compose file says is the composition root's to read, and refusing to start on
/// a secret that is too short happens there, before anything below can be asked
/// to work with one (ADR 0040).
/// </para>
/// <para>
/// It is read on every start while the installation is unclaimed, so changing it
/// is editing the compose file and restarting. On an installation that has an
/// operator it decides nothing at all — there is no re-claim while claimed.
/// </para>
/// </remarks>
/// <param name="Mode">Which of the two guards is in force.</param>
/// <param name="SuppliedSecret">
/// The secret configuration named, or <c>null</c> — which in
/// <see cref="ClaimMode.Secret"/> means the installation draws one for itself and
/// keeps its hash, and in <see cref="ClaimMode.Window"/> means nothing at all.
/// </param>
public sealed record ClaimSettings(ClaimMode Mode, ClaimSecret? SuppliedSecret)
{
    /// <summary>
    /// What an installation runs with when nobody said otherwise: a secret it
    /// draws itself.
    /// </summary>
    public static ClaimSettings Default { get; } = new(ClaimMode.Secret, null);

    /// <summary>
    /// Whether the installation has to draw a secret of its own — which it does
    /// in secret mode, and only while it is holding none.
    /// </summary>
    public bool DrawsItsOwnSecret => Mode is ClaimMode.Secret && SuppliedSecret is null;
}

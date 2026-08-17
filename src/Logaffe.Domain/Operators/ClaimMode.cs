namespace Logaffe.Domain.Operators;

/// <summary>
/// The two ways an installation's claim can be guarded, one of which whoever
/// installs picks before the first start (ADR 0040).
/// </summary>
/// <remarks>
/// It is configuration rather than state: it is read on every start while the
/// installation is unclaimed, so a compose file written wrong is fixed by editing
/// it and restarting, and neither value does anything at all on an installation
/// that already has an operator.
/// </remarks>
public enum ClaimMode
{
    /// <summary>
    /// The default. Nobody can claim the installation without presenting the
    /// <see cref="ClaimSecret"/>, and there is no deadline — the operator claims
    /// when they get to it rather than within minutes of the container coming up,
    /// which is what makes an installation set up by one party and claimed by
    /// another work at all.
    /// </summary>
    Secret,

    /// <summary>
    /// No secret, and anyone who can reach the installation may claim it, for
    /// <see cref="ClaimGuard.WindowDuration"/> after its first run. It is for the
    /// installation where reading a file or a container log is not on offer, and
    /// it is the older of the two rather than the better one.
    /// </summary>
    Window,
}

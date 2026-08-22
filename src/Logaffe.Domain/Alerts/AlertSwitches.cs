namespace Logaffe.Domain.Alerts;

/// <summary>
/// Which of the three conditions this installation has been switched on.
/// </summary>
/// <remarks>
/// <para>
/// Three switches and nothing else. There is no threshold behind any of them, no
/// per-project variation of one, and no schedule — the conditions already learn
/// a project's normal by hour of the day, which is the same idea quiet hours are
/// and done with nothing to enter (ADR 0050).
/// </para>
/// <para>
/// <b>All three are off until the operator turns them on.</b> An installation
/// that has never been asked has nowhere to send anything anyway, and a product
/// that starts notifying on its own the first time a disk gets busy is one whose
/// notifications get muted at the phone.
/// </para>
/// </remarks>
public sealed record AlertSwitches(bool FillingUp, bool GoneQuiet, bool Flooding)
{
    /// <summary>What an installation nobody has asked has switched on.</summary>
    public static readonly AlertSwitches AllOff = new(false, false, false);

    /// <summary>Whether there is anything to evaluate at all.</summary>
    public bool Any => FillingUp || GoneQuiet || Flooding;
}

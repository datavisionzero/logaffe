namespace Logaffe.Domain.Alerts;

/// <summary>
/// Which of the four conditions this installation has been switched on.
/// </summary>
/// <remarks>
/// <para>
/// Four switches and nothing else. There is no threshold behind any of them, no
/// per-project variation of one, and no schedule — the conditions already learn
/// a project's normal by hour of the day, which is the same idea quiet hours are
/// and done with nothing to enter (ADR 0050).
/// </para>
/// <para>
/// <b>All four are off until the operator turns them on.</b> An installation
/// that has never been asked has nowhere to send anything anyway, and a product
/// that starts notifying on its own the first time a disk gets busy is one whose
/// notifications get muted at the phone.
/// </para>
/// <para>
/// <b>Adding one was a decision and a migration</b>, which is the friction
/// ADR 0050 was built with and the thing standing between a closed set and the
/// first expression box.
/// </para>
/// </remarks>
public sealed record AlertSwitches(bool FillingUp, bool GoneQuiet, bool Flooding, bool Failing)
{
    /// <summary>What an installation nobody has asked has switched on.</summary>
    public static readonly AlertSwitches AllOff = new(false, false, false, false);

    /// <summary>Whether there is anything to evaluate at all.</summary>
    public bool Any => FillingUp || GoneQuiet || Flooding || Failing;

    /// <summary>
    /// Whether anything here is asked of a project, which is what decides
    /// whether the hourly pass walks them at all.
    /// </summary>
    public bool AnyProject => GoneQuiet || Flooding || Failing;
}

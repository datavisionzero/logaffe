namespace Logaffe.Domain.Alerts;

/// <summary>
/// Why the store filling up cannot be evaluated, for a condition that is
/// switched on and cannot see.
/// </summary>
/// <remarks>
/// <para>
/// It is an answer rather than a silence on purpose. An operator who thinks a
/// disk is being watched when it is not is worse off than one who was never
/// offered the switch, so the state is legible where the switch is
/// (<c>docs/ui.md</c>) instead of the condition simply never firing.
/// </para>
/// <para>
/// The reasons are separate where the footprint's are not, and it is the same
/// distinction made twice for different readers: a screen choosing a retention
/// window has nothing to do about a missing disk reading, and a screen offering
/// this switch has exactly one thing to do about each of these.
/// </para>
/// </remarks>
public enum Blindness
{
    /// <summary>Nothing is in the way: there is a reading.</summary>
    None = 0,

    /// <summary>
    /// The installation names no machine and no mount, which is every
    /// installation until the operator names one.
    /// </summary>
    NoHostNamed = 1,

    /// <summary>
    /// The machine it names has not reported recently enough to be believed —
    /// including never.
    /// </summary>
    NotReporting = 2,

    /// <summary>
    /// The machine is reporting and the mount it names is not among what
    /// arrives, which is a mount renamed or taken out of that host's collector.
    /// </summary>
    MountAbsent = 3,
}

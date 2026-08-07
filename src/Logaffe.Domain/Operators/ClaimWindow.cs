namespace Logaffe.Domain.Operators;

/// <summary>
/// The half hour during which an installation nobody owns can be claimed over
/// the network.
/// </summary>
/// <remarks>
/// <para>
/// It opens when the installation first runs, lasts thirty minutes, and a
/// restart does not extend it — the deadline belongs to the installation rather
/// than to the process, so nobody gains anything by forcing a restart
/// (<c>docs/setup.md</c>). Host Recovery arms a fresh one, which is what makes
/// this something written as well as read
/// (ADR 0013).
/// </para>
/// <para>
/// There is one of these, in a table holding one row, and it lives in the
/// database rather than on the host volume beside the key (ADR 0034) — so "first
/// run" is exactly the run that created the schema.
/// </para>
/// <para>
/// Thirty minutes is short on purpose, and it is short because the way back is
/// cheap. It is measured against a person walking back to their desk rather than
/// against a scanner's patience: a fresh hostname appears in the public
/// Certificate Transparency logs within seconds of its certificate being issued,
/// so an installation reachable under its own name is discoverable almost
/// immediately.
/// </para>
/// </remarks>
public sealed class ClaimWindow
{
    /// <summary>
    /// Thirty minutes, from <see cref="OpenedAt"/>.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

    private ClaimWindow()
    {
        // EF Core materializes through this; every other route goes through
        // OpenedOnFirstRun.
    }

    private ClaimWindow(Guid id, DateTimeOffset openedAt)
    {
        Id = id;
        OpenedAt = openedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// When the installation last became claimable: its first run, or the last
    /// Host Recovery. It is the whole of what this row holds, and it is not a
    /// secret — the screen an unclaimed installation shows counts down from it.
    /// </summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>
    /// Derived rather than stored, so that it cannot disagree with the instant
    /// it is measured from.
    /// </summary>
    public DateTimeOffset ClosesAt => OpenedAt + Duration;

    public bool IsOpenAt(DateTimeOffset when) => when < ClosesAt;

    /// <summary>
    /// Opens the window the installation gets for having been started for the
    /// first time.
    /// </summary>
    /// <remarks>
    /// Called on every start and written on exactly one of them: what makes a
    /// restart not extend anything is that the row is only inserted when there
    /// is none, and the store is what holds that.
    /// </remarks>
    public static ClaimWindow OpenedOnFirstRun(DateTimeOffset at) =>
        new(Guid.CreateVersion7(), at);

    /// <summary>
    /// Opens a fresh window on an installation that is being handed back, which
    /// is the half of Host Recovery that is not the account going away
    /// (ADR 0013).
    /// </summary>
    /// <remarks>
    /// It moves the instant rather than making a second row. There is one window
    /// per installation and it is the current one; a previous one is not a thing
    /// the product has any use for, and a ticket drawn under it is refused by
    /// naming an instant that is no longer this one (ADR 0035).
    /// </remarks>
    public void ArmAt(DateTimeOffset when) => OpenedAt = when;
}

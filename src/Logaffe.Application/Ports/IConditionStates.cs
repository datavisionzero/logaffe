using Logaffe.Domain.Alerts;

namespace Logaffe.Application.Ports;

/// <summary>
/// What the installation remembers about the conditions that have already
/// fired, so that a restart does not make an event a new one.
/// </summary>
/// <remarks>
/// <para>
/// It is a table for one reason: an installation that restarts hourly must not
/// notify hourly (ADR 0050). Everything else about a condition is derived when
/// it is evaluated — the tally says what a project did, the samples say how full
/// the disk is — and this is the single fact that cannot be, because it is about
/// what was said rather than about what happened.
/// </para>
/// <para>
/// <b>It is not a list of alerts and it is not an inbox.</b> There is one row
/// per subject per condition, holding the last thing said and nothing before it:
/// an alert leaves the installation, it does not accumulate on a screen, there
/// is nothing to acknowledge and nothing to dismiss (<c>docs/ui.md</c>). What
/// the alerts screen shows off these rows is when each condition last fired,
/// which is the only history there is.
/// </para>
/// <para>
/// <b>Rows of a subject that no longer exists are left.</b> There is no foreign
/// key here for the reason the tally has none, and what a deleted project leaves
/// behind is at most two rows nothing can reach — the walk that evaluates is
/// over the projects that exist.
/// </para>
/// </remarks>
public interface IConditionStates
{
    /// <summary>
    /// What is remembered about this condition and this subject, or <c>null</c>
    /// when nothing has ever been said about it.
    /// </summary>
    Task<ConditionState?> FindAsync(
        Guid subjectId, AlertCondition condition, CancellationToken cancellationToken);

    /// <summary>
    /// Every subject and condition that has ever fired, newest first, and
    /// nothing about the ones that have not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is the only history alerting has</b>, and it is one row per subject
    /// per condition rather than a list of what was sent: what it answers is
    /// "when did this last fire", which is what makes "is this thing working?"
    /// answerable without waiting for an incident (<c>docs/alerts.md</c>).
    /// </para>
    /// <para>
    /// Rows that have never fired are left out here rather than handed over with
    /// nothing in them. They exist because a condition cleared before it ever
    /// fired, which is the ordinary shape of an installation nothing has gone
    /// wrong on, and a screen has nothing to say about one.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ConditionState>> ListFiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the state back, whether it is a row already there or the first
    /// thing this subject has ever had said about it.
    /// </summary>
    /// <remarks>
    /// Written on a condition clearing as well as on one firing: the latch
    /// coming down is what lets the next event be said, and a re-arming a
    /// restart lost would leave that event unsaid.
    /// </remarks>
    Task RecordAsync(ConditionState state, CancellationToken cancellationToken);
}

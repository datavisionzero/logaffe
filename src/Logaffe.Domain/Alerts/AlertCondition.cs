namespace Logaffe.Domain.Alerts;

/// <summary>
/// One of the four things an installation says something about unasked.
/// </summary>
/// <remarks>
/// <para>
/// The set is closed and named in the product (ADR 0050): there is no rule for
/// an operator to write, nothing here is attached to a filter — which is the
/// alternative that decision rejected — and a fifth member is a change to that
/// document rather than a value added here. The fourth was, and the decision
/// records what it cost to add.
/// </para>
/// <para>
/// <b>The numbers are written out because they are stored.</b> Each condition
/// keys the row remembering when it last fired for a subject
/// (<see cref="ConditionState"/>), so a member renumbered would hand every
/// installation's history to the wrong condition.
/// </para>
/// </remarks>
public enum AlertCondition
{
    /// <summary>The filesystem the installation's database sits on is filling up.</summary>
    FillingUp = 1,

    /// <summary>
    /// A project has received nothing for far longer than it is ever quiet for.
    /// </summary>
    GoneQuiet = 2,

    /// <summary>
    /// A project's closed hour is far above what that hour of the day normally
    /// holds for it.
    /// </summary>
    Flooding = 3,

    /// <summary>
    /// A project's entries at <c>Error</c> or above are far above what that hour
    /// of the day normally holds for it, on two closed hours in a row.
    /// </summary>
    Failing = 4,
}

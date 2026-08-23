namespace Logaffe.Domain.Alerts;

/// <summary>
/// What a condition decided, and the numbers it decided it on.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries names and numbers and it cannot carry anything else.</b> There
/// is no member here a rendered message, an exception, a property value, a
/// logger name or an instance could be put in — not because nothing puts one
/// there today, but because there is nowhere to put one (ADR 0049). That is what
/// makes the rule a property of what this type can hold rather than a discipline
/// whoever writes the notifier has to remember.
/// </para>
/// <para>
/// The names are the operator's own: a project is called what they called it and
/// a machine likewise. Nothing here came out of an entry, and the path that
/// would let one is not open — every condition runs on the tally and the
/// samples, and there is no route from either to <c>log_entry</c>.
/// </para>
/// <para>
/// <b>The four shapes are the four conditions</b>, and they are separate
/// because their numbers are: "seven hours silent against five tolerated" and
/// "twelve thousand entries against a usual three hundred" are not the same two
/// figures under different names. A fifth shape is a fifth condition, which is a
/// change to ADR 0050.
/// </para>
/// </remarks>
public abstract record Alert
{
    private protected Alert(AlertCondition condition, Guid subjectId, string subjectName)
    {
        Condition = condition;
        SubjectId = subjectId;
        SubjectName = subjectName;
    }

    /// <summary>Which of the four fired.</summary>
    public AlertCondition Condition { get; }

    /// <summary>The project this is about, or the machine for the disk.</summary>
    public Guid SubjectId { get; }

    /// <summary>What the operator calls that project or that machine.</summary>
    public string SubjectName { get; }

    /// <summary>
    /// The filesystem the database sits on crossed a threshold it had not
    /// crossed before.
    /// </summary>
    /// <param name="Threshold">
    /// Which of the two it crossed, so that the alert says whether this is the
    /// first thing worth knowing or the last one.
    /// </param>
    public sealed record StoreFillingUp(
        Guid HostId, string HostName, int Percent, int Threshold)
        : Alert(AlertCondition.FillingUp, HostId, HostName);

    /// <summary>
    /// A project has received nothing for longer than its own fortnight says is
    /// ordinary.
    /// </summary>
    /// <param name="Tolerated">
    /// What it would have taken without a word, which is three times its longest
    /// quiet stretch. It rides along because it is the answer to the question
    /// the alert provokes — why now, and not last night?
    /// </param>
    public sealed record ProjectGoneQuiet(
        Guid ProjectId, string ProjectName, int Hours, int Tolerated)
        : Alert(AlertCondition.GoneQuiet, ProjectId, ProjectName);

    /// <summary>
    /// A project's closed hour is far above what that hour of the day normally
    /// holds for it.
    /// </summary>
    /// <param name="Hour">
    /// The hour it fired on, which is what the link's filters are set to: what
    /// the operator wants at that moment is that hour of that project, not one
    /// line of it.
    /// </param>
    public sealed record ProjectFlooding(
        Guid ProjectId,
        string ProjectName,
        DateTimeOffset Hour,
        long Entries,
        long Baseline)
        : Alert(AlertCondition.Flooding, ProjectId, ProjectName);

    /// <summary>
    /// A project's entries at <c>Error</c> or above are far above what that hour
    /// of the day normally holds for it, and were on the hour before it too.
    /// </summary>
    /// <param name="Hour">
    /// The second of the two hours, which is the one it fired on and the one the
    /// link's filters are set to — narrowed to <c>Error</c> and above, because
    /// that is what the condition counted.
    /// </param>
    /// <param name="Errors">How many that hour held.</param>
    /// <param name="Previous">
    /// How many the hour before it held. It rides along for the reason
    /// <see cref="ProjectGoneQuiet.Tolerated"/> does: it is the answer to the
    /// question the alert provokes — why now, and not an hour ago?
    /// </param>
    public sealed record ProjectFailing(
        Guid ProjectId,
        string ProjectName,
        DateTimeOffset Hour,
        long Errors,
        long Previous,
        long Baseline)
        : Alert(AlertCondition.Failing, ProjectId, ProjectName);
}

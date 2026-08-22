namespace Logaffe.Domain.Alerts;

/// <summary>
/// What the installation remembers about one condition and one subject: the
/// level it fired at and has not fallen back from, and when it last said
/// anything.
/// </summary>
/// <remarks>
/// <para>
/// It is what makes an alert a message about an event rather than a reading
/// taken every hour, and it is stored rather than held in memory for the
/// plainest of reasons: an installation that restarts hourly must not notify
/// hourly (ADR 0050).
/// </para>
/// <para>
/// <b>Two rules, and they answer different repeats.</b> A condition that is
/// still holding says nothing more, however long it holds — one event, still
/// happening, so a condition that holds for a day is one notification and not
/// twenty-four. A condition that clears and holds again is a second event, and
/// that one is held off by <see cref="Alerting.Silence"/> so that something
/// flapping across a threshold does not notify every hour it flaps.
/// </para>
/// <para>
/// <b>Nothing is written when a condition clears</b>, and this is the only place
/// a clearing leaves a mark: the latch comes down so the next event can be said,
/// and no notification goes out and no record of one is kept. There is no
/// "resolved" — the operator looks at the screen either way, and a message about
/// something that has stopped needing a person is a message read at three in the
/// morning for nothing.
/// </para>
/// </remarks>
public sealed class ConditionState
{
    private ConditionState()
    {
        // EF Core materializes through this; every other route goes through For.
    }

    /// <summary>
    /// What the condition is about: the project for the two that judge a
    /// project, and the machine the installation sits on for the one that reads
    /// its disk.
    /// </summary>
    /// <remarks>
    /// One column rather than two nullable ones, because there is one question
    /// asked of this table — what is remembered about this condition and this
    /// subject — and a subject is whichever of the two the condition is named
    /// for. There is no foreign key to either, for the reason the tally has
    /// none: what a deleted project or a deleted host leaves is two rows nothing
    /// can reach.
    /// </remarks>
    public required Guid SubjectId { get; init; }

    public required AlertCondition Condition { get; init; }

    /// <summary>
    /// The level this fired at and has not fallen back from, or
    /// <see cref="Alerting.Clear"/> for a condition that is not holding.
    /// </summary>
    public int Latched { get; private set; }

    /// <summary>
    /// The level of the last alert that went out, which stays where it is when
    /// the condition clears. It is what lets a disk going from 85 to 95 say the
    /// second thing at once while a disk flapping across 85 says nothing.
    /// </summary>
    public int NotifiedLevel { get; private set; }

    /// <summary>
    /// When the last alert went out, or <c>null</c> for a condition that has
    /// never fired. It is the only history alerting has, and it is what makes
    /// "is this thing working?" answerable without waiting for an incident.
    /// </summary>
    public DateTimeOffset? NotifiedAt { get; private set; }

    /// <summary>A subject nothing has been said about yet.</summary>
    public static ConditionState For(Guid subjectId, AlertCondition condition) =>
        new() { SubjectId = subjectId, Condition = condition };

    /// <summary>
    /// Takes the latch down to what the condition is doing now, and answers
    /// whether that changed anything.
    /// </summary>
    /// <remarks>
    /// Called on every evaluation, including the ones where nothing holds. A
    /// change here is worth a write on its own: a re-arming that a restart lost
    /// would leave the next real event unsaid.
    /// </remarks>
    public bool Holds(int level)
    {
        if (level >= Latched)
        {
            return false;
        }

        Latched = level;

        return true;
    }

    /// <summary>
    /// Whether an alert at <paramref name="level"/> is due, given what has
    /// already been said. <see cref="Holds"/> goes first, so that a condition
    /// that has fallen back has already been armed again.
    /// </summary>
    public bool Fires(int level, DateTimeOffset now) =>
        level > Latched
        && (level > NotifiedLevel
            || NotifiedAt is null
            || now - NotifiedAt.Value >= Alerting.Silence);

    /// <summary>Writes down that one went out.</summary>
    public void Fired(int level, DateTimeOffset at)
    {
        Latched = level;
        NotifiedLevel = level;
        NotifiedAt = at;
    }
}

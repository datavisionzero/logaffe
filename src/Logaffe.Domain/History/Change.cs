using Logaffe.Domain.Identities;

namespace Logaffe.Domain.History;

/// <summary>
/// What a change was made to. It is a closed set for the reason the alert
/// conditions are: a history that could point at anything is a history nobody
/// can read as a list.
/// </summary>
public enum Subject
{
    Project,
    Group,
    Host,
    IngestToken,
    HostToken,
    AgentToken,
    User,
    ProjectAccess,

    /// <summary>
    /// The installation itself: the sample window, the alert switches, the
    /// notifier.
    /// </summary>
    Installation,
}

/// <summary>
/// What was done. Written out rather than derived from whether a field is
/// present, because <em>deleted</em> and <em>set the retention window to null</em>
/// are not the same sentence and a reader should not have to work out which one
/// they are looking at.
/// </summary>
public enum Act
{
    Created,
    Renamed,
    Changed,
    Removed,
    Issued,
    Revoked,
    Invited,
    Deactivated,
    Reactivated,
    Granted,
    Withdrawn,
}

/// <summary>
/// One row of the history: who, when, what they did to which thing, and — when
/// it was a setting — which field, from what to what
/// (<c>CONTEXT.md</c>, Change).
/// </summary>
/// <remarks>
/// <para>
/// <b>Written by the installation, never edited and never deleted.</b> There is
/// no act anywhere that changes one of these; what removes them is Host Recovery
/// removing the identity they point at, and the subject being deleted taking its
/// own rows with it.
/// </para>
/// <para>
/// <b>Log entries are never in here.</b> They are written once and never
/// changed, so a history over them would have nothing to record. What retention
/// removes is an act of the retention pass and stands as one, not as a change to
/// an entry.
/// </para>
/// <para>
/// <b>It says whether <em>who</em> was a person or an agent.</b> That is not
/// cosmetic here: an agent acts with its owner's authority
/// (ADR 0052), so a row naming the owner and not saying that an agent was
/// holding the keyboard would be true and misleading at once.
/// </para>
/// <para>
/// <b>The subject's name is copied in.</b> A project that has since been deleted
/// or renamed still reads as what it was called when it happened — the whole
/// point of the question *who deleted project X* is that X is not there to be
/// looked up.
/// </para>
/// </remarks>
public sealed class Change
{
    /// <summary>What a field name, a value or a name may be in a row.</summary>
    public const int TextMaxLength = 200;

    private Change()
    {
        // EF Core materializes through this; every other route goes through Of.
    }

    private Change(
        Guid actorId,
        IdentityKind actorKind,
        string actorName,
        DateTimeOffset at,
        Subject subject,
        Guid? subjectId,
        string subjectName,
        Act act,
        string? field,
        string? from,
        string? to)
    {
        ActorId = actorId;
        ActorKind = actorKind;
        ActorName = actorName;
        At = at;
        Subject = subject;
        SubjectId = subjectId;
        SubjectName = subjectName;
        Act = act;
        Field = field;
        From = from;
        To = to;
    }

    /// <summary>Assigned by the database, in the order the rows were written.</summary>
    public long Id { get; private init; }

    /// <summary>The identity that did it, which is a user or an agent.</summary>
    public Guid ActorId { get; private init; }

    /// <inheritdoc cref="Change"/>
    public IdentityKind ActorKind { get; private init; }

    /// <summary>
    /// What they were called at the time, copied in for the reason the subject's
    /// name is: a row has to read as a sentence long after the rows it points at
    /// have moved on.
    /// </summary>
    public string ActorName { get; private init; } = null!;

    public DateTimeOffset At { get; private init; }

    public Subject Subject { get; private init; }

    /// <summary>
    /// Which one, and <c>null</c> for the installation itself — there is one of
    /// those and nothing to name.
    /// </summary>
    public Guid? SubjectId { get; private init; }

    /// <inheritdoc cref="Change"/>
    public string SubjectName { get; private init; } = null!;

    public Act Act { get; private init; }

    /// <summary>
    /// Which setting moved, on an <see cref="Act.Changed"/>, and <c>null</c> on
    /// everything else.
    /// </summary>
    public string? Field { get; private init; }

    /// <summary>
    /// What it was and what it became, as the text a reader sees. A secret never
    /// appears here in either column — a token is <see cref="Act.Issued"/> and
    /// <see cref="Act.Revoked"/>, and what it was is not part of the record.
    /// </summary>
    public string? From { get; private init; }

    /// <inheritdoc cref="From"/>
    public string? To { get; private init; }

    /// <summary>
    /// One row, from whoever did it.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A field was given for an act that is not a change, or withheld from one
    /// that is. That is a row nobody could read, and it is refused here rather
    /// than stored.
    /// </exception>
    public static Change Of(
        Identity actor,
        DateTimeOffset at,
        Subject subject,
        Guid? subjectId,
        string subjectName,
        Act act,
        string? field = null,
        string? from = null,
        string? to = null)
    {
        if (act is Act.Changed == field is null)
        {
            throw new ArgumentException(
                "A change names the field that moved, and nothing else does.", nameof(field));
        }

        return new Change(
            actor.Id,
            actor.Kind,
            Cut(actor.Name)!,
            at,
            subject,
            subjectId,
            Cut(subjectName)!,
            act,
            Cut(field),
            Cut(from),
            Cut(to));
    }

    /// <summary>
    /// What fits in a column. A value longer than this is a value nobody reads
    /// out of a list anyway, and the alternative is a history that can be made
    /// arbitrarily large by naming things at length.
    /// </summary>
    private static string? Cut(string? text) =>
        text is null || text.Length <= TextMaxLength ? text : text[..TextMaxLength];
}

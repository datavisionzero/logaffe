using Logaffe.Application.Ports;
using Logaffe.Domain.History;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// Who is doing this, for the length of one request.
/// </summary>
/// <remarks>
/// <para>
/// It is filled in by the two doors — the session's and the agent's — and read
/// by <see cref="RecordAChange"/>. That is what keeps twenty-odd acts from each
/// carrying an actor through their own signature: the actor is a property of the
/// request rather than an argument of the act, and an act that records a change
/// says so by asking for the recorder.
/// </para>
/// <para>
/// <b>Not every scope has one.</b> The retention sweep, the alert pass and the
/// ingest path run with nobody behind them — and none of them changes
/// configuration, which is why none of them records.
/// </para>
/// </remarks>
public sealed class TheActor
{
    private Identity? who;

    /// <summary>The identity behind this request, which is a user or an agent.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing admitted this scope. An act that records reached a path that has
    /// no actor, which is a wiring mistake rather than a state.
    /// </exception>
    public Identity Who =>
        who ?? throw new InvalidOperationException("Nothing admitted this request.");

    /// <summary>Whether there is one, which the recorder asks before it writes.</summary>
    public bool IsKnown => who is not null;

    public void Admitted(Identity actor) => who = actor;
}

/// <summary>
/// Writing down what somebody changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recorded after the change, and a failure to record does not undo it.</b>
/// The alternative is an installation that refuses to rename a project because
/// it could not write a line about it, which is the wrong way round: the history
/// exists to answer questions afterwards, not to be a second thing that has to
/// work for the product to work.
/// </para>
/// <para>
/// <b>The subject's name is read before the act where the act destroys it.</b>
/// *Who deleted project X* is one of the two questions this exists to answer,
/// and after the delete there is nothing left to call it.
/// </para>
/// </remarks>
public sealed class RecordAChange(IHistory history, TheActor actor, TimeProvider clock)
{
    public Task ExecuteAsync(
        Subject subject,
        Guid? subjectId,
        string subjectName,
        Act act,
        CancellationToken cancellationToken,
        string? field = null,
        string? from = null,
        string? to = null) =>
        // Nothing behind the request means a path that has no actor, which on
        // one that records is a wiring mistake — and one that must not cost the
        // change itself.
        !actor.IsKnown
            ? Task.CompletedTask
            : history.RecordAsync(
                Change.Of(
                    actor.Who,
                    clock.GetUtcNow(),
                    subject,
                    subjectId,
                    subjectName,
                    act,
                    field,
                    from,
                    to),
                cancellationToken);
}

/// <summary>
/// The history as it is read: newest first, a page at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an administrator's.</b> The rows say what somebody did to the
/// installation's configuration, which is the role's own subject — and unlike a
/// project's entries it is not narrowed by a reach, because a change to a
/// project somebody cannot open is still a change to this installation and the
/// row carries a name rather than anything the project holds
/// (ADR 0055).
/// </para>
/// <para>
/// One page is a hundred rows. It resumes on the row's own id, which is the
/// order they were written in, so there is nothing to break a tie on.
/// </para>
/// </remarks>
public sealed class ReadTheHistory(IHistory history)
{
    /// <summary>How many rows one page carries.</summary>
    public const int PageSize = 100;

    public Task<IReadOnlyList<Change>> ExecuteAsync(
        long? before, CancellationToken cancellationToken) =>
        history.ListAsync(before, PageSize, cancellationToken);
}

using Logaffe.Domain.Queries;

namespace Logaffe.Application.Operations;

/// <summary>
/// What a read answers with: the answer, or what to narrow because it ran out of
/// its five seconds.
/// </summary>
/// <remarks>
/// <para>
/// The expiry is an outcome rather than an exception on the way out of this
/// layer, because it is an ordinary thing for a caller to be told and an
/// ordinary thing for a caller to act on — the operator adjusts a filter and
/// tries again, and the agent is handed the same fact as data (ADR 0012). An
/// exception here would make both adapters catch one to say something the use
/// case already knows how to say.
/// </para>
/// <para>
/// <b>An expired read has an answer to what to do about it, always.</b>
/// <see cref="ReadLimit.WhatToNarrow"/> never comes back empty, which is what
/// makes <see cref="Expired"/> readable off the list.
/// </para>
/// </remarks>
/// <param name="Answer">What was read, or <c>null</c> when nothing was.</param>
/// <param name="Narrow">
/// The adjustments that would make it finish, in the order to try them, and
/// empty for a read that did.
/// </param>
public sealed record Read<TAnswer>(TAnswer? Answer, IReadOnlyList<Narrowing> Narrow)
    where TAnswer : class
{
    /// <summary>A read that finished.</summary>
    public static Read<TAnswer> Of(TAnswer answer) => new(answer, []);

    /// <summary>
    /// A read that did not, and what to change about
    /// <paramref name="filters"/> so that the next one does.
    /// </summary>
    public static Read<TAnswer> RanOut(EntryFilters filters) =>
        new(null, ReadLimit.WhatToNarrow(filters));

    /// <summary>Whether the five seconds were what ended it.</summary>
    public bool Expired => Answer is null;
}

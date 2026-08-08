namespace Logaffe.Domain.Queries;

/// <summary>
/// What a count is broken down by, when it is broken down at all.
/// </summary>
/// <remarks>
/// <para>
/// The four are the columns a filter already exists for, and that is the whole
/// rule: a grouping is only worth anything if every row of it narrows to itself.
/// A count grouped by logger name is a list an operator clicks a row of to
/// become the filtered page for that logger, which is the closest thing this
/// product has to a facet — computed because somebody asked for it rather than
/// maintained because a screen wanted it (<c>docs/ui.md</c>).
/// </para>
/// <para>
/// The trace is not among them. Grouping a project by trace would produce a row
/// per request, which is not a summary of anything.
/// </para>
/// </remarks>
public enum Grouping
{
    /// <summary>One number for the whole filter set, which is the plain count.</summary>
    None = 0,

    /// <summary>
    /// By severity — the breakdown that answers <i>were there critical errors</i>
    /// without returning the entries, which is the question
    /// <c>VISION.md</c> puts an agent in front of.
    /// </summary>
    Level,

    /// <summary>By the promoted <c>SourceContext</c>: which part of the application is noisy.</summary>
    LoggerName,

    /// <summary>By the sender copy: whether one instance is the one having trouble.</summary>
    Instance,

    /// <summary>
    /// By a bucket of event time, which is the breakdown that answers
    /// <i>when</i>. It is the one grouping that needs a second value —
    /// <see cref="TimeBucket"/> — because a bucket without a size is not a
    /// grouping.
    /// </summary>
    Time,
}

/// <summary>
/// The size of the bucket a time-grouped count falls into.
/// </summary>
/// <remarks>
/// <para>
/// Three, and no arbitrary interval. They span the retention ceiling sensibly —
/// a minute over an incident, an hour over a day, a day over the ninety ADR 0020
/// permits — and each of them keeps the number of rows a count answers with in
/// the range a person reads at a glance. An arbitrary interval buys the
/// intervals between these and costs an answer to what a fortnight-long bucket
/// starting at 03:17 is aligned to.
/// </para>
/// <para>
/// Buckets are aligned to the clock and not to the range asked for, so the same
/// entry falls in the same bucket whatever window it is counted in — which is
/// what makes two counts of overlapping ranges comparable.
/// </para>
/// </remarks>
public enum TimeBucket
{
    Minute = 0,
    Hour,
    Day,
}

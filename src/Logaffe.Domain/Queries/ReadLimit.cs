namespace Logaffe.Domain.Queries;

/// <summary>
/// The five seconds every read on this surface gets, and what to say when one
/// meets them.
/// </summary>
/// <remarks>
/// <para>
/// One value for every kind of read, the same in every installation, with no
/// setting that raises it
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0026-a-read-has-five-seconds.md">ADR 0026</see>).
/// The number comes from the live tail rather than from caution: a read that
/// takes longer than the interval which refreshes the view has already stopped
/// being an interface. Nothing measured at ten million entries came near it, so
/// it bounds the queries nobody anticipated.
/// </para>
/// <para>
/// It binds this surface and nothing else. The retention sweep legitimately runs
/// for minutes, and it is not a read.
/// </para>
/// </remarks>
public static class ReadLimit
{
    /// <summary>How long any one read may take.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// What the caller should change, given the filters the read that expired
    /// was run with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reporting that a statement timed out names the mechanism and not the
    /// remedy.</b> This is the remedy, and it is computed from the query rather
    /// than written into a screen, because the agent meets the same limit and
    /// has to be told the same thing — as data and never as prose (ADR 0012).
    /// </para>
    /// <para>
    /// The order is what to try first. The time range comes before everything
    /// because it is the narrowing that helps most and the one a count has;
    /// after it, the filter that is unindexed on purpose.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Narrowing> WhatToNarrow(EntryFilters filters)
    {
        var narrowings = new List<Narrowing>(3);

        // First, always, and whichever way round it is unset: the range is what
        // bounds the rows a read has to visit at all, and a count has nothing
        // else that bounds it.
        if (filters.From is null || filters.Until is null)
        {
            narrowings.Add(Narrowing.TimeRange);
        }

        // The one filter that is served by no index, deliberately (ADR 0028). It
        // is a second act rather than a tax on every search, and taking it off is
        // the adjustment that makes a read finish when it is the one running.
        if (filters.ExceptionText is not null)
        {
            narrowings.Add(Narrowing.ExceptionText);
        }

        // Last resort, and the only one that is not a filter: a range that is
        // already set can be set smaller. Offered when nothing above applies,
        // so that a read that expired never comes back with nothing to do about
        // it.
        if (narrowings.Count == 0)
        {
            narrowings.Add(Narrowing.SmallerTimeRange);
        }

        return narrowings;
    }
}

/// <summary>
/// One adjustment that would make an expired read finish.
/// </summary>
/// <remarks>
/// These are the terms <c>docs/ui.md</c> puts the message in — <i>narrow the
/// time range, take the exception filter off, count a day instead of the
/// project</i> — and they are named values rather than sentences so that the
/// screen writes the sentence and the agent gets the fact.
/// </remarks>
public enum Narrowing
{
    /// <summary>Set a time range, where the read ran with an open end.</summary>
    TimeRange = 0,

    /// <summary>Make a range that is already set a shorter one.</summary>
    SmallerTimeRange,

    /// <summary>Take off the exception filter, which no index serves.</summary>
    ExceptionText,
}

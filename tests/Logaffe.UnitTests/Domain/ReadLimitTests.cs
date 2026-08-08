using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Domain;

public sealed class ReadLimitTests
{
    private static readonly DateTimeOffset Ten = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_expired_read_always_has_something_to_say()
    {
        // ADR 0026: reporting that a statement timed out names the mechanism and
        // not the remedy. Whatever was set, there is a remedy — including for
        // the read that already narrowed every way it could.
        EntryFilters[] each =
        [
            EntryFilters.None,
            new() { From = Ten },
            new() { From = Ten, Until = Ten.AddHours(1) },
            new() { From = Ten, Until = Ten.AddHours(1), ExceptionText = SearchText.Create("ioexception") },
        ];

        Assert.All(each, filters => Assert.NotEmpty(ReadLimit.WhatToNarrow(filters)));
    }

    [Fact]
    public void A_read_with_an_open_ended_range_is_told_to_set_one()
    {
        // First, always: the range is what bounds the rows a read visits at all,
        // and a count has nothing else that bounds it.
        Assert.Equal([Narrowing.TimeRange], ReadLimit.WhatToNarrow(EntryFilters.None));
        Assert.Equal([Narrowing.TimeRange], ReadLimit.WhatToNarrow(new EntryFilters { From = Ten }));
    }

    [Fact]
    public void The_unindexed_filter_is_named_after_the_range()
    {
        var filters = new EntryFilters
        {
            ExceptionText = SearchText.Create("nullreference"),
        };

        // The range first because it helps most, then the filter that is served
        // by no index on purpose (ADR 0028).
        Assert.Equal(
            [Narrowing.TimeRange, Narrowing.ExceptionText],
            ReadLimit.WhatToNarrow(filters));
    }

    [Fact]
    public void A_read_that_narrowed_everything_is_told_to_narrow_it_further()
    {
        var filters = new EntryFilters
        {
            From = Ten,
            Until = Ten.AddHours(1),
            MinimumLevel = Logaffe.Domain.Entries.Level.Warning,
            Search = SearchText.Create("timeout"),
        };

        // Nothing to take off, so the range that is already set is the thing to
        // make smaller. A read that expired never comes back with nothing to do
        // about it.
        Assert.Equal([Narrowing.SmallerTimeRange], ReadLimit.WhatToNarrow(filters));
    }
}

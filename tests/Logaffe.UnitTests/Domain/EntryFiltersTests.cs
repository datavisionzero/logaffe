using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Domain;

public sealed class EntryFiltersTests
{
    private static readonly DateTimeOffset Ten = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_filter_set_that_narrows_nothing_is_a_projects_whole_page()
    {
        Assert.False(EntryFilters.None.Narrows);
        Assert.True(EntryFilters.None.HasARange);
    }

    [Fact]
    public void An_open_ended_range_is_a_range()
    {
        // Everything since ten, and everything until ten. Both are questions an
        // operator asks, and neither is malformed for missing its other end.
        Assert.True(new EntryFilters { From = Ten }.HasARange);
        Assert.True(new EntryFilters { Until = Ten }.HasARange);
    }

    [Fact]
    public void A_range_that_ends_where_it_starts_is_not_one()
    {
        // The end is exclusive, so this asks for a period of no length. It is a
        // malformed question rather than an empty answer: a caller told "no
        // entries" would go looking for a delivery problem.
        Assert.False(new EntryFilters { From = Ten, Until = Ten }.HasARange);
        Assert.False(new EntryFilters { From = Ten, Until = Ten.AddMinutes(-5) }.HasARange);
        Assert.True(new EntryFilters { From = Ten, Until = Ten.AddMinutes(5) }.HasARange);
    }

    [Fact]
    public void A_trace_is_the_length_a_trace_id_is()
    {
        // Self-validating, as it is on the entry: a filter carrying something
        // that is not a trace id would ask the index a question it cannot be
        // asked.
        var traceId = new byte[LogEntry.TraceIdLength];

        Assert.Equal(traceId, new EntryFilters { TraceId = traceId }.TraceId);
        Assert.Throws<ArgumentException>(() => new EntryFilters { TraceId = new byte[15] });
    }

    [Fact]
    public void Every_filter_on_its_own_narrows()
    {
        // The distinction the empty project rests on: docs/ui.md shows the
        // delivery snippet for a project holding nothing and the filters
        // responsible for a set that matched nothing, and showing one where the
        // other belongs is how an operator concludes their integration is broken
        // while the truth is that the range is set to yesterday.
        EntryFilters[] each =
        [
            new() { From = Ten },
            new() { Until = Ten },
            new() { MinimumLevel = Level.Warning },
            new() { Instance = "api-7c4f" },
            new() { LoggerName = "Orders.Api" },
            new() { TraceId = new byte[LogEntry.TraceIdLength] },
            new() { Search = SearchText.Create("timeout") },
            new() { ExceptionText = SearchText.Create("nullreference") },
        ];

        Assert.All(each, filters => Assert.True(filters.Narrows));
    }
}

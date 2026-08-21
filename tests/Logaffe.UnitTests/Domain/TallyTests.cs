using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Domain;

/// <summary>
/// What is true of a tally row and of the hour it is keyed on.
/// </summary>
/// <remarks>
/// The hour is the second half of the key, so where an hour starts is a rule
/// rather than a convenience: two writers disagreeing about it would put one
/// hour of one project into two rows, and the two things that read this compare
/// an hour against the same hour of other days.
/// </remarks>
public sealed class TallyTests
{
    private static readonly Guid Project = Guid.CreateVersion7();

    private static readonly DateTimeOffset Hour = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_hour_is_the_moment_with_its_minutes_and_seconds_taken_off()
    {
        Assert.Equal(Hour, Tallying.HourOf(new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero)));
        Assert.Equal(Hour, Tallying.HourOf(new(2026, 8, 21, 14, 59, 59, TimeSpan.Zero)));
        Assert.Equal(Hour, Tallying.HourOf(new(2026, 8, 21, 14, 30, 0, 500, TimeSpan.Zero)));
    }

    [Fact]
    public void An_hour_is_at_UTC_whatever_offset_the_moment_came_in()
    {
        // Two in the afternoon in Berlin is midday here, and the tally of a
        // project is not kept in whatever offset a caller happened to hold.
        var berlin = new DateTimeOffset(2026, 8, 21, 14, 30, 0, TimeSpan.FromHours(2));

        var hour = Tallying.HourOf(berlin);

        Assert.Equal(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero), hour);
        Assert.Equal(TimeSpan.Zero, hour.Offset);
    }

    [Fact]
    public void A_new_hour_has_counted_nothing()
    {
        var tally = Tally.For(Project, Hour);

        Assert.Equal(0, tally.Entries);
        Assert.Equal(0, tally.AtErrorOrAbove);
    }

    [Fact]
    public void Adding_accumulates_rather_than_replacing()
    {
        // The whole of what a flush is: it carries what arrived since the last
        // one, so a row that took the second increment as its total would hold
        // its final minute rather than its hour.
        var tally = Tally.For(Project, Hour);

        tally.Add(40, 3);
        tally.Add(2, 1);

        Assert.Equal(42, tally.Entries);
        Assert.Equal(4, tally.AtErrorOrAbove);
    }

    [Fact]
    public void An_hour_that_is_not_a_whole_hour_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Tally.For(Project, new DateTimeOffset(2026, 8, 21, 14, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_whole_hour_at_the_wrong_offset_is_refused()
    {
        // Two o'clock somewhere else is not two o'clock here, and a key that
        // accepted it would hold the same hour twice.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Tally.For(Project, new DateTimeOffset(2026, 8, 21, 14, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void A_negative_amount_is_refused()
    {
        // The counter this comes from only ever goes up, and a tally that could
        // be reduced would be a correction — which is the thing nothing here
        // does.
        var tally = Tally.For(Project, Hour);

        Assert.Throws<ArgumentOutOfRangeException>(() => tally.Add(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => tally.Add(0, -1));
    }

    [Fact]
    public void The_period_outlives_the_longest_window_a_project_can_have()
    {
        // A project keeping entries for a week still needs a fortnight of
        // history to have a baseline, and one at the ceiling needs history
        // covering everything it holds.
        Assert.True(Tallying.RetentionDays > RetentionWindow.MaximumDays);
    }
}

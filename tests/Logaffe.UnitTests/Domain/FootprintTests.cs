using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Storage;

namespace Logaffe.UnitTests.Domain;

/// <summary>
/// The arithmetic the ceiling handed its job to (ADR 0048). What is tested here
/// is that the numbers are the product's own — <c>docs/storage.md</c>'s per-entry
/// cost and the row costs of the sample tables — and that the shape of the sum
/// is rate times days.
/// </summary>
public sealed class FootprintTests
{
    [Fact]
    public void An_entry_costs_what_the_storage_document_measured() =>
        // 1.2 KiB, the number that document names as the one to multiply.
        Assert.Equal(1229, Footprint.BytesPerEntry);

    [Fact]
    public void A_window_is_the_rate_times_the_days()
    {
        // Five thousand entries a day over a fortnight, kept for ninety days.
        var footprint = Footprint.OfEntries(
            5_000 * 14, TimeSpan.FromDays(14), RetentionWindow.OfDays(90));

        Assert.Equal(5_000L * 90 * Footprint.BytesPerEntry, footprint);
    }

    [Fact]
    public void Four_times_the_days_is_four_times_the_bytes()
    {
        var quarter = Footprint.OfEntries(
            10_000, TimeSpan.FromDays(14), RetentionWindow.OfDays(90));
        var year = Footprint.OfEntries(
            10_000, TimeSpan.FromDays(14), RetentionWindow.OfDays(360));

        // To the rounding of the last byte, which is where a rate that is not a
        // whole number of entries a day ends up.
        Assert.Equal(4, year / (double)quarter, 6);
    }

    [Fact]
    public void The_table_in_the_decision_is_what_comes_back()
    {
        // ADR 0048 says a project at five thousand entries a day costs 0.5 GiB
        // at ninety days and 2.1 GiB at a year. This is that table.
        var quarter = Footprint.OfEntries(
            5_000, TimeSpan.FromDays(1), RetentionWindow.OfDays(90));
        var year = Footprint.OfEntries(
            5_000, TimeSpan.FromDays(1), RetentionWindow.OfDays(365));

        Assert.Equal(0.5, Math.Round(quarter / (double)(1024 * 1024 * 1024), 1));
        Assert.Equal(2.1, Math.Round(year / (double)(1024 * 1024 * 1024), 1));
    }

    [Fact]
    public void A_project_that_received_nothing_costs_nothing() =>
        // Nought is an answer and not an absence: the project delivered nothing
        // for a fortnight, so the window it is given holds nothing. What is
        // absent instead is the project with no fortnight behind it, and that is
        // decided a layer up.
        Assert.Equal(0, Footprint.OfEntries(0, TimeSpan.FromDays(14), RetentionWindow.OfDays(90)));

    [Fact]
    public void A_rate_needs_a_period_with_length() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Footprint.OfEntries(10, TimeSpan.Zero, RetentionWindow.OfDays(30)));

    [Fact]
    public void A_count_of_entries_is_not_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Footprint.OfEntries(-1, TimeSpan.FromDays(14), RetentionWindow.OfDays(30)));

    [Fact]
    public void Samples_are_a_row_a_minute_per_host_and_per_filesystem()
    {
        var aDay = (long)(TimeSpan.FromDays(1) / Sampling.Interval);

        var footprint = Footprint.OfSamples(5, 15, RetentionWindow.OfDays(90));

        Assert.Equal(
            90 * aDay * ((5 * Footprint.BytesPerSample) + (15 * Footprint.BytesPerFilesystemReading)),
            footprint);
    }

    [Fact]
    public void The_sample_tables_come_to_what_the_storage_document_says()
    {
        // Five hosts watching three filesystems each, at ninety days: about
        // 250 MiB, which is the figure that document arrives at by the same
        // arithmetic.
        var footprint = Footprint.OfSamples(5, 15, RetentionWindow.OfDays(90));

        var mib = footprint / (double)(1024 * 1024);

        Assert.InRange(mib, 245, 255);
    }

    [Fact]
    public void An_installation_nothing_reports_to_costs_nothing_in_samples() =>
        Assert.Equal(0, Footprint.OfSamples(0, 0, RetentionWindow.OfDays(365)));
}

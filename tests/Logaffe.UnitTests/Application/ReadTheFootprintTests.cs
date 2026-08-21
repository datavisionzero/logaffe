using Logaffe.Application.Operations;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Storage;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The three numbers a window is chosen against (ADR 0048). What is tested here
/// is the part the ceiling used to do: that the arithmetic is the project's own
/// rate, that it says nothing rather than guessing when there is nothing to work
/// from, and that it refuses nothing whatever it comes to.
/// </summary>
public sealed class ReadTheFootprintTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 30, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly InMemoryHosts _hosts = new();
    private readonly RecordingTallies _tallies = new();
    private readonly StubSampleReader _samples = new();
    private readonly InMemoryInstallation _installation = new();
    private readonly StubStoreFootprint _store = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task What_the_installation_holds_is_the_stores_own_number()
    {
        var project = Holding();
        _store.Held = 4711;

        var footprint = await OfProject(project.Id, 90);

        Assert.Equal(4711, footprint!.Held);
        // One call, and it is the whole database rather than a table: the
        // operator is looking at this because of a disk.
        Assert.Equal(1, _store.Reads);
    }

    [Fact]
    public async Task A_window_costs_the_projects_own_rate_times_the_days()
    {
        var project = Holding();

        // A thousand entries in each of the fourteen closed days behind now.
        await Delivering(project.Id, anHour: 50, days: 14);

        var footprint = await OfProject(project.Id, 365);

        Assert.Equal(50L * 24 * 365 * Footprint.BytesPerEntry, footprint!.Implied);
    }

    [Fact]
    public async Task A_year_costs_four_times_a_quarter()
    {
        var project = Holding();
        await Delivering(project.Id, anHour: 50, days: 14);

        var quarter = await OfProject(project.Id, 90);
        var year = await OfProject(project.Id, 360);

        // The whole of what the ceiling being wrong was about: the same project
        // costs what the days say, and nothing about the arithmetic changes at
        // ninety.
        Assert.Equal(4, year!.Implied!.Value / (double)quarter!.Implied!.Value, 6);
    }

    [Fact]
    public async Task A_project_younger_than_a_fortnight_says_so_rather_than_extrapolating()
    {
        var project = Holding();

        // Two busy days. Multiplied up by a year that is fifty gibibytes, and
        // it would be a number the operator has no reason to believe.
        await Delivering(project.Id, anHour: 5_000, days: 2);

        var footprint = await OfProject(project.Id, 365);

        Assert.Null(footprint!.Implied);
    }

    [Fact]
    public async Task A_project_that_has_never_delivered_says_nothing_either()
    {
        var project = Holding();

        Assert.Null((await OfProject(project.Id, 30))!.Implied);
    }

    [Fact]
    public async Task A_fortnight_of_silence_costs_nothing_and_is_not_an_absence()
    {
        var project = Holding();

        // One busy day, a fortnight ago and outside the window the rate is read
        // over. The row is what says this project has a history; what it
        // delivered since is nothing, and nothing is a rate. This is the project
        // whose window is free, not the project nobody can say anything about.
        await Delivering(project.Id, anHour: 4, days: 1, endingDaysAgo: 14);

        var footprint = await OfProject(project.Id, 365);

        Assert.Equal(0, footprint!.Implied);
    }

    [Fact]
    public async Task The_hour_in_progress_is_left_out()
    {
        var project = Holding();
        await Delivering(project.Id, anHour: 50, days: 14);

        var before = await OfProject(project.Id, 90);

        // A burst arriving in the hour nobody has closed yet. It is a fraction
        // of an hour that would be divided as a whole one, so it waits.
        await Flush(project.Id, Tallying.HourOf(Now), 500_000);

        Assert.Equal(before!.Implied, (await OfProject(project.Id, 90))!.Implied);
    }

    [Fact]
    public async Task A_project_that_is_gone_is_no_answer_at_all() =>
        Assert.Null(await OfProject(Guid.CreateVersion7(), 30));

    [Fact]
    public async Task An_installation_on_no_host_shows_the_first_two_numbers()
    {
        var project = Holding();
        await Delivering(project.Id, anHour: 10, days: 14);

        var footprint = await OfProject(project.Id, 30);

        // The ordinary installation: it names no machine, so there is no disk to
        // read, and the field says the two things it does know.
        Assert.Null(footprint!.Disk);
        Assert.NotEqual(0, footprint.Held);
        Assert.NotNull(footprint.Implied);
    }

    [Fact]
    public async Task The_disk_is_the_newest_reading_of_the_mount_that_was_named()
    {
        var project = Holding();
        var host = _hosts.Holding("db", Now.AddDays(-30));

        await NamingHost(host.Id, "/var/lib/postgresql");
        _samples.Reporting(host.Id, Now.AddMinutes(-1),
        [
            StubSampleReader.Reading(host.Id, Now.AddMinutes(-1), "/", 5, 100),
            StubSampleReader.Reading(
                host.Id, Now.AddMinutes(-1), "/var/lib/postgresql", 300, 1_000),
        ]);

        var footprint = await OfProject(project.Id, 30);

        // The named mount and not the first one the machine reports: a machine
        // has several filesystems and one of them holds the database.
        Assert.Equal(new DiskSpace(700, 1_000), footprint!.Disk);
    }

    [Fact]
    public async Task A_mount_that_is_not_being_reported_is_no_reading()
    {
        var project = Holding();
        var host = _hosts.Holding("db", Now.AddDays(-30));

        await NamingHost(host.Id, "/var/lib/postgresql");
        _samples.Reporting(host.Id, Now.AddMinutes(-1), "/", 5, 100);

        // The collector was reconfigured and no longer watches the disk the
        // database is on. Nothing here guesses at another one.
        Assert.Null((await OfProject(project.Id, 30))!.Disk);
    }

    [Fact]
    public async Task A_named_host_that_never_reported_is_no_reading_either()
    {
        var project = Holding();
        var host = _hosts.Holding("db", Now.AddDays(-30));

        await NamingHost(host.Id, "/var/lib/postgresql");

        Assert.Null((await OfProject(project.Id, 30))!.Disk);
    }

    [Fact]
    public async Task The_sample_window_costs_what_the_collectors_write()
    {
        var one = _hosts.Holding("web", Now.AddDays(-30));
        var two = _hosts.Holding("db", Now.AddDays(-30));

        _samples.Reporting(one.Id, Now.AddMinutes(-1), "/", 5, 100);
        _samples.Reporting(two.Id, Now.AddMinutes(-1),
        [
            StubSampleReader.Reading(two.Id, Now.AddMinutes(-1), "/", 5, 100),
            StubSampleReader.Reading(two.Id, Now.AddMinutes(-1), "/data", 9, 100),
        ]);

        var footprint = await Act().OfSamplesAsync(
            RetentionWindow.OfDays(90), TestContext.Current.CancellationToken);

        // Two machines and three filesystems between them, a row a minute each.
        Assert.Equal(Footprint.OfSamples(2, 3, RetentionWindow.OfDays(90)), footprint.Implied);
    }

    [Fact]
    public async Task A_host_that_has_never_reported_is_not_in_the_sample_arithmetic()
    {
        var reporting = _hosts.Holding("web", Now.AddDays(-30));
        _hosts.Holding("built-yesterday", Now.AddDays(-1));

        _samples.Reporting(reporting.Id, Now.AddMinutes(-1), "/", 5, 100);

        var footprint = await Act().OfSamplesAsync(
            RetentionWindow.OfDays(30), TestContext.Current.CancellationToken);

        // A machine between being created and its collector being started writes
        // nothing, so it costs nothing. What it will write is not a number this
        // installation has.
        Assert.Equal(Footprint.OfSamples(1, 1, RetentionWindow.OfDays(30)), footprint.Implied);
    }

    [Fact]
    public async Task An_installation_nothing_reports_to_says_nothing_about_samples()
    {
        _hosts.Holding("web", Now.AddDays(-1));

        var footprint = await Act().OfSamplesAsync(
            RetentionWindow.OfDays(30), TestContext.Current.CancellationToken);

        Assert.Null(footprint.Implied);
        // The other two are the installation's and are answered either way,
        // which is the point of showing them on both screens.
        Assert.NotEqual(0, footprint.Held);
    }

    [Fact]
    public async Task Nothing_is_refused_however_large_the_window_comes_to()
    {
        var project = Holding();
        await Delivering(project.Id, anHour: 250_000, days: 14);

        var footprint = await OfProject(project.Id, RetentionWindow.MaximumDays);

        // Two terabytes on a disk this installation may not have. It is a
        // number and not a refusal: the arithmetic is advisory, and time stays
        // the only limit a project has.
        Assert.True(footprint!.Implied > 2L * 1024 * 1024 * 1024 * 1024);
    }

    private Project Holding() =>
        _projects.Holding("api", RetentionWindow.OfDays(30), Now.AddDays(-60));

    private Task NamingHost(Guid hostId, string mount) =>
        _installation.RecordHostAsync(
            new InstallationHost(hostId, MountPath.Create(mount)),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// <paramref name="anHour"/> entries in every hour of <paramref name="days"/>
    /// whole days, the newest of them ending <paramref name="endingDaysAgo"/>
    /// days back — one row an hour, as the flush writes them.
    /// </summary>
    private async Task Delivering(
        Guid projectId, long anHour, int days, int endingDaysAgo = 0)
    {
        var until = Tallying.HourOf(Now).AddDays(-endingDaysAgo);

        for (var day = 1; day <= days; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                await Flush(projectId, until.AddDays(-day).AddHours(hour), anHour);
            }
        }
    }

    private Task Flush(Guid projectId, DateTimeOffset hour, long entries) =>
        _tallies.AddAsync(
            [
                new TallyIncrement
                {
                    ProjectId = projectId,
                    Hour = hour,
                    Entries = entries,
                    AtErrorOrAbove = 0,
                },
            ],
            TestContext.Current.CancellationToken);

    private async Task<Footprint?> OfProject(Guid id, int days) =>
        await Act().OfProjectAsync(
            id, RetentionWindow.OfDays(days), TestContext.Current.CancellationToken);

    private ReadTheFootprint Act() => new(
        _projects, _hosts, _tallies, _samples, _installation, _store, _clock);
}

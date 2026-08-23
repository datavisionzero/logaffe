using Logaffe.Application.Operations;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The three switches, and the machine the installation says it sits on.
/// </summary>
public sealed class AlertSettingActsTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task All_three_are_off_until_the_operator_switches_one_on()
    {
        var switches = new ChangeTheAlertSwitches(_scene.Installation);

        Assert.Equal(
            AlertSwitches.AllOff,
            await switches.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_switches_are_written_and_read_back_as_one_setting()
    {
        var switches = new ChangeTheAlertSwitches(_scene.Installation);

        await switches.ExecuteAsync(
            new AlertSwitches(
                FillingUp: true, GoneQuiet: false, Flooding: true, Failing: true),
            TestContext.Current.CancellationToken);

        var read = await switches.ReadAsync(TestContext.Current.CancellationToken);

        // All four every time: the one left off is written as off rather than
        // left as whatever it was, because a screen that saved them separately
        // would have four ways to be half-applied.
        Assert.True(read.FillingUp);
        Assert.False(read.GoneQuiet);
        Assert.True(read.Flooding);
        Assert.True(read.Failing);
    }

    [Fact]
    public async Task The_machine_and_the_mount_are_named_together()
    {
        var host = _scene.Hosts.Holding("db", AlertScene.Start.AddDays(-9));
        var act = new NameTheInstallationHost(_scene.Installation, _scene.Hosts);

        Assert.Equal(
            NameTheInstallationHostOutcome.Named,
            await act.ExecuteAsync(
                host.Id, "/var/lib/postgresql", TestContext.Current.CancellationToken));

        var named = await act.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(named);
        Assert.Equal(host.Id, named.HostId);
        Assert.Equal("/var/lib/postgresql", named.Mount.Value);
    }

    [Fact]
    public async Task Naming_a_machine_that_is_gone_is_an_answer_rather_than_a_failure()
    {
        var act = new NameTheInstallationHost(_scene.Installation, _scene.Hosts);

        // The ordinary way here is a host deleted from another browser while
        // this screen was open.
        Assert.Equal(
            NameTheInstallationHostOutcome.NoSuchHost,
            await act.ExecuteAsync(
                Guid.NewGuid(), "/var/lib/postgresql", TestContext.Current.CancellationToken));

        Assert.Null(await act.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_machine_without_a_mount_does_not_say_which_filesystem_the_database_is_on()
    {
        var host = _scene.Hosts.Holding("db", AlertScene.Start.AddDays(-9));
        var act = new NameTheInstallationHost(_scene.Installation, _scene.Hosts);

        Assert.Equal(
            NameTheInstallationHostOutcome.NotAMount,
            await act.ExecuteAsync(host.Id, null, TestContext.Current.CancellationToken));

        Assert.Equal(
            NameTheInstallationHostOutcome.NotAMount,
            await act.ExecuteAsync(host.Id, "postgresql", TestContext.Current.CancellationToken));

        Assert.Null(await act.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_mount_the_machine_is_not_reporting_is_accepted_and_read_as_blindness()
    {
        var host = _scene.Hosts.Holding("db", AlertScene.Start.AddDays(-9));
        _scene.Reporting(host, percent: 40, mount: "/");

        var act = new NameTheInstallationHost(_scene.Installation, _scene.Hosts);

        // Refusing here would mean an operator could not name a mount while the
        // collector was down, and could not correct one afterwards either. What
        // it costs is a condition that says it cannot see.
        Assert.Equal(
            NameTheInstallationHostOutcome.Named,
            await act.ExecuteAsync(
                host.Id, "/var/lib/postgresql", TestContext.Current.CancellationToken));

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Blindness.MountAbsent, settings.Store.Blindness);
    }

    [Fact]
    public async Task Naming_no_machine_takes_the_mount_with_it()
    {
        _scene.Sitting(percent: 40);

        var act = new NameTheInstallationHost(_scene.Installation, _scene.Hosts);

        await act.ExecuteAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Null(await act.ReadAsync(TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// Taking one project out of the conditions, which is the whole of what varies
/// per project.
/// </summary>
public sealed class MuteAProjectTests
{
    private readonly InMemoryProjects _projects = new();

    [Fact]
    public async Task A_project_is_created_unmuted_and_is_muted_and_unmuted_again()
    {
        var project = _projects.Holding(
            "api", RetentionWindow.OfDays(30), DateTimeOffset.UnixEpoch);

        Assert.False(project.Muted);

        var mute = new MuteAProject(_projects);

        Assert.Equal(
            MuteAProjectOutcome.Muted,
            await mute.ExecuteAsync(project.Id, true, TestContext.Current.CancellationToken));

        Assert.True(project.Muted);

        await mute.ExecuteAsync(project.Id, false, TestContext.Current.CancellationToken);

        Assert.False(project.Muted);
    }

    [Fact]
    public async Task Muting_a_project_that_is_already_muted_writes_nothing()
    {
        var project = _projects.Holding(
            "api", RetentionWindow.OfDays(30), DateTimeOffset.UnixEpoch);

        var mute = new MuteAProject(_projects);
        await mute.ExecuteAsync(project.Id, true, TestContext.Current.CancellationToken);

        var writes = _projects.Writes;

        await mute.ExecuteAsync(project.Id, true, TestContext.Current.CancellationToken);

        Assert.Equal(writes, _projects.Writes);
    }

    [Fact]
    public async Task A_project_that_is_gone_is_an_answer_rather_than_a_failure()
    {
        Assert.Equal(
            MuteAProjectOutcome.NoSuchProject,
            await new MuteAProject(_projects).ExecuteAsync(
                Guid.NewGuid(), true, TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// What the alerts area says a switch will actually do, in this installation's
/// own numbers.
/// </summary>
/// <remarks>
/// A switch whose behaviour has to be looked up in a document is one that gets
/// turned on once and then distrusted, so these are tested as the sentences the
/// screen makes out of them: which project is put forward, and after how long.
/// Whether the arithmetic behind those numbers is right is the hourly pass's own
/// tests, over the same scene.
/// </remarks>
public sealed class ReadTheAlertSettingsTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task An_installation_that_has_been_asked_nothing_says_so_at_every_end()
    {
        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AlertSwitches.AllOff, settings.Switches);
        Assert.Null(settings.Host);
        Assert.Equal(Blindness.NoHostNamed, settings.Store.Blindness);
        Assert.Null(settings.Quiet.Busiest);
        Assert.Null(settings.Quiet.Quietest);
        Assert.Empty(settings.Fired);
    }

    [Fact]
    public async Task The_two_ends_are_the_project_noticed_soonest_and_the_one_noticed_latest()
    {
        // One delivering every hour of the fortnight, so it has no gap at all
        // and tolerates the floor; one idle every night from one until six, so
        // its longest stretch is five and it tolerates fifteen.
        var busy = _scene.Holding("api");
        await _scene.DeliveringEveryHourAsync(busy, _scene.ClosedHour);

        var nightly = _scene.Holding("batch");
        await _scene.DeliveringAsync(
            nightly,
            _scene.ClosedHour - Tallying.Baseline,
            _scene.ClosedHour,
            hour => hour.Hour is >= 1 and <= 5 ? 0 : 10);

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal("api", settings.Quiet.Busiest?.Name);
        Assert.Equal(Quiet.LeastTolerated, settings.Quiet.Busiest?.ToleratedHours);

        Assert.Equal("batch", settings.Quiet.Quietest?.Name);
        Assert.Equal(15, settings.Quiet.Quietest?.ToleratedHours);
    }

    [Fact]
    public async Task A_project_without_a_fortnight_behind_it_is_counted_rather_than_ranked()
    {
        var young = _scene.Holding("new");
        await _scene.DeliveringAsync(
            young, _scene.ClosedHour.AddDays(-2), _scene.ClosedHour, _ => 10);

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        // It has no normal to have departed from, so it is neither end of the
        // sentence and is the number beside it instead.
        Assert.Null(settings.Quiet.Busiest);
        Assert.Equal(1, settings.Quiet.WithoutAFortnight);
    }

    [Fact]
    public async Task A_muted_project_is_neither_an_end_nor_counted_as_waiting()
    {
        var muted = _scene.Holding("api");
        muted.Mute(true);
        await _scene.DeliveringEveryHourAsync(muted, _scene.ClosedHour);

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        // Putting it forward as what this switch will do would be describing the
        // one project the switch will never say anything about.
        Assert.Null(settings.Quiet.Busiest);
        Assert.Equal(0, settings.Quiet.WithoutAFortnight);
    }

    [Fact]
    public async Task The_disk_is_read_off_what_the_machine_already_reports()
    {
        var host = _scene.Sitting(percent: 87);

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Blindness.None, settings.Store.Blindness);
        Assert.Equal(87, settings.Store.Percent);
        Assert.Equal(host.Id, settings.Host?.HostId);
    }

    [Fact]
    public async Task A_machine_that_has_stopped_reporting_is_a_condition_that_cannot_see()
    {
        _scene.Sitting(percent: 40);
        _scene.Clock.Now = _scene.Clock.Now.Add(Alerting.Reporting).AddMinutes(1);

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        // Yesterday's per cent presented as today's is the number that would be
        // believed, so a stale report is no report.
        Assert.Equal(Blindness.NotReporting, settings.Store.Blindness);
    }

    [Fact]
    public async Task What_fired_is_one_row_per_subject_and_condition_carrying_the_name()
    {
        var project = _scene.Holding("api");
        _scene.SwitchOn(goneQuiet: true);

        // A fortnight of delivering every hour, and then three days of nothing.
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddDays(-3));

        await _scene.RunAsync();

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        var fired = Assert.Single(settings.Fired);

        Assert.Equal(project.Id, fired.SubjectId);
        Assert.Equal("api", fired.Subject);
        Assert.Equal(AlertCondition.GoneQuiet, fired.Condition);
        Assert.Equal(_scene.Clock.Now, fired.At);
    }

    [Fact]
    public async Task A_condition_that_cleared_before_it_ever_fired_is_not_history()
    {
        var project = _scene.Holding("api");
        _scene.SwitchOn(goneQuiet: true);

        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour);

        await _scene.RunAsync();

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        // The row exists because the latch came down; a screen has nothing to
        // say about one, and "never" is not a date.
        Assert.Empty(settings.Fired);
    }

    [Fact]
    public async Task History_about_a_project_that_is_gone_is_left_out_rather_than_shown_nameless()
    {
        var project = _scene.Holding("api");
        _scene.SwitchOn(goneQuiet: true);

        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddDays(-3));
        await _scene.RunAsync();

        await _scene.Projects.RemoveAsync(project, TestContext.Current.CancellationToken);

        var settings = await _scene.Settings.ExecuteAsync(TestContext.Current.CancellationToken);

        // Deleting a project leaves its rows behind, exactly as it leaves its
        // tally: what they are is history about something that no longer exists.
        Assert.Empty(settings.Fired);
    }
}

using Logaffe.Domain.Alerts;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The hourly pass: what it reads before it walks anything, and what a project
/// can be excused from.
/// </summary>
public sealed class EvaluateTheConditionsTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task An_installation_that_has_switched_nothing_on_evaluates_nothing()
    {
        var project = _scene.Holding();
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddDays(-3));

        await _scene.RunAsync();

        // A project silent for three days, and not a word: the switch is read
        // before anything else happens, and all four are off until the operator
        // turns one on.
        Assert.Empty(_scene.Sent);
        Assert.Equal(0, _scene.States.Writes);
    }

    [Fact]
    public async Task A_muted_project_is_not_evaluated_at_all()
    {
        var project = _scene.Holding();
        project.Mute(true);

        _scene.SwitchOn(goneQuiet: true, flooding: true);
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddDays(-3));

        await _scene.RunAsync();

        // Not suppressed on the way out but never asked, so a muted project
        // leaves no state behind either.
        Assert.Empty(_scene.Sent);
        Assert.Equal(0, _scene.States.Writes);
    }

    [Fact]
    public async Task A_project_that_fires_one_condition_is_not_asked_the_other()
    {
        var project = _scene.Holding();

        _scene.SwitchOn(goneQuiet: true, flooding: true);
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddHours(-4));

        await _scene.RunAsync();

        Assert.IsType<Alert.ProjectGoneQuiet>(Assert.Single(_scene.Sent));

        // At most one thing is said about a project in an hour, and the second
        // condition is not evaluated rather than evaluated and dropped.
        Assert.Null(await _scene.States.FindAsync(
            project.Id, AlertCondition.Flooding, TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// The store filling up: the two thresholds, the arming, and the four ways the
/// condition can be switched on and unable to see.
/// </summary>
public sealed class CheckTheStoreIsFillingUpTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task A_disk_over_the_first_threshold_says_so_once()
    {
        _scene.SwitchOn(fillingUp: true);
        var host = _scene.Sitting(percent: 87);

        await _scene.RunAsync();

        var alert = Assert.IsType<Alert.StoreFillingUp>(Assert.Single(_scene.Sent));
        Assert.Equal(host.Id, alert.HostId);
        Assert.Equal("db", alert.HostName);
        Assert.Equal(87, alert.Percent);
        Assert.Equal(StoreFullness.FirstThreshold, alert.Threshold);

        // The disk continuing to fill inside the same threshold is the same
        // event, still happening.
        _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        _scene.Reporting(host, percent: 91);
        await _scene.RunAsync();

        Assert.Single(_scene.Sent);
    }

    [Fact]
    public async Task The_second_threshold_is_said_while_the_first_is_still_latched()
    {
        _scene.SwitchOn(fillingUp: true);
        var host = _scene.Sitting(percent: 87);

        await _scene.RunAsync();

        _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        _scene.Reporting(host, percent: 96);
        await _scene.RunAsync();

        // An hour after the first and inside the six, because a disk that has
        // gone from 85 to 95 in an afternoon is what this condition is for.
        Assert.Equal(2, _scene.Sent.Count);
        Assert.Equal(
            StoreFullness.SecondThreshold,
            Assert.IsType<Alert.StoreFillingUp>(_scene.Sent[1]).Threshold);
    }

    [Fact]
    public async Task A_disk_that_falls_back_and_fills_again_waits_out_the_silence()
    {
        _scene.SwitchOn(fillingUp: true);
        var host = _scene.Sitting(percent: 87);

        await _scene.RunAsync();

        _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        _scene.Reporting(host, percent: 60);
        await _scene.RunAsync();

        // Nothing is sent when a condition clears — no second message, and no
        // resolved.
        Assert.Single(_scene.Sent);

        _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        _scene.Reporting(host, percent: 88);
        await _scene.RunAsync();

        // A second event, and one that would notify hourly if a disk sat on the
        // threshold and flapped across it.
        Assert.Single(_scene.Sent);

        _scene.Clock.Now = _scene.Clock.Now.Add(Alerting.Silence);
        _scene.Reporting(host, percent: 88);
        await _scene.RunAsync();

        Assert.Equal(2, _scene.Sent.Count);
    }

    [Fact]
    public async Task A_disk_that_stays_full_says_nothing_more_however_many_passes_run()
    {
        _scene.SwitchOn(fillingUp: true);
        var host = _scene.Sitting(percent: 87);

        // Eight hourly passes on a disk that has not moved, which is also the
        // eight an installation restarting hourly would run — the state is a row
        // rather than a field a restart loses. It outlasts the six hours of
        // silence, because the silence is the floor under a second event and
        // this is the first one, still happening.
        for (var hour = 0; hour < 8; hour++)
        {
            _scene.Reporting(host, percent: 87);
            await _scene.RunAsync();
            _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        }

        Assert.Single(_scene.Sent);
    }

    [Fact]
    public async Task An_installation_that_names_no_host_is_switched_on_and_blind()
    {
        _scene.SwitchOn(fillingUp: true);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
        Assert.Equal(
            Blindness.NoHostNamed,
            (await _scene.FillingUp.ReadAsync(TestContext.Current.CancellationToken))
                .Blindness);
    }

    [Fact]
    public async Task A_machine_that_stopped_reporting_is_switched_on_and_blind()
    {
        _scene.SwitchOn(fillingUp: true);
        _scene.Sitting(percent: 99);

        // An hour of a machine that speaks every minute is sixty missed
        // readings, and yesterday's per cent is the number that would be
        // believed.
        _scene.Clock.Now = _scene.Clock.Now.AddHours(2);
        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
        Assert.Equal(
            Blindness.NotReporting,
            (await _scene.FillingUp.ReadAsync(TestContext.Current.CancellationToken))
                .Blindness);
    }

    [Fact]
    public async Task A_mount_that_is_not_among_what_arrives_is_switched_on_and_blind()
    {
        _scene.SwitchOn(fillingUp: true);
        var host = _scene.Sitting(percent: 99);
        _scene.Reporting(host, percent: 99, mount: "/mnt/other");

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
        Assert.Equal(
            Blindness.MountAbsent,
            (await _scene.FillingUp.ReadAsync(TestContext.Current.CancellationToken))
                .Blindness);
    }

    [Fact]
    public async Task A_disk_below_both_thresholds_says_nothing()
    {
        _scene.SwitchOn(fillingUp: true);
        _scene.Sitting(percent: 84);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }
}

/// <summary>
/// A project going quiet: what it takes to fire, and the three shapes of project
/// this must never wake anybody about.
/// </summary>
public sealed class CheckAProjectHasGoneQuietTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task A_project_delivering_every_hour_is_noticed_on_its_second_silent_hour()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(goneQuiet: true);

        // Delivering up to the hour before the one being evaluated: one silent
        // hour, and a project with no gap at all tolerates one.
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddHours(-1));

        await _scene.RunAsync();
        Assert.Empty(_scene.Sent);

        _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        await _scene.RunAsync();

        var alert = Assert.IsType<Alert.ProjectGoneQuiet>(Assert.Single(_scene.Sent));
        Assert.Equal(project.Id, alert.ProjectId);
        Assert.Equal(2, alert.Hours);
        Assert.Equal(Quiet.LeastTolerated, alert.Tolerated);
    }

    [Fact]
    public async Task A_condition_that_holds_for_a_day_is_one_notification()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(goneQuiet: true);
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddHours(-1));

        await _scene.RunForHoursAsync(24);

        // Not twenty-four, and not four either: it is one event, still
        // happening, and the six hours are the floor under a second event
        // rather than a repeat.
        Assert.Single(_scene.Sent);
    }

    [Fact]
    public async Task A_project_idle_every_night_is_not_woken_for_its_nights()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(goneQuiet: true);

        var today = _scene.ClosedHour;

        // Idle from one until six, every night, for the fortnight and through
        // the two nights being evaluated.
        await _scene.DeliveringAsync(
            project,
            today - Tallying.Baseline,
            today.AddDays(2),
            hour => hour.Hour is >= 1 and < 6 ? 0 : 10);

        // Two in the morning, and every hour of the night up to the deliveries
        // starting again — on both of them, because the answer has to be the
        // same on any night rather than on the first one after a busy fortnight.
        foreach (var night in (int[])[1, 2])
        {
            _scene.Clock.Now = today.AddDays(night).AddHours(2).AddMinutes(5);

            await _scene.RunForHoursAsync(5);
        }

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_project_created_yesterday_fires_nothing_however_quiet_it_is()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(goneQuiet: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-1),
            _scene.ClosedHour.AddHours(-12),
            _ => 10);

        await _scene.RunAsync();

        // Twelve silent hours against a day of history, and no alarm: a project
        // with less than a fortnight behind it has no normal to have departed
        // from.
        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_project_that_has_never_received_anything_fires_nothing()
    {
        _scene.Holding();
        _scene.SwitchOn(goneQuiet: true);

        await _scene.RunAsync();

        // A project created and not yet deployed is not an incident.
        Assert.Empty(_scene.Sent);
        Assert.Equal(0, _scene.States.Writes);
    }

    [Fact]
    public async Task A_project_that_comes_back_is_not_told_about()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(goneQuiet: true);
        await _scene.DeliveringEveryHourAsync(project, _scene.ClosedHour.AddHours(-4));

        await _scene.RunAsync();
        Assert.Single(_scene.Sent);

        _scene.Clock.Now = _scene.Clock.Now.AddHours(1);
        await _scene.DeliveringAsync(
            project, _scene.ClosedHour, _scene.ClosedHour, _ => 10);

        await _scene.RunAsync();

        // Nothing is sent when a condition clears: no second notification, no
        // resolved state, and no record of one.
        Assert.Single(_scene.Sent);
    }
}

/// <summary>
/// A project flooding: the ratio, the floor under it, and the median by hour of
/// the day that keeps a nightly batch from firing every night.
/// </summary>
public sealed class CheckAProjectIsFloodingTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task An_hour_far_above_that_hour_of_the_day_says_so()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline,
            _scene.ClosedHour,
            hour => hour == _scene.ClosedHour ? 50_000 : 10);

        await _scene.RunAsync();

        var alert = Assert.IsType<Alert.ProjectFlooding>(Assert.Single(_scene.Sent));
        Assert.Equal(_scene.ClosedHour, alert.Hour);
        Assert.Equal(50_000, alert.Entries);
        Assert.Equal(10, alert.Baseline);
    }

    [Fact]
    public async Task A_nightly_batch_is_normal_at_the_hour_it_runs_at()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        // Fifty thousand entries at three in the morning, every night, and a
        // quiet hundred the rest of the day.
        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-15),
            _scene.ClosedHour.AddDays(1),
            hour => hour.Hour == 3 ? 50_000 : 100);

        // Four in the morning, so the hour that has just closed is the batch's.
        _scene.Clock.Now = Midnight(_scene.ClosedHour.AddDays(1)).AddHours(4).AddMinutes(5);
        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task The_same_batch_at_an_hour_it_never_runs_at_says_so()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-15),
            _scene.ClosedHour.AddDays(1),
            hour => hour.Hour == 3 ? 50_000 : 100);

        // Two in the afternoon, where a hundred is what this project does.
        var afternoon = Midnight(_scene.ClosedHour.AddDays(1)).AddHours(14);
        await _scene.DeliveringAsync(project, afternoon, afternoon, _ => 49_900);

        _scene.Clock.Now = afternoon.AddHours(1).AddMinutes(5);
        await _scene.RunAsync();

        Assert.IsType<Alert.ProjectFlooding>(Assert.Single(_scene.Sent));
    }

    [Fact]
    public async Task Two_entries_becoming_twenty_says_nothing()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline,
            _scene.ClosedHour,
            hour => hour == _scene.ClosedHour ? 20 : 2);

        await _scene.RunAsync();

        // A tenfold rise, and not an incident in any project ever: the floor is
        // absolute and it is not a ratio.
        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_project_created_yesterday_fires_nothing_however_much_arrives()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-1),
            _scene.ClosedHour,
            hour => hour == _scene.ClosedHour ? 50_000 : 10);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task An_hour_the_project_is_normally_silent_in_still_needs_the_floor()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        // Nothing at this hour of the day for a fortnight, and nine hundred
        // entries in it now: a baseline of nought, and ten times nothing is
        // nothing.
        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-15),
            _scene.ClosedHour,
            hour => hour == _scene.ClosedHour ? 900 : hour.Hour == 13 ? 0 : 10);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task An_hour_the_project_is_normally_silent_in_fires_above_the_floor()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true);

        // The hour of the day this project has never written anything in, with
        // five thousand entries in it now.
        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-15),
            _scene.ClosedHour,
            hour => hour == _scene.ClosedHour ? 5_000 : hour.Hour == 13 ? 0 : 10);

        await _scene.RunAsync();

        var alert = Assert.IsType<Alert.ProjectFlooding>(Assert.Single(_scene.Sent));
        Assert.Equal(0, alert.Baseline);
    }

    /// <summary>
    /// The start of that day, so that a test naming an hour of the day names the
    /// hour it means rather than one counted from whatever hour the scene starts
    /// at.
    /// </summary>
    private static DateTimeOffset Midnight(DateTimeOffset moment) =>
        new(moment.Year, moment.Month, moment.Day, 0, 0, 0, TimeSpan.Zero);
}


/// <summary>
/// A project failing far more than it does: the ratio on the tally's second
/// number, the floor of its own, and the second hour that is the whole of what
/// separates this from a flood.
/// </summary>
public sealed class CheckAProjectIsFailingTests
{
    private readonly AlertScene _scene = new();

    [Fact]
    public async Task Two_hours_of_errors_far_above_that_hour_of_the_day_says_so()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await Failing(project, closedHour: 500, previousHour: 300);

        await _scene.RunAsync();

        var alert = Assert.IsType<Alert.ProjectFailing>(Assert.Single(_scene.Sent));
        Assert.Equal(_scene.ClosedHour, alert.Hour);
        Assert.Equal(500, alert.Errors);

        // The hour before rides along because it is the answer to "why now, and
        // not an hour ago".
        Assert.Equal(300, alert.Previous);
        Assert.Equal(0, alert.Baseline);
    }

    [Fact]
    public async Task A_deploys_spike_inside_one_hour_says_nothing()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await Failing(project, closedHour: 5_000, previousHour: 0);

        await _scene.RunAsync();

        // Not because the burst was filtered, but because it stopped. This is
        // the objection the condition was deferred on, answered by the second
        // hour rather than argued past.
        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_storm_that_resolved_itself_says_nothing_either()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await Failing(project, closedHour: 0, previousHour: 5_000);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task Nine_errors_against_none_stays_under_the_floor()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await Failing(project, closedHour: 9, previousHour: 9);

        await _scene.RunAsync();

        // The ratio is infinite and the floor is absolute. A project that fails
        // nine times in an hour twice over has not had an incident.
        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_project_that_logs_a_handled_exception_per_request_has_its_own_normal()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        // Two hundred errors an hour is what this project is: the median is high
        // and ten times it is proportionally high.
        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline - TimeSpan.FromHours(1),
            _scene.ClosedHour,
            _ => 10_000,
            _ => 200);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task The_same_project_failing_ten_times_its_own_normal_says_so()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline - TimeSpan.FromHours(1),
            _scene.ClosedHour,
            _ => 10_000,
            hour => hour >= _scene.ClosedHour.AddHours(-1) ? 5_000 : 200);

        await _scene.RunAsync();

        var alert = Assert.IsType<Alert.ProjectFailing>(Assert.Single(_scene.Sent));
        Assert.Equal(200, alert.Baseline);
    }

    [Fact]
    public async Task Each_of_the_two_hours_is_judged_against_its_own_hour_of_the_day()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        // A project that fails five hundred times at one in the afternoon every
        // day and never at two: the ordinary one o'clock is normal for one
        // o'clock and abnormal for two.
        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-16),
            _scene.ClosedHour.AddDays(1),
            _ => 10_000,
            hour => hour.Hour == 13 ? 500 : 0);

        // Three in the afternoon, so the two hours judged are two o'clock — with
        // five thousand errors in it — and the ordinary one o'clock before it.
        var afternoon = Midnight(_scene.ClosedHour.AddDays(1)).AddHours(14);
        await _scene.DeliveringAsync(project, afternoon, afternoon, _ => 10_000, _ => 5_000);

        _scene.Clock.Now = afternoon.AddHours(1).AddMinutes(5);
        await _scene.RunAsync();

        // One baseline for both hours would have judged one o'clock's five
        // hundred against two o'clock's nought and fired on a project doing
        // exactly what it does every day.
        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_project_without_a_fortnight_fires_nothing_however_it_fails()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour.AddDays(-1),
            _scene.ClosedHour,
            _ => 10_000,
            hour => hour >= _scene.ClosedHour.AddHours(-1) ? 5_000 : 0);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task A_flood_of_entries_that_are_not_errors_says_nothing_here()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        // Fifty thousand entries in each of two hours and not one of them at
        // Error: this condition counts the tally's second number and nothing
        // else.
        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline - TimeSpan.FromHours(1),
            _scene.ClosedHour,
            hour => hour >= _scene.ClosedHour.AddHours(-1) ? 50_000 : 10,
            _ => 0);

        await _scene.RunAsync();

        Assert.Empty(_scene.Sent);
    }

    [Fact]
    public async Task Failing_outranks_flooding_when_one_hour_is_both()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(flooding: true, failing: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline - TimeSpan.FromHours(1),
            _scene.ClosedHour,
            hour => hour >= _scene.ClosedHour.AddHours(-1) ? 50_000 : 10,
            hour => hour >= _scene.ClosedHour.AddHours(-1) ? 5_000 : 0);

        await _scene.RunAsync();

        // Both hold on this hour, and the operator gets the sentence that names
        // what is wrong rather than the one that names how much of it there is.
        Assert.IsType<Alert.ProjectFailing>(Assert.Single(_scene.Sent));

        // The one that lost is not evaluated rather than evaluated and dropped.
        Assert.Null(await _scene.States.FindAsync(
            project.Id, AlertCondition.Flooding, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failure_that_is_still_failing_says_nothing_more()
    {
        var project = _scene.Holding();
        _scene.SwitchOn(failing: true);

        await _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline - TimeSpan.FromHours(1),
            _scene.ClosedHour.AddHours(5),
            _ => 10_000,
            hour => hour >= _scene.ClosedHour.AddHours(-1) ? 5_000 : 0);

        await _scene.RunForHoursAsync(5);

        // One event, still happening. The shared guard covers this condition
        // exactly as it covers the other three.
        Assert.Single(_scene.Sent);
    }

    /// <summary>
    /// A fortnight of a project that never fails, and then the two hours being
    /// judged, each with the errors it is given.
    /// </summary>
    private Task Failing(Project project, long closedHour, long previousHour) =>
        _scene.DeliveringAsync(
            project,
            _scene.ClosedHour - Tallying.Baseline - TimeSpan.FromHours(1),
            _scene.ClosedHour,
            _ => 10_000,
            hour =>
                hour == _scene.ClosedHour ? closedHour
                : hour == _scene.ClosedHour.AddHours(-1) ? previousHour
                : 0);

    /// <inheritdoc cref="CheckAProjectIsFloodingTests"/>
    private static DateTimeOffset Midnight(DateTimeOffset moment) =>
        new(moment.Year, moment.Month, moment.Day, 0, 0, 0, TimeSpan.Zero);
}

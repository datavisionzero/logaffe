using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;

using Host = Logaffe.Domain.Hosts.Host;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What the installation remembers about the conditions that have fired, in
/// memory. It behaves as the real store does in the one way the acts turn on: a
/// state written is the state read back on the next pass, which is what a
/// restart is being asked about.
/// </summary>
internal sealed class InMemoryConditionStates : IConditionStates
{
    private readonly Dictionary<(Guid SubjectId, AlertCondition Condition), ConditionState> _rows =
        [];

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    public Task<ConditionState?> FindAsync(
        Guid subjectId, AlertCondition condition, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault((subjectId, condition)));

    public Task RecordAsync(ConditionState state, CancellationToken cancellationToken)
    {
        _rows[(state.SubjectId, state.Condition)] = state;
        Writes++;

        return Task.CompletedTask;
    }
}

/// <summary>
/// The notifier, holding what it was handed rather than sending it. What a test
/// asks it is what left the installation and in what order — and, as often, that
/// nothing did.
/// </summary>
internal sealed class RecordingNotifier : IAlertNotifier
{
    public List<Alert> Sent { get; } = [];

    public Task SendAsync(Alert alert, CancellationToken cancellationToken)
    {
        Sent.Add(alert);

        return Task.CompletedTask;
    }
}

/// <summary>
/// The installation an alerting test stands in: a clock it moves, the stores the
/// three conditions read, and the notifier they end at.
/// </summary>
/// <remarks>
/// It is here rather than repeated in every test because what these tests are
/// about is what an installation does over several hours — a condition fires,
/// holds, clears and fires again — and that is a scene rather than a call. The
/// pieces are the ordinary doubles the other acts are tested against.
/// </remarks>
internal sealed class AlertScene
{
    /// <summary>Five past two in the afternoon, so the closed hour is one.</summary>
    public static readonly DateTimeOffset Start = new(2026, 8, 22, 14, 5, 0, TimeSpan.Zero);

    public StoppedClock Clock { get; } = new(Start);

    public InMemoryInstallation Installation { get; } = new();

    public InMemoryProjects Projects { get; } = new();

    public InMemoryHosts Hosts { get; } = new();

    public StubSampleReader Samples { get; } = new();

    public RecordingTallies Tallies { get; } = new();

    public InMemoryConditionStates States { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    /// <summary>What everything sent so far was about, in order.</summary>
    public IReadOnlyList<Alert> Sent => Notifier.Sent;

    public DateTimeOffset ClosedHour => Alerting.ClosedHourAt(Clock.Now);

    public CheckTheStoreIsFillingUp FillingUp =>
        new(Installation, Hosts, Samples, States, Clock);

    public CheckAProjectHasGoneQuiet GoneQuiet => new(Tallies, States, Clock);

    public CheckAProjectIsFlooding Flooding => new(Tallies, States, Clock);

    public EvaluateTheConditions Pass =>
        new(Installation, Projects, FillingUp, GoneQuiet, Flooding, Notifier, Clock);

    public void SwitchOn(
        bool fillingUp = false, bool goneQuiet = false, bool flooding = false) =>
        Installation.Switches = new AlertSwitches(fillingUp, goneQuiet, flooding);

    /// <summary>A project that has been there a good while.</summary>
    public Project Holding(string name = "api") =>
        Projects.Holding(name, RetentionWindow.OfDays(30), Start.AddDays(-90));

    /// <summary>The machine the installation says it sits on, reporting.</summary>
    public Host Sitting(int percent, string mount = "/var/lib/postgresql")
    {
        var host = Hosts.Holding("db", Start.AddDays(-90));

        // The double writes in memory, so there is nothing here to wait for.
        _ = Installation.RecordHostAsync(
            new InstallationHost(host.Id, MountPath.Create(mount)),
            TestContext.Current.CancellationToken);

        Reporting(host, percent, mount);

        return host;
    }

    /// <summary>What that machine's newest reading says about its disk.</summary>
    public void Reporting(Host host, int percent, string mount = "/var/lib/postgresql") =>
        Samples.Reporting(host.Id, Clock.Now, mount, percent, 100);

    /// <summary>
    /// What a project delivered in each hour of a stretch, an hour answered
    /// nought leaving no row at all — which is how the tally says nothing
    /// arrived.
    /// </summary>
    public async Task DeliveringAsync(
        Project project,
        DateTimeOffset from,
        DateTimeOffset until,
        Func<DateTimeOffset, long> entries)
    {
        var increments = new List<TallyIncrement>();

        for (var hour = from; hour <= until; hour = hour.AddHours(1))
        {
            var count = entries(hour);
            if (count > 0)
            {
                increments.Add(new TallyIncrement
                {
                    ProjectId = project.Id,
                    Hour = hour,
                    Entries = count,
                    AtErrorOrAbove = 0,
                });
            }
        }

        await Tallies.AddAsync(increments, TestContext.Current.CancellationToken);
    }

    /// <summary>A fortnight of history behind the hour being evaluated.</summary>
    public Task DeliveringEveryHourAsync(
        Project project, DateTimeOffset until, long entries = 10) =>
        DeliveringAsync(project, ClosedHour - Tallying.Baseline, until, _ => entries);

    /// <summary>One hourly pass.</summary>
    public Task RunAsync() => Pass.ExecuteAsync(TestContext.Current.CancellationToken);

    /// <summary>An hourly pass, an hour later, for as many hours as asked.</summary>
    public async Task RunForHoursAsync(int hours)
    {
        for (var hour = 0; hour < hours; hour++)
        {
            await RunAsync();
            Clock.Now = Clock.Now.AddHours(1);
        }
    }
}

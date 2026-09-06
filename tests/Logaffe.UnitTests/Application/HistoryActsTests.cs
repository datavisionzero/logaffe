using Logaffe.Application.Operations;
using Logaffe.Domain.History;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What the installation writes down when somebody changes its configuration
/// (<c>CONTEXT.md</c>, Change).
/// </summary>
public sealed class HistoryActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryHistory _history = new();
    private readonly InMemoryProjects _projects = new();
    private readonly InMemoryGroups _groups = new();
    private readonly InMemoryProjectAccess _access = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task Creating_a_project_says_who_did_it_and_what_it_was_called()
    {
        var created = await CreateAsync("orders-api");

        var change = Assert.Single(_history.Recorded);

        Assert.Equal(Recording.Somebody.Id, change.ActorId);
        Assert.Equal(IdentityKind.User, change.ActorKind);
        Assert.Equal(Subject.Project, change.Subject);
        Assert.Equal(created.Project!.Id, change.SubjectId);
        Assert.Equal("orders-api", change.SubjectName);
        Assert.Equal(Act.Created, change.Act);

        // A creation is not a field moving, so it names none.
        Assert.Null(change.Field);
    }

    [Fact]
    public async Task Deleting_a_project_keeps_the_name_it_had()
    {
        var created = await CreateAsync("orders-api");

        Assert.True(await new DeleteProject(_projects, Recorder()).ExecuteAsync(
            Reach.TheInstallation, created.Project!.Id, TestContext.Current.CancellationToken));

        // *Who deleted project X* is the question this exists to answer, and X
        // is not there to be looked up afterwards.
        var removed = _history.Recorded[^1];
        Assert.Equal(Act.Removed, removed.Act);
        Assert.Equal("orders-api", removed.SubjectName);
    }

    [Fact]
    public async Task A_setting_that_moves_says_which_one_and_from_what()
    {
        var created = await CreateAsync("orders-api");

        await new ChangeRetentionWindow(_projects, Recorder()).ExecuteAsync(
            Reach.TheInstallation,
            created.Project!.Id,
            RetentionWindow.OfDays(30),
            TestContext.Current.CancellationToken);

        var change = _history.Recorded[^1];
        Assert.Equal(Act.Changed, change.Act);
        Assert.Equal("retention", change.Field);
        Assert.Equal("7 days", change.From);
        Assert.Equal("30 days", change.To);
    }

    [Fact]
    public async Task An_agent_is_recorded_as_an_agent_and_not_as_its_owner()
    {
        var agent = Agent.Create("the terminal agent", Recording.Somebody.Id, Now);

        await new CreateProject(_projects, _groups, _access, Recorder(agent), _clock)
            .ExecuteAsync(
                Recording.Somebody.Id,
                "orders-api",
                RetentionWindow.OfDays(7),
                groupId: null,
                TestContext.Current.CancellationToken);

        // An agent acts with its owner's authority, so a row naming the owner
        // and not saying an agent held the keyboard would be true and misleading
        // at once (ADR 0052).
        var change = Assert.Single(_history.Recorded);
        Assert.Equal(IdentityKind.Agent, change.ActorKind);
        Assert.Equal(agent.Id, change.ActorId);
        Assert.Equal("the terminal agent", change.ActorName);
    }

    [Fact]
    public async Task A_scope_with_nobody_behind_it_records_nothing_and_still_changes_it()
    {
        var nobody = new RecordAChange(_history, new TheActor(), _clock);

        var created = await new CreateProject(_projects, _groups, _access, nobody, _clock)
            .ExecuteAsync(
                Guid.CreateVersion7(),
                "orders-api",
                RetentionWindow.OfDays(7),
                groupId: null,
                TestContext.Current.CancellationToken);

        // The history exists to answer questions afterwards, not to be a second
        // thing that has to work for the product to work.
        Assert.Equal(CreateProjectOutcome.Created, created.Outcome);
        Assert.Empty(_history.Recorded);
    }

    [Fact]
    public void A_change_names_the_field_and_nothing_else_does()
    {
        // A row saying `removed` and naming a field, or `changed` and naming
        // none, is a row nobody can read.
        Assert.Throws<ArgumentException>(() => Change.Of(
            Recording.Somebody, Now, Subject.Project, Guid.CreateVersion7(), "orders-api",
            Act.Removed, field: "retention"));

        Assert.Throws<ArgumentException>(() => Change.Of(
            Recording.Somebody, Now, Subject.Project, Guid.CreateVersion7(), "orders-api",
            Act.Changed));
    }

    [Fact]
    public void A_name_longer_than_the_column_is_cut_rather_than_refused()
    {
        var change = Change.Of(
            Recording.Somebody,
            Now,
            Subject.Project,
            Guid.CreateVersion7(),
            new string('x', Change.TextMaxLength + 50),
            Act.Created);

        // The alternative is a history that can be made arbitrarily large by
        // naming things at length.
        Assert.Equal(Change.TextMaxLength, change.SubjectName.Length);
    }

    private Task<CreationAttempt> CreateAsync(string name) =>
        new CreateProject(_projects, _groups, _access, Recorder(), _clock).ExecuteAsync(
            Recording.Somebody.Id,
            name,
            RetentionWindow.OfDays(7),
            groupId: null,
            TestContext.Current.CancellationToken);

    private RecordAChange Recorder(Identity? actor = null) => Recording.Of(_history, actor);
}

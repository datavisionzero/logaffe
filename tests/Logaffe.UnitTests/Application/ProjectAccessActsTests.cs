using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The one filter every read narrows to, and the two acts that change what it
/// holds (ADR 0055).
/// </summary>
public sealed class ProjectAccessActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly InMemoryGroups _groups = new();
    private readonly InMemoryIdentities _identities = new();
    private readonly InMemoryProjectAccess _access = new();
    private readonly StoppedClock _clock = new(Now);

    private readonly User _somebody;
    private readonly User _somebodyElse;

    public ProjectAccessActsTests()
    {
        _somebody = _identities.Seed(Active("somebody@example.com"));
        _somebodyElse = _identities.Seed(Active("else@example.com"));
    }

    [Fact]
    public async Task Whoever_creates_a_project_reaches_it()
    {
        var created = await CreateAsync("orders-api", _somebody);

        Assert.Equal(CreateProjectOutcome.Created, created.Outcome);

        // The alternative is a project its creator cannot open, which is a bug
        // report waiting to happen.
        var reach = await Reaching().ExecuteAsync(
            _somebody, TestContext.Current.CancellationToken);
        Assert.True(reach.Includes(created.Project!.Id));

        // And nobody else gains anything by it.
        var theirs = await Reaching().ExecuteAsync(
            _somebodyElse, TestContext.Current.CancellationToken);
        Assert.False(theirs.Includes(created.Project.Id));
    }

    [Fact]
    public async Task A_new_account_reaches_nothing()
    {
        await CreateAsync("orders-api", _somebody);

        var newcomer = _identities.Seed(
            User.Invite("Newcomer", "newcomer@example.com", administrator: false, Now));

        // An invitation grants an account, not a view. The two directions are
        // asymmetric on purpose: an upgrade loses nobody, and a new account
        // gains nothing.
        var reach = await Reaching().ExecuteAsync(
            newcomer, TestContext.Current.CancellationToken);
        Assert.Empty(reach.Projects);
    }

    [Fact]
    public async Task A_project_out_of_reach_is_not_in_the_list_and_is_not_found()
    {
        var mine = (await CreateAsync("orders-api", _somebody)).Project!;
        var theirs = (await CreateAsync("checkout", _somebodyElse)).Project!;

        var reach = await Reaching().ExecuteAsync(
            _somebody, TestContext.Current.CancellationToken);

        Assert.Equal(
            [mine.Id],
            (await _projects.ListAsync(reach, TestContext.Current.CancellationToken))
                .Select(project => project.Id));

        // Absent rather than present and refused: asking for it by id answers
        // the way asking for one that was never created answers.
        Assert.Null(await _projects.FindAsync(
            reach, theirs.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_installation_s_own_reach_narrows_nothing()
    {
        await CreateAsync("orders-api", _somebody);
        await CreateAsync("checkout", _somebodyElse);

        // What the retention sweep, the alert pass and the ingest path run
        // with — the work that has nobody behind it.
        Assert.Equal(
            2,
            (await _projects.ListAsync(
                Reach.TheInstallation, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task An_administrator_assigns_a_project_and_takes_it_back()
    {
        var project = (await CreateAsync("orders-api", _somebody)).Project!;

        Assert.Equal(
            AssignProjectOutcome.Assigned,
            await Assigning().GrantAsync(
                project.Id, _somebodyElse.Id, _somebody.Id,
                TestContext.Current.CancellationToken));

        Assert.True((await Reaching().ExecuteAsync(
            _somebodyElse, TestContext.Current.CancellationToken)).Includes(project.Id));

        Assert.Equal(
            AssignProjectOutcome.Withdrawn,
            await Assigning().WithdrawAsync(
                project.Id, _somebodyElse.Id, TestContext.Current.CancellationToken));

        Assert.False((await Reaching().ExecuteAsync(
            _somebodyElse, TestContext.Current.CancellationToken)).Includes(project.Id));
    }

    [Fact]
    public async Task Assigning_twice_is_the_state_that_was_asked_for()
    {
        var project = (await CreateAsync("orders-api", _somebody)).Project!;

        await Assigning().GrantAsync(
            project.Id, _somebodyElse.Id, _somebody.Id, TestContext.Current.CancellationToken);

        Assert.Equal(
            AssignProjectOutcome.Assigned,
            await Assigning().GrantAsync(
                project.Id, _somebodyElse.Id, _somebody.Id,
                TestContext.Current.CancellationToken));

        Assert.Single(_access.Granted, a => a.UserId == _somebodyElse.Id);
    }

    [Fact]
    public async Task An_assignment_naming_nothing_says_which_half_was_missing()
    {
        var project = (await CreateAsync("orders-api", _somebody)).Project!;

        Assert.Equal(
            AssignProjectOutcome.NoSuchProject,
            await Assigning().GrantAsync(
                Guid.CreateVersion7(), _somebody.Id, _somebody.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            AssignProjectOutcome.NoSuchUser,
            await Assigning().GrantAsync(
                project.Id, Guid.CreateVersion7(), _somebody.Id,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_list_names_the_people_rather_than_identifying_them()
    {
        var project = (await CreateAsync("orders-api", _somebody)).Project!;

        var listed = await new ListProjectAccess(_projects, _identities, _access)
            .ExecuteAsync(project.Id, TestContext.Current.CancellationToken);

        var only = Assert.Single(listed!);
        Assert.Equal(_somebody.Id, only.UserId);
        Assert.Equal(_somebody.Email, only.Email);

        // Granted by themselves, because there is nobody else it could
        // truthfully name: they created it.
        Assert.Equal(_somebody.Id, only.GrantedBy);
    }

    private Task<CreationAttempt> CreateAsync(string name, User creator) =>
        new CreateProject(_projects, _groups, _access, _clock).ExecuteAsync(
            creator.Id,
            name,
            RetentionWindow.OfDays(7),
            groupId: null,
            TestContext.Current.CancellationToken);

    private ResolveReach Reaching() => new(_access);

    private AssignProject Assigning() => new(_projects, _identities, _access, _clock);

    private static User Active(string email)
    {
        var user = User.Bootstrap("Somebody", email, Now);
        user.ActivateWith("$argon2id$v=19$m=19456,t=2,p=1$not-a-real-hash");

        return user;
    }
}

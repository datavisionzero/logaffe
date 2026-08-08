using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What the operator does to a project: create it, rename it, change how long
/// it keeps its entries, and end it.
/// </summary>
public sealed class ProjectActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly InMemoryTokens _tokens = new();
    private readonly RecordingReader _entries = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task A_created_project_is_a_name_a_window_and_an_identity()
    {
        var project = await CreateAsync("api", 14);

        Assert.NotNull(project);
        Assert.Equal("api", project.Name);
        Assert.Equal(14, project.Retention.Days);
        Assert.Equal(Now, project.CreatedAt);
        Assert.Equal(project.Id, Assert.Single(_projects.Stored).Id);
    }

    [Fact]
    public async Task A_created_project_receives_nothing_until_a_token_is_issued()
    {
        // Creation mints no credential. A project with no token is a project
        // whose door is closed, which is a state the operator can also arrive
        // at by revoking.
        var project = await CreateAsync("api", 7);

        var listed = Assert.Single(await Listing().ExecuteAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal(project!.Id, listed.Id);
        Assert.Equal(0, listed.IngestTokens);
    }

    [Fact]
    public async Task A_second_project_by_that_name_is_refused_and_nothing_is_written()
    {
        await CreateAsync("api", 7);
        var writesBefore = _projects.Writes;

        // Two projects called `api` is a trap for the operator reaching for one
        // of them at three in the morning.
        Assert.Null(await CreateAsync("api", 30));
        Assert.Single(_projects.Stored);
        Assert.Equal(writesBefore, _projects.Writes);
    }

    [Fact]
    public async Task A_name_is_taken_as_it_would_be_stored_rather_than_as_it_was_typed()
    {
        await CreateAsync("api", 7);

        Assert.Null(await CreateAsync("  api  ", 7));
    }

    [Fact]
    public async Task The_identity_survives_a_rename_and_the_name_does_not()
    {
        var project = await CreateAsync("api", 7);

        Assert.Equal(
            RenameOutcome.Renamed,
            await Renaming().ExecuteAsync(
                project!.Id, "orders-api", TestContext.Current.CancellationToken));

        var stored = Assert.Single(_projects.Stored);
        Assert.Equal(project.Id, stored.Id);
        Assert.Equal("orders-api", stored.Name);
    }

    [Fact]
    public async Task A_rename_onto_another_projects_name_is_refused()
    {
        await CreateAsync("api", 7);
        var web = await CreateAsync("web", 7);

        Assert.Equal(
            RenameOutcome.NameTaken,
            await Renaming().ExecuteAsync(
                web!.Id, "api", TestContext.Current.CancellationToken));
        Assert.Equal("web", _projects.Stored.Single(p => p.Id == web.Id).Name);
    }

    [Fact]
    public async Task A_project_renamed_to_the_name_it_has_does_not_collide_with_itself()
    {
        // The operator opened the field and left it.
        var project = await CreateAsync("api", 7);

        Assert.Equal(
            RenameOutcome.Renamed,
            await Renaming().ExecuteAsync(
                project!.Id, "api", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Renaming_a_project_that_is_not_there_says_so()
    {
        Assert.Equal(
            RenameOutcome.NoSuchProject,
            await Renaming().ExecuteAsync(
                Guid.CreateVersion7(), "api", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_retention_window_is_changed_and_nothing_is_swept_here()
    {
        var project = await CreateAsync("api", 90);

        Assert.True(await Changing().ExecuteAsync(
            project!.Id, RetentionWindow.OfDays(7), TestContext.Current.CancellationToken));

        // Lowering it puts entries outside the window; the sweep is what
        // removes them, and it is not this act.
        Assert.Equal(7, Assert.Single(_projects.Stored).Retention.Days);
    }

    [Fact]
    public async Task Changing_the_window_of_a_project_that_is_not_there_says_so() =>
        Assert.False(await Changing().ExecuteAsync(
            Guid.CreateVersion7(),
            RetentionWindow.OfDays(7),
            TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_deleted_project_is_gone_and_its_name_is_free_again()
    {
        var project = await CreateAsync("api", 7);

        Assert.True(await Deleting().ExecuteAsync(
            project!.Id, TestContext.Current.CancellationToken));

        Assert.Empty(_projects.Stored);
        Assert.NotNull(await CreateAsync("api", 7));
    }

    [Fact]
    public async Task Deleting_a_project_that_is_already_gone_says_so_and_writes_nothing()
    {
        var project = await CreateAsync("api", 7);
        Assert.True(await Deleting().ExecuteAsync(
            project!.Id, TestContext.Current.CancellationToken));
        var writesBefore = _projects.Writes;

        // A second click, or another browser tab, and not a failure of
        // anything.
        Assert.False(await Deleting().ExecuteAsync(
            project.Id, TestContext.Current.CancellationToken));
        Assert.Equal(writesBefore, _projects.Writes);
    }

    [Fact]
    public async Task The_list_is_oldest_first_and_carries_what_each_project_can_receive_on()
    {
        var api = await CreateAsync("api", 7);
        _clock.Now = Now.AddMinutes(1);
        var web = await CreateAsync("web", 30);

        // One mid-rotation, one with the door closed.
        await IssueAsync(api!.Id);
        await IssueAsync(api.Id);

        var listed = await Listing().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal([api.Id, web!.Id], listed.Select(project => project.Id));
        Assert.Equal([2, 0], listed.Select(project => project.IngestTokens));
        Assert.Equal([7, 30], listed.Select(project => project.Retention.Days));
    }

    [Fact]
    public async Task Each_row_says_when_that_project_last_received_an_entry()
    {
        var api = await CreateAsync("api", 7);
        var web = await CreateAsync("web", 30);
        _entries.Received[api!.Id] = Now.AddMinutes(-3);

        var listed = await Listing().ExecuteAsync(TestContext.Current.CancellationToken);

        // One lookup per project and each inside its own: the fact is asked for
        // by project, because there is no reading across them.
        Assert.Equal([api.Id, web!.Id], _entries.Receipts);
        Assert.Equal(
            [Now.AddMinutes(-3), null],
            listed.Select(project => project.LastReceivedAt));
    }

    [Fact]
    public async Task A_project_that_has_never_received_anything_says_so_rather_than_a_time()
    {
        // Distinct from a project that received something long ago, which is
        // the difference an operator reads the column for. A project created a
        // minute ago has no receipt, and its creation time is not one.
        var project = await CreateAsync("api", 7);

        var listed = Assert.Single(await Listing().ExecuteAsync(
            TestContext.Current.CancellationToken));

        Assert.Null(listed.LastReceivedAt);
        Assert.Equal(project!.CreatedAt, listed.CreatedAt);
    }

    [Fact]
    public async Task One_project_is_read_back_by_the_identity_and_nothing_else()
    {
        var project = await CreateAsync("api", 7);

        var read = await new ReadProject(_projects).ExecuteAsync(
            project!.Id, TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, read?.Id);
        Assert.Null(await new ReadProject(_projects).ExecuteAsync(
            Guid.CreateVersion7(), TestContext.Current.CancellationToken));
    }

    private Task<Project?> CreateAsync(string name, int retentionDays) =>
        new CreateProject(_projects, _clock).ExecuteAsync(
            name, RetentionWindow.OfDays(retentionDays), TestContext.Current.CancellationToken);

    private Task<IssueAttempt> IssueAsync(Guid project) =>
        new IssueIngestToken(_projects, _tokens, new ReversingCipher(), _clock)
            .ExecuteAsync(project, TestContext.Current.CancellationToken);

    private ListProjects Listing() => new(_projects, _tokens, _entries);

    private RenameProject Renaming() => new(_projects);

    private ChangeRetentionWindow Changing() => new(_projects);

    private DeleteProject Deleting() => new(_projects);
}

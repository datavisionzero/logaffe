using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What the operator does to a group: make it, rename it, remove it, and move a
/// project into or out of it.
/// </summary>
/// <remarks>
/// A group carries a name and nothing else (ADR 0039), so most of what is
/// asserted here is what it does <i>not</i> do: removing one destroys no
/// project, renaming one moves none, and nothing about a group narrows a read.
/// </remarks>
public sealed class GroupActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly InMemoryGroups _groups = new();
    private readonly InMemoryTokens _tokens = new();
    private readonly RecordingReader _entries = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task A_made_group_is_a_name_and_an_identity()
    {
        var group = await MakingAsync("shop");

        Assert.NotNull(group);
        Assert.Equal("shop", group.Name);
        Assert.Equal(Now, group.CreatedAt);
        Assert.Equal(group.Id, Assert.Single(_groups.Stored).Id);
    }

    [Fact]
    public async Task A_second_group_by_that_name_is_refused_and_nothing_is_written()
    {
        await MakingAsync("shop");
        var writesBefore = _groups.Writes;

        Assert.Null(await MakingAsync("shop"));
        Assert.Single(_groups.Stored);
        Assert.Equal(writesBefore, _groups.Writes);
    }

    [Fact]
    public async Task A_group_is_empty_until_a_project_is_moved_into_it()
    {
        // Empty is an ordinary state and not a step that has not finished: the
        // operator made the heading before there was anything to put under it.
        var group = await MakingAsync("shop");

        var listed = Assert.Single(await Listing().ExecuteAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal(group!.Id, listed.Id);
        Assert.Equal(0, listed.Projects);
    }

    [Fact]
    public async Task The_identity_survives_a_rename_and_the_projects_do_not_move()
    {
        var group = await MakingAsync("shop");
        var project = await CreateAsync("api");
        await MoveAsync(project!.Id, group!.Id);

        Assert.Equal(
            RenameGroupOutcome.Renamed,
            await Renaming().ExecuteAsync(
                group.Id, "storefront", TestContext.Current.CancellationToken));

        // A project points at the identity rather than at the name, which is
        // the whole reason the identity is there.
        Assert.Equal("storefront", Assert.Single(_groups.Stored).Name);
        Assert.Equal(group.Id, Assert.Single(_projects.Stored).GroupId);
    }

    [Fact]
    public async Task A_rename_onto_another_groups_name_is_refused()
    {
        await MakingAsync("shop");
        var blog = await MakingAsync("blog");

        Assert.Equal(
            RenameGroupOutcome.NameTaken,
            await Renaming().ExecuteAsync(
                blog!.Id, "shop", TestContext.Current.CancellationToken));
        Assert.Equal("blog", _groups.Stored.Single(g => g.Id == blog.Id).Name);
    }

    [Fact]
    public async Task A_group_renamed_to_the_name_it_has_does_not_collide_with_itself()
    {
        var group = await MakingAsync("shop");

        Assert.Equal(
            RenameGroupOutcome.Renamed,
            await Renaming().ExecuteAsync(
                group!.Id, "shop", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Renaming_a_group_that_is_not_there_says_so() =>
        Assert.Equal(
            RenameGroupOutcome.NoSuchGroup,
            await Renaming().ExecuteAsync(
                Guid.CreateVersion7(), "shop", TestContext.Current.CancellationToken));

    [Fact]
    public async Task Removing_a_group_keeps_every_project_it_held()
    {
        var group = await MakingAsync("shop");
        var project = await CreateAsync("api");
        await MoveAsync(project!.Id, group!.Id);

        Assert.True(await Removing().ExecuteAsync(
            group.Id, TestContext.Current.CancellationToken));

        // The projects are left in no group by the foreign key, which is the
        // database's doing; what this act must not do is take them with it.
        Assert.Empty(_groups.Stored);
        Assert.Single(_projects.Stored);
        Assert.NotNull(await MakingAsync("shop"));
    }

    [Fact]
    public async Task Removing_a_group_that_is_already_gone_says_so_and_writes_nothing()
    {
        var group = await MakingAsync("shop");
        Assert.True(await Removing().ExecuteAsync(
            group!.Id, TestContext.Current.CancellationToken));
        var writesBefore = _groups.Writes;

        Assert.False(await Removing().ExecuteAsync(
            group.Id, TestContext.Current.CancellationToken));
        Assert.Equal(writesBefore, _groups.Writes);
    }

    [Fact]
    public async Task The_list_says_how_many_projects_each_group_holds()
    {
        var shop = await MakingAsync("shop");
        _clock.Now = Now.AddMinutes(1);
        var blog = await MakingAsync("blog");

        var api = await CreateAsync("api");
        var web = await CreateAsync("web");
        var loose = await CreateAsync("loose");

        await MoveAsync(api!.Id, shop!.Id);
        await MoveAsync(web!.Id, shop.Id);

        var listed = await Listing().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal([shop.Id, blog!.Id], listed.Select(group => group.Id));
        Assert.Equal([2, 0], listed.Select(group => group.Projects));

        // A project in no group is counted under no heading rather than under
        // one that does not exist.
        Assert.Null(_projects.Stored.Single(p => p.Id == loose!.Id).GroupId);
    }

    [Fact]
    public async Task A_moved_project_is_listed_under_the_group_and_changes_nothing_else()
    {
        var group = await MakingAsync("shop");
        var project = await CreateAsync("api");

        Assert.Equal(MoveProjectOutcome.Moved, await MoveAsync(project!.Id, group!.Id));

        var stored = Assert.Single(_projects.Stored);
        Assert.Equal(group.Id, stored.GroupId);
        Assert.Equal("api", stored.Name);
        Assert.Equal(project.Retention.Days, stored.Retention.Days);
    }

    [Fact]
    public async Task A_project_moves_back_out_to_no_group()
    {
        var group = await MakingAsync("shop");
        var project = await CreateAsync("api");
        await MoveAsync(project!.Id, group!.Id);

        Assert.Equal(MoveProjectOutcome.Moved, await MoveAsync(project.Id, null));
        Assert.Null(Assert.Single(_projects.Stored).GroupId);
    }

    [Fact]
    public async Task A_move_into_a_group_holding_that_name_is_refused()
    {
        var group = await MakingAsync("shop");
        var inside = await CreateAsync("api");
        await MoveAsync(inside!.Id, group!.Id);

        // The two projects called `api` the uniqueness exists to prevent would
        // otherwise be exactly what the operator is left looking at.
        var outside = await CreateAsync("api-2");
        await RenameAsync(outside!.Id, "api");

        Assert.Equal(MoveProjectOutcome.NameTaken, await MoveAsync(outside.Id, group.Id));
        Assert.Null(_projects.Stored.Single(p => p.Id == outside.Id).GroupId);
    }

    [Fact]
    public async Task A_move_to_the_group_a_project_is_already_in_is_not_a_collision()
    {
        // The operator opened the field and left it.
        var group = await MakingAsync("shop");
        var project = await CreateAsync("api");
        await MoveAsync(project!.Id, group!.Id);

        Assert.Equal(MoveProjectOutcome.Moved, await MoveAsync(project.Id, group.Id));
    }

    [Fact]
    public async Task A_move_into_a_group_that_is_not_there_says_so()
    {
        var project = await CreateAsync("api");

        Assert.Equal(
            MoveProjectOutcome.NoSuchGroup,
            await MoveAsync(project!.Id, Guid.CreateVersion7()));
        Assert.Null(Assert.Single(_projects.Stored).GroupId);
    }

    [Fact]
    public async Task Moving_a_project_that_is_not_there_says_so() =>
        Assert.Equal(
            MoveProjectOutcome.NoSuchProject,
            await MoveAsync(Guid.CreateVersion7(), null));

    [Fact]
    public async Task Two_projects_share_a_name_when_they_are_in_different_groups()
    {
        // `shop / api` beside `blog / api` names two different things wherever
        // either of them appears, which is what the group resolves.
        var shop = await MakingAsync("shop");
        var blog = await MakingAsync("blog");

        var one = await CreateAsync("api");
        await MoveAsync(one!.Id, shop!.Id);

        // A project is created in no group, where `api` is now free again, and
        // the second name only has to be free inside the group it lands in.
        var other = await CreateAsync("api");
        Assert.NotNull(other);
        await MoveAsync(other.Id, blog!.Id);

        Assert.Equal(
            [shop.Id, blog.Id],
            _projects.Stored.OrderBy(p => p.CreatedAt).Select(p => p.GroupId));
        Assert.Equal(["api", "api"], _projects.Stored.Select(p => p.Name));
    }

    [Fact]
    public async Task The_project_list_carries_the_group_a_project_is_in()
    {
        var group = await MakingAsync("shop");
        var project = await CreateAsync("api");
        await MoveAsync(project!.Id, group!.Id);

        var listed = Assert.Single(await new ListProjects(_projects, _tokens, _entries)
            .ExecuteAsync(TestContext.Current.CancellationToken));

        // The identity and not the name: the name is on the group list, which
        // is also where a group holding nothing is found.
        Assert.Equal(group.Id, listed.GroupId);
    }

    private Task<Group?> MakingAsync(string name) =>
        new CreateGroup(_groups, _clock).ExecuteAsync(name, TestContext.Current.CancellationToken);

    private Task<Project?> CreateAsync(string name) =>
        new CreateProject(_projects, _clock).ExecuteAsync(
            name, RetentionWindow.OfDays(7), TestContext.Current.CancellationToken);

    private Task<RenameOutcome> RenameAsync(Guid project, string name) =>
        new RenameProject(_projects).ExecuteAsync(
            project, name, TestContext.Current.CancellationToken);

    private Task<MoveProjectOutcome> MoveAsync(Guid project, Guid? group) =>
        new MoveProjectToGroup(_projects, _groups).ExecuteAsync(
            project, group, TestContext.Current.CancellationToken);

    private ListGroups Listing() => new(_groups, _projects);

    private RenameGroup Renaming() => new(_groups);

    private DeleteGroup Removing() => new(_groups);
}

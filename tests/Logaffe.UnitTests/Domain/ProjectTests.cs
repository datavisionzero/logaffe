using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Domain;

public sealed class ProjectTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_project_is_a_name_a_window_and_an_identity()
    {
        var project = Project.Create("api", RetentionWindow.OfDays(14), Now);

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("api", project.Name);
        Assert.Equal(14, project.Retention.Days);
        Assert.Equal(Now, project.CreatedAt);
    }

    [Fact]
    public void The_identity_survives_a_rename()
    {
        var project = Project.Create("api", RetentionWindow.OfDays(14), Now);
        var identity = project.Id;

        project.Rename("orders-api");

        Assert.Equal(identity, project.Id);
        Assert.Equal("orders-api", project.Name);
    }

    [Fact]
    public void A_project_is_in_no_group_until_it_is_moved_into_one()
    {
        var project = Project.Create("api", RetentionWindow.OfDays(14), Now);
        Assert.Null(project.GroupId);

        var group = Guid.CreateVersion7();
        project.MoveTo(group);
        Assert.Equal(group, project.GroupId);

        // And back out again, which destroys nothing either way.
        project.MoveTo(null);
        Assert.Null(project.GroupId);
    }

    [Fact]
    public void A_project_sits_on_no_host_until_it_is_put_on_one()
    {
        var project = Project.Create("api", RetentionWindow.OfDays(14), Now);
        Assert.Null(project.HostId);

        var host = Guid.CreateVersion7();
        project.RunsOn(host);
        Assert.Equal(host, project.HostId);

        // And back off again, which costs the project nothing but its band.
        project.RunsOn(null);
        Assert.Null(project.HostId);
    }

    /// <summary>
    /// Two separate facts about where a project is: one is where it is listed,
    /// the other is which machine it runs on, and neither moves the other.
    /// </summary>
    [Fact]
    public void The_group_and_the_host_do_not_disturb_each_other()
    {
        var project = Project.Create("api", RetentionWindow.OfDays(14), Now);
        var group = Guid.CreateVersion7();
        var host = Guid.CreateVersion7();

        project.MoveTo(group);
        project.RunsOn(host);
        Assert.Equal(group, project.GroupId);

        project.MoveTo(null);
        Assert.Equal(host, project.HostId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_project_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(
            () => Project.Create(name, RetentionWindow.OfDays(7), Now));

    [Fact]
    public void A_name_is_trimmed() =>
        Assert.Equal("api", Project.Create("  api  ", RetentionWindow.OfDays(7), Now).Name);

    [Fact]
    public void A_name_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => Project.Create(
            new string('x', Project.NameMaxLength + 1), RetentionWindow.OfDays(7), Now));
}

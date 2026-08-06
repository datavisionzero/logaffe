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

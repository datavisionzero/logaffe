using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Domain;

public sealed class GroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_group_is_a_name_and_an_identity()
    {
        var group = Group.Create("shop", Now);

        Assert.NotEqual(Guid.Empty, group.Id);
        Assert.Equal("shop", group.Name);
        Assert.Equal(Now, group.CreatedAt);
    }

    [Fact]
    public void The_identity_survives_a_rename()
    {
        // It is what a project points at, so that renaming a group moves none
        // of them (ADR 0039).
        var group = Group.Create("shop", Now);
        var identity = group.Id;

        group.Rename("storefront");

        Assert.Equal(identity, group.Id);
        Assert.Equal("storefront", group.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_group_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(() => Group.Create(name, Now));

    [Fact]
    public void A_name_is_trimmed() => Assert.Equal("shop", Group.Create("  shop  ", Now).Name);

    [Fact]
    public void A_name_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(
            () => Group.Create(new string('x', Group.NameMaxLength + 1), Now));
}

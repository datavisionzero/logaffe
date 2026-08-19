using Logaffe.Domain.Hosts;

namespace Logaffe.UnitTests.Domain;

public sealed class HostTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_host_is_a_name_and_an_identity()
    {
        var host = Host.Create("hetzner-1", Now);

        Assert.NotEqual(Guid.Empty, host.Id);
        Assert.Equal("hetzner-1", host.Name);
        Assert.Equal(Now, host.CreatedAt);
    }

    [Fact]
    public void The_identity_survives_a_rename()
    {
        var host = Host.Create("hetzner-1", Now);
        var identity = host.Id;

        host.Rename("web-1");

        Assert.Equal(identity, host.Id);
        Assert.Equal("web-1", host.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_host_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(() => Host.Create(name, Now));

    [Fact]
    public void A_name_is_trimmed() => Assert.Equal("web-1", Host.Create("  web-1  ", Now).Name);

    [Fact]
    public void A_name_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(
            () => Host.Create(new string('x', Host.NameMaxLength + 1), Now));
}

using Logaffe.Domain.Hosts;

namespace Logaffe.UnitTests.Domain;

public sealed class MountPathTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/var")]
    [InlineData("/var/lib/docker")]
    public void An_absolute_path_is_a_mount_path(string value) =>
        Assert.Equal(value, MountPath.Create(value).Value);

    [Fact]
    public void A_path_is_trimmed() => Assert.Equal("/var", MountPath.Create("  /var  ").Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("var/lib")]
    [InlineData("C:\\data")]
    public void What_is_not_an_absolute_path_is_refused(string? value) =>
        Assert.False(MountPath.TryCreate(value, out _));

    /// <summary>
    /// The one string in the sample shape, and the reason ADR 0045 can hand a
    /// sample to an agent without an entry's care. A control character in it
    /// would be the column quietly becoming somewhere to put arbitrary text.
    /// </summary>
    [Theory]
    [InlineData("/var\nlib")]
    [InlineData("/var\0lib")]
    [InlineData("/var\tlib")]
    public void A_mount_path_carries_nothing_a_path_does_not(string value) =>
        Assert.False(MountPath.TryCreate(value, out _));

    [Fact]
    public void A_path_that_will_not_fit_the_key_is_refused() =>
        Assert.False(MountPath.TryCreate("/" + new string('x', MountPath.MaxLength), out _));
}

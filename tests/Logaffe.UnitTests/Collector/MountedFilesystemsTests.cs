using Logaffe.Collector;

namespace Logaffe.UnitTests.Collector;

/// <summary>
/// The filesystems a collector was asked to measure, and where it looks for
/// them.
/// </summary>
public sealed class MountedFilesystemsTests
{
    [Theory]
    // The root of the machine is the root of the mount, and not a directory
    // inside it.
    [InlineData("/", "/rootfs")]
    [InlineData("/data", "/rootfs/data")]
    [InlineData("/var/lib/docker", "/rootfs/var/lib/docker")]
    // A path an operator typed with a slash on the end is the same path.
    [InlineData("/data/", "/rootfs/data")]
    public void A_path_on_the_machine_is_reached_under_the_root_the_container_sees(
        string mount, string at) =>
        Assert.Equal(at, MountedFilesystems.Under("/rootfs", mount));

    [Fact]
    public void A_filesystem_is_reported_under_the_name_the_operator_gave_it()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        var filesystem = Assert.Single(new MountedFilesystems(root, ["/"]).Read());

        // `/`, not `/rootfs` — what the operator asked about is a path on their
        // machine, and the bind mount is this collector's business.
        Assert.Equal("/", filesystem.Mount);
    }

    [Fact]
    public void A_path_that_is_not_a_mount_point_is_measured_as_the_filesystem_it_sits_on()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "data"));

        var filesystem = Assert.Single(new MountedFilesystems(root, ["/data"]).Read());

        // Which is what makes the whole arrangement work: inside the container
        // `/rootfs/data` is either the machine's separate disk or the disk that
        // holds it, and either answer is the truthful one about that path.
        Assert.True(filesystem.Total > 0);
        Assert.InRange(filesystem.Used, 0, filesystem.Total);
    }

    [Fact]
    public void A_mount_that_is_not_there_leaves_the_rest_of_the_reading_alone()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        // A disk not mounted yet, or a path typed with a trailing word. The
        // machine is still worth reporting, and the band draws the missing
        // track as the gap it is.
        var read = new MountedFilesystems(root, ["/", "/nowhere"]).Read();

        Assert.Equal(["/"], read.Select(one => one.Mount));
    }

    [Fact]
    public void Watching_nothing_is_measuring_nothing_and_not_discovering_something()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        // It enumerates no mounts, ever: a machine that mounts forty container
        // overlays does not silently become forty rows a minute.
        Assert.Empty(new MountedFilesystems(root, []).Read());
    }
}

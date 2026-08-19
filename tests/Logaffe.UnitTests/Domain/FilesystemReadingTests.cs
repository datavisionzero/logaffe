using Logaffe.Domain.Hosts;

namespace Logaffe.UnitTests.Domain;

public sealed class FilesystemReadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static FilesystemReading Reading(long used = 41_234_567_890, long total = 107_374_182_400) =>
        new()
        {
            HostId = Guid.CreateVersion7(),
            ReceiptTime = Now,
            MountPath = MountPath.Create("/"),
            Used = used,
            Total = total,
        };

    [Fact]
    public void A_reading_is_a_mount_and_two_numbers()
    {
        var reading = Reading();

        Assert.Equal("/", reading.MountPath.Value);
        Assert.Equal(41_234_567_890, reading.Used);
        Assert.Equal(107_374_182_400, reading.Total);
        Assert.Equal(Now, reading.ReceiptTime);
    }

    [Fact]
    public void Bytes_in_use_are_not_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(used: -1));

    [Fact]
    public void Bytes_the_filesystem_holds_are_not_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(total: -1));
}

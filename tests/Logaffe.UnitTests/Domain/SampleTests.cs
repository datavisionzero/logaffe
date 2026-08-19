using Logaffe.Domain.Hosts;

namespace Logaffe.UnitTests.Domain;

public sealed class SampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AHost = Guid.CreateVersion7();

    private static Sample Reading(
        double cpu = 0.42,
        long memoryUsed = 6_115_295_232,
        long memoryTotal = 16_769_712_128,
        double load1 = 0.52) =>
        new()
        {
            HostId = AHost,
            ReceiptTime = Now,
            Cpu = cpu,
            MemoryUsed = memoryUsed,
            MemoryTotal = memoryTotal,
            Load1 = load1,
            Load5 = 0.61,
            Load15 = 0.58,
        };

    [Fact]
    public void A_sample_is_the_closed_set_of_numbers_and_one_clock()
    {
        var sample = Reading();

        Assert.Equal(AHost, sample.HostId);
        Assert.Equal(Now, sample.ReceiptTime);
        Assert.Equal(0.42, sample.Cpu);
        Assert.Equal(6_115_295_232, sample.MemoryUsed);
        Assert.Equal(16_769_712_128, sample.MemoryTotal);
    }

    /// <summary>
    /// A share of an interval, so nought to one. An entry over its cap is
    /// truncated because the ones that overrun are the ones worth having
    /// (ADR 0008); a number outside its range is not a large reading, it is not
    /// a reading, and the next one is a minute away.
    /// </summary>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_processor_share_is_between_nought_and_one(double cpu) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(cpu: cpu));

    [Fact]
    public void The_ends_of_the_range_are_readings() =>
        Assert.Equal(1, Reading(cpu: 1).Cpu);

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Memory_in_use_is_not_negative(long used) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(memoryUsed: used));

    [Fact]
    public void Memory_the_machine_has_is_not_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(memoryTotal: -1));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void A_load_average_is_not_negative(double load) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(load1: load));

    /// <summary>
    /// A machine with more load than processors is the ordinary way to be busy,
    /// so nothing caps this from above.
    /// </summary>
    [Fact]
    public void A_load_average_has_no_ceiling() => Assert.Equal(64, Reading(load1: 64).Load1);
}

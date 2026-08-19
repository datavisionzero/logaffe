using Logaffe.Domain.Hosts;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The bucketed read of a host's samples, against the Postgres an installation
/// runs.
/// </summary>
/// <remarks>
/// The bucketing is date arithmetic done in the database over a span the caller
/// chose, and every mistake available in it compiles: a bucket off by one, an
/// average taken over the wrong rows, a peak that is really a last value, or a
/// span that collapses every reading into one. So it is asked of a real table.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class SampleReaderTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ten = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _host = Guid.CreateVersion7();
    private readonly Guid _other = Guid.CreateVersion7();

    [Fact]
    public async Task A_range_with_nothing_in_it_is_an_answer_and_not_an_absence()
    {
        var reader = await ReadingAsync();

        var window = await reader.ReadAsync(
            _host, Ten, Ten.AddHours(1), BucketCount.Of(4),
            TestContext.Current.CancellationToken);

        // A machine that was switched off reported nothing, which is a fact. The
        // band draws the gap rather than drawing through it.
        Assert.Empty(window.Samples);
        Assert.Empty(window.Filesystems);
    }

    [Fact]
    public async Task Every_reading_in_a_span_becomes_one_bucket_carrying_its_average_and_peak()
    {
        var reader = await ReadingAsync(
            Sample(Ten, cpu: 0.10),
            Sample(Ten.AddMinutes(1), cpu: 0.90),
            Sample(Ten.AddMinutes(2), cpu: 0.20));

        var window = await reader.ReadAsync(
            _host, Ten, Ten.AddMinutes(3), BucketCount.Of(1),
            TestContext.Current.CancellationToken);

        var bucket = Assert.Single(window.Samples);

        Assert.Equal(0.40, bucket.CpuAverage, 3);

        // The whole reason a peak is stored beside an average: the minute at the
        // ceiling is what somebody went looking for, and the mean hides it.
        Assert.Equal(0.90, bucket.CpuPeak, 3);
        Assert.Equal(Ten, bucket.Start);
    }

    [Fact]
    public async Task A_span_the_machine_reported_nothing_in_is_missing_rather_than_zero()
    {
        var reader = await ReadingAsync(
            Sample(Ten, cpu: 0.5),

            // Nothing in the second span at all.
            Sample(Ten.AddMinutes(20), cpu: 0.5));

        var window = await reader.ReadAsync(
            _host, Ten, Ten.AddMinutes(30), BucketCount.Of(3),
            TestContext.Current.CancellationToken);

        // Two buckets and not three, and the starts say which two they are. A
        // bucket carrying zeroes would say the machine reported nought per cent
        // of a processor.
        Assert.Equal(
            [Ten, Ten.AddMinutes(20)],
            window.Samples.Select(bucket => bucket.Start));
    }

    [Fact]
    public async Task The_size_of_the_machine_is_taken_whole_rather_than_averaged()
    {
        var reader = await ReadingAsync(
            Sample(Ten, memoryUsed: 1_000, memoryTotal: 8_000),
            Sample(Ten.AddMinutes(1), memoryUsed: 3_000, memoryTotal: 16_000));

        var window = await reader.ReadAsync(
            _host, Ten, Ten.AddMinutes(2), BucketCount.Of(1),
            TestContext.Current.CancellationToken);

        var bucket = Assert.Single(window.Samples);

        Assert.Equal(2_000, bucket.MemoryUsedAverage);
        Assert.Equal(3_000, bucket.MemoryUsedPeak);

        // How large the machine is rather than how much of it was in use, so a
        // mean of it would only ever be an artefact of a resize mid-span.
        Assert.Equal(16_000, bucket.MemoryTotal);
    }

    [Fact]
    public async Task Each_filesystem_is_bucketed_on_its_own()
    {
        var reader = await ReadingAsync(Sample(Ten));

        await WriteFilesystemsAsync(
            Filesystem(Ten, "/", used: 100, total: 1_000),
            Filesystem(Ten, "/data", used: 200, total: 4_000));

        var window = await reader.ReadAsync(
            _host, Ten, Ten.AddMinutes(1), BucketCount.Of(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [("/", 100L), ("/data", 200L)],
            window.Filesystems.Select(
                bucket => (bucket.MountPath.Value, bucket.UsedAverage)));
    }

    [Fact]
    public async Task A_read_names_one_host_and_reaches_no_other()
    {
        var reader = await ReadingAsync(Sample(Ten, cpu: 0.5));

        await WriteAsync(new Sample
        {
            HostId = _other,
            ReceiptTime = Ten,
            Cpu = 0.9,
            MemoryUsed = 1,
            MemoryTotal = 2,
            Load1 = 0,
            Load5 = 0,
            Load15 = 0,
        });

        var window = await reader.ReadAsync(
            _host, Ten, Ten.AddMinutes(1), BucketCount.Of(1),
            TestContext.Current.CancellationToken);

        // Samples may be read across projects (ADR 0045) and never across
        // machines: a band drawn from two hosts' numbers is a band about nothing.
        Assert.Equal(0.5, Assert.Single(window.Samples).CpuAverage, 3);
    }

    [Fact]
    public async Task When_each_host_last_reported_is_read_off_its_newest_sample()
    {
        var reader = await ReadingAsync(
            Sample(Ten), Sample(Ten.AddMinutes(5)), Sample(Ten.AddMinutes(2)));

        var reported = await reader.LastReportedAsync(TestContext.Current.CancellationToken);

        // One grouped statement over every host, and a host that never reported
        // is absent rather than null — there is no row to read it off.
        Assert.Equal(Ten.AddMinutes(5), Assert.Single(reported).Value);
        Assert.False(reported.ContainsKey(_other));
    }

    private string _connectionString = null!;

    private async Task<SampleReader> ReadingAsync(params Sample[] samples)
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = ContextFor(_connectionString))
        {
            await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
                .ApplyAsync(TestContext.Current.CancellationToken);
        }

        await WriteHostsAsync();
        await WriteAsync(samples);

        return new SampleReader(ContextFor(_connectionString));
    }

    private async Task WriteHostsAsync()
    {
        await using var context = ContextFor(_connectionString);

        context.Hosts.Add(HostRow(_host, "web-01"));
        context.Hosts.Add(HostRow(_other, "web-02"));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task WriteAsync(params Sample[] samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        await using var context = ContextFor(_connectionString);

        context.Samples.AddRange(samples);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task WriteFilesystemsAsync(params FilesystemReading[] readings)
    {
        await using var context = ContextFor(_connectionString);

        context.FilesystemReadings.AddRange(readings);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Sample Sample(
        DateTimeOffset at,
        double cpu = 0.5,
        long memoryUsed = 1_000,
        long memoryTotal = 8_000) => new()
    {
        HostId = _host,
        ReceiptTime = at,
        Cpu = cpu,
        MemoryUsed = memoryUsed,
        MemoryTotal = memoryTotal,
        Load1 = 1,
        Load5 = 1,
        Load15 = 1,
    };

    private FilesystemReading Filesystem(
        DateTimeOffset at, string mount, long used, long total) => new()
    {
        HostId = _host,
        ReceiptTime = at,
        MountPath = MountPath.Create(mount),
        Used = used,
        Total = total,
    };

    private static Logaffe.Domain.Hosts.Host HostRow(Guid id, string name)
    {
        var host = Logaffe.Domain.Hosts.Host.Create(name, Ten);

        // The identity is the reader's argument, so the row has to carry the one
        // the test is going to ask with.
        typeof(Logaffe.Domain.Hosts.Host)
            .GetProperty(nameof(Logaffe.Domain.Hosts.Host.Id))!
            .SetValue(host, id);

        return host;
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}

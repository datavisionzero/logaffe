using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Queries;

using Host = Logaffe.Domain.Hosts.Host;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The host table, in memory, in the one way the footprint turns on: it lists
/// the machines this installation collects from.
/// </summary>
internal sealed class InMemoryHosts : IHosts
{
    private readonly List<Host> _hosts = [];

    /// <summary>A host that is already there when the act runs.</summary>
    public Host Holding(string name, DateTimeOffset createdAt)
    {
        var host = Host.Create(name, createdAt);
        _hosts.Add(host);

        return host;
    }

    public Task<IReadOnlyList<Host>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Host>>([.. _hosts.OrderBy(host => host.CreatedAt)]);

    public Task<Host?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_hosts.SingleOrDefault(host => host.Id == id));

    public Task<Host?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_hosts.SingleOrDefault(host => host.Name == name));

    public Task AddAsync(Host host, CancellationToken cancellationToken)
    {
        _hosts.Add(host);

        return Task.CompletedTask;
    }

    public Task RecordAsync(Host host, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RemoveAsync(Host host, CancellationToken cancellationToken)
    {
        _hosts.Remove(host);

        return Task.CompletedTask;
    }
}

/// <summary>
/// What the machines last said, as the reader hands it over: the newest report
/// of each host that has one, and nothing for the hosts that never reported.
/// </summary>
internal sealed class StubSampleReader : ISampleReader
{
    private readonly Dictionary<Guid, NewestReport> _reports = [];

    /// <summary>Which hosts were asked about, in the order they were asked.</summary>
    public List<IReadOnlyList<Guid>> Asked { get; } = [];

    /// <summary>A machine reporting one filesystem, as full as it is said to be.</summary>
    public void Reporting(
        Guid hostId, DateTimeOffset at, string mount, long used, long total) =>
        Reporting(hostId, at, [Reading(hostId, at, mount, used, total)]);

    public void Reporting(
        Guid hostId, DateTimeOffset at, IReadOnlyList<FilesystemReading> filesystems) =>
        _reports[hostId] = new NewestReport(hostId, at, filesystems);

    public static FilesystemReading Reading(
        Guid hostId, DateTimeOffset at, string mount, long used, long total) =>
        new()
        {
            HostId = hostId,
            ReceiptTime = at,
            MountPath = MountPath.Create(mount),
            Used = used,
            Total = total,
        };

    public Task<IReadOnlyList<NewestReport>> NewestReportsAsync(
        IReadOnlyList<Guid> hostIds, CancellationToken cancellationToken)
    {
        Asked.Add(hostIds);

        return Task.FromResult<IReadOnlyList<NewestReport>>(
        [
            .. hostIds
                .Where(_reports.ContainsKey)
                .Select(hostId => _reports[hostId]),
        ]);
    }

    public Task<SampleWindow> ReadAsync(
        Guid hostId,
        DateTimeOffset from,
        DateTimeOffset to,
        BucketCount buckets,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SampleWindow([], []));

    public Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastReportedAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateTimeOffset>>(
            _reports.ToDictionary(pair => pair.Key, pair => pair.Value.ReceiptTime));
}

/// <summary>What the store says it occupies, and how often it was asked.</summary>
internal sealed class StubStoreFootprint : IStoreFootprint
{
    public long Held { get; set; } = 12L * 1024 * 1024 * 1024;

    public int Reads { get; private set; }

    public Task<long> HeldBytesAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Held);
    }
}

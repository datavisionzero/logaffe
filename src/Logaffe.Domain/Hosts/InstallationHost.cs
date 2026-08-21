namespace Logaffe.Domain.Hosts;

/// <summary>
/// The machine logaffe is itself on, and which of that machine's filesystems
/// holds the database.
/// </summary>
/// <remarks>
/// <para>
/// The project's relation to a host pointed the other way: this one is about the
/// installation rather than about what it stores (<c>docs/metrics.md</c>). It
/// exists so that the installation can read how full its own disk is off numbers
/// that already exist — the footprint a retention window is chosen against
/// (ADR 0048), and the condition that says the store is filling up (ADR 0050) —
/// and it exists for that and nothing else.
/// </para>
/// <para>
/// <b>It names a mount as well as a machine</b>, because a machine has several
/// filesystems and only one of them holds the database. The mounts are named in
/// that host's collector configuration, so the operator picks from what the host
/// already reports rather than typing a path — which also means the mount named
/// here can go missing from what arrives, and the numbers that read it are
/// absent rather than wrong when it does.
/// </para>
/// <para>
/// <b>Having none is the ordinary case.</b> An installation that names no host
/// is not a degraded one: it is every installation until the operator decides
/// they want the disk read. The host is still not a scope, and this relation
/// appears in no query.
/// </para>
/// </remarks>
public sealed record InstallationHost(Guid HostId, MountPath Mount);

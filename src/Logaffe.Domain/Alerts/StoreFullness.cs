namespace Logaffe.Domain.Alerts;

/// <summary>
/// How full the filesystem the installation's database sits on is, or why
/// nothing can be said about it.
/// </summary>
/// <remarks>
/// <para>
/// It is read off the samples that already exist — the installation names the
/// host it runs on and the mount on that host, and this is <c>used / total</c>
/// from that host's newest filesystem reading (<c>docs/metrics.md</c>). Nothing
/// new is collected and nothing is asked of Postgres: a machine that reports its
/// filesystems every minute is already saying this, and a disk size the operator
/// typed in once would be a number that goes stale without anyone being told.
/// </para>
/// <para>
/// <b>It is the newest reading and not an average of recent ones</b>, for the
/// reason the footprint's is: how full a disk is is a level rather than a rate.
/// </para>
/// </remarks>
public sealed record StoreFullness
{
    /// <summary>The first thing worth saying about a disk.</summary>
    public const int FirstThreshold = 85;

    /// <summary>
    /// The second, which is worth saying while the first is still latched — a
    /// disk that has gone from one to the other is filling rather than full.
    /// </summary>
    public const int SecondThreshold = 95;

    private StoreFullness(Blindness blindness) => Blindness = blindness;

    private StoreFullness(Guid hostId, string hostName, long used, long total)
    {
        HostId = hostId;
        HostName = hostName;
        Used = used;
        Total = total;
    }

    /// <summary>
    /// What stands between this and a reading, or <see cref="Alerts.Blindness.None"/>
    /// when nothing does.
    /// </summary>
    public Blindness Blindness { get; }

    /// <summary>The machine the installation says it sits on.</summary>
    public Guid HostId { get; }

    /// <summary>
    /// What the operator calls that machine, which is what an alert carries.
    /// </summary>
    public string HostName { get; } = string.Empty;

    public long Used { get; }

    public long Total { get; }

    /// <summary>
    /// How full the mount is, in whole per cent, rounded down.
    /// </summary>
    /// <remarks>
    /// A filesystem keeps blocks back for the superuser, so this is what the
    /// machine reports as used against what it reports as held rather than
    /// anything derived from what is free.
    /// </remarks>
    public int Percent => Total <= 0 ? 0 : (int)(Used * 100 / Total);

    /// <summary>
    /// The highest threshold this reading is at or above, and
    /// <see cref="Alerting.Clear"/> when it is below both or there is no reading.
    /// </summary>
    public int Crossed => Blindness is not Blindness.None
        ? Alerting.Clear
        : Percent >= SecondThreshold
            ? SecondThreshold
            : Percent >= FirstThreshold
                ? FirstThreshold
                : Alerting.Clear;

    /// <summary>What the named mount last said about itself.</summary>
    public static StoreFullness Of(Guid hostId, string hostName, long used, long total) =>
        new(hostId, hostName, used, total);

    /// <summary>A condition that is switched on and cannot see.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="blindness"/> is <see cref="Alerts.Blindness.None"/>,
    /// which is not a reason — a condition that can see has a reading.
    /// </exception>
    public static StoreFullness Blind(Blindness blindness) =>
        blindness is Blindness.None
            ? throw new ArgumentOutOfRangeException(
                nameof(blindness), blindness, "Being blind has a reason.")
            : new StoreFullness(blindness);
}

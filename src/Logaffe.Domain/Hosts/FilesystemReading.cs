namespace Logaffe.Domain.Hosts;

/// <summary>
/// How full one of a host's filesystems was, at the moment its
/// <see cref="Sample"/> was taken.
/// </summary>
/// <remarks>
/// It is a row of its own rather than a member of the sample because a machine
/// has several filesystems and one processor: folding the two together would
/// make the shape of a stored reading depend on how the collector that sent it
/// happened to be configured, which is the thing the closed schema exists to
/// prevent (ADR 0044).
/// </remarks>
public sealed class FilesystemReading
{
    private readonly long _used;
    private readonly long _total;

    /// <summary>The host this was read off.</summary>
    public required Guid HostId { get; init; }

    /// <summary>
    /// The moment of the sample this belongs to, carried rather than referenced:
    /// with <see cref="HostId"/> and <see cref="MountPath"/> it is the key, and a
    /// reading is only ever read over a range of it.
    /// </summary>
    public required DateTimeOffset ReceiptTime { get; init; }

    /// <summary>
    /// Which filesystem, by the path the operator named in their collector's
    /// configuration.
    /// </summary>
    public required MountPath MountPath { get; init; }

    /// <summary>Bytes in use.</summary>
    public required long Used
    {
        get => _used;
        init => _used = NotNegative(value, nameof(Used));
    }

    /// <summary>Bytes the filesystem holds.</summary>
    public required long Total
    {
        get => _total;
        init => _total = NotNegative(value, nameof(Total));
    }

    private static long NotNegative(long value, string name) =>
        value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                name, value, $"A filesystem reading's {name} is not negative.");
}

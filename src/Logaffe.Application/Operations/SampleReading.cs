using System.Text.Json;
using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Operations;

/// <summary>
/// One filesystem as a delivery gave it.
/// </summary>
public sealed record ReadFilesystem(MountPath MountPath, long Used, long Total);

/// <summary>
/// One delivery, read: everything a sample carries except the two things the
/// installation supplies rather than the collector — the host, and the clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>One reading per delivery, and never a batch.</b> A collector buffers
/// nothing, retries nothing and has nothing to catch up on, so there is no
/// second reading for a delivery to carry. This is where samples part company
/// with entries, whose batching exists because an application produces them
/// faster than it should open connections.
/// </para>
/// <para>
/// <b>There is no timestamp on the wire.</b> That is the single clock made
/// visible: the installation stamps the sample when it arrives, and a field for
/// the collector's own clock would be a field somebody eventually trusts.
/// </para>
/// <para>
/// <b>A member this does not know is passed over.</b> There is nowhere to store
/// one (ADR 0044), and that is also what makes the format additive: an older
/// collector omitting a number added later delivers a sample that lacks it, and
/// a newer one sending a number this installation has never heard of is read by
/// the part it does know. A collector must keep working across an upgrade of the
/// installation it reports to, because the alternative turns
/// <c>docker compose pull</c> into a silent stop of every machine's reporting.
/// </para>
/// <para>
/// <b>It is read whole or not at all</b>, unlike a line of a batch (ADR 0006).
/// Partial acceptance exists so that one broken line does not cost the other
/// nine hundred and ninety-nine; one reading has no other lines to protect, and
/// half a sample — memory without processor — is a band with a hole in it that
/// looks like data.
/// </para>
/// </remarks>
public sealed record SampleReading(
    double Cpu,
    long MemoryUsed,
    long MemoryTotal,
    double Load1,
    double Load5,
    double Load15,
    IReadOnlyList<ReadFilesystem> Filesystems)
{
    private const string CpuKey = "cpu";
    private const string MemoryUsedKey = "memoryUsed";
    private const string MemoryTotalKey = "memoryTotal";
    private const string Load1Key = "load1";
    private const string Load5Key = "load5";
    private const string Load15Key = "load15";
    private const string FilesystemsKey = "filesystems";
    private const string MountKey = "mount";
    private const string UsedKey = "used";
    private const string TotalKey = "total";

    /// <summary>
    /// Reads a delivery, or says in one sentence why it is not a reading.
    /// </summary>
    /// <remarks>
    /// The reason is for the person wiring a collector up with <c>curl</c> and
    /// for nobody else — a collector neither waits for it nor looks at it. It
    /// names the member at fault, because "not a reading" sends somebody
    /// reading their own JSON character by character.
    /// </remarks>
    public static bool TryRead(
        ReadOnlySpan<byte> body, out SampleReading reading, out string reason)
    {
        reading = null!;
        reason = string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                body.ToArray(),
                new JsonDocumentOptions { MaxDepth = 3 });
        }
        catch (JsonException)
        {
            reason = "The body is not JSON.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                reason = "A sample is a JSON object.";
                return false;
            }

            if (!TryNumber(root, CpuKey, out var cpu, out reason)
                || !TryInteger(root, MemoryUsedKey, out var memoryUsed, out reason)
                || !TryInteger(root, MemoryTotalKey, out var memoryTotal, out reason)
                || !TryNumber(root, Load1Key, out var load1, out reason)
                || !TryNumber(root, Load5Key, out var load5, out reason)
                || !TryNumber(root, Load15Key, out var load15, out reason)
                || !TryFilesystems(root, out var filesystems, out reason))
            {
                return false;
            }

            // The bounds are the domain's, and they are asked here rather than
            // thrown from the constructor because this is the one place that
            // owes the sender a sentence.
            if (!IsShare(cpu))
            {
                reason = $"'{CpuKey}' is a share of the interval, between 0 and 1.";
                return false;
            }

            if (memoryUsed < 0 || memoryTotal < 0)
            {
                reason = $"'{MemoryUsedKey}' and '{MemoryTotalKey}' are not negative.";
                return false;
            }

            if (!IsLoad(load1) || !IsLoad(load5) || !IsLoad(load15))
            {
                reason = "A load average is a finite number and is not negative.";
                return false;
            }

            reading = new SampleReading(
                cpu, memoryUsed, memoryTotal, load1, load5, load15, filesystems);
            return true;
        }
    }

    private static bool IsShare(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool IsLoad(double value) => double.IsFinite(value) && value >= 0;

    private static bool TryNumber(
        JsonElement root, string key, out double value, out string reason)
    {
        value = 0;
        reason = string.Empty;

        if (!root.TryGetProperty(key, out var member))
        {
            reason = $"A sample carries '{key}'.";
            return false;
        }

        if (member.ValueKind is not JsonValueKind.Number || !member.TryGetDouble(out value))
        {
            reason = $"'{key}' is a number.";
            return false;
        }

        return true;
    }

    private static bool TryInteger(
        JsonElement root, string key, out long value, out string reason)
    {
        value = 0;
        reason = string.Empty;

        if (!root.TryGetProperty(key, out var member))
        {
            reason = $"A sample carries '{key}'.";
            return false;
        }

        if (member.ValueKind is not JsonValueKind.Number || !member.TryGetInt64(out value))
        {
            reason = $"'{key}' is a whole number of bytes.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The filesystems, of which there may be none: a collector told to watch
    /// nothing still reports the machine.
    /// </summary>
    private static bool TryFilesystems(
        JsonElement root, out IReadOnlyList<ReadFilesystem> filesystems, out string reason)
    {
        filesystems = [];
        reason = string.Empty;

        if (!root.TryGetProperty(FilesystemsKey, out var member))
        {
            return true;
        }

        if (member.ValueKind is not JsonValueKind.Array)
        {
            reason = $"'{FilesystemsKey}' is an array.";
            return false;
        }

        if (member.GetArrayLength() > Sampling.FilesystemsPerSample)
        {
            reason =
                $"A sample carries at most {Sampling.FilesystemsPerSample} filesystems.";
            return false;
        }

        var read = new List<ReadFilesystem>(member.GetArrayLength());
        var mounts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in member.EnumerateArray())
        {
            if (element.ValueKind is not JsonValueKind.Object
                || !element.TryGetProperty(MountKey, out var mount)
                || mount.ValueKind is not JsonValueKind.String
                || !MountPath.TryCreate(mount.GetString(), out var path))
            {
                reason = $"A filesystem carries '{MountKey}', an absolute path.";
                return false;
            }

            if (!TryInteger(element, UsedKey, out var used, out reason)
                || !TryInteger(element, TotalKey, out var total, out reason))
            {
                return false;
            }

            if (used < 0 || total < 0)
            {
                reason = $"'{UsedKey}' and '{TotalKey}' are not negative.";
                return false;
            }

            // The mount is half the key, so two readings of one path in one
            // delivery are two rows that cannot both exist. Refused here rather
            // than resolved, because which of the two the operator meant is not
            // this act's to guess.
            if (!mounts.Add(path.Value))
            {
                reason = $"'{path.Value}' is given twice.";
                return false;
            }

            read.Add(new ReadFilesystem(path, used, total));
        }

        filesystems = read;
        return true;
    }
}

using Logaffe.Domain.Storage;

namespace Logaffe.Api.Http;

/// <summary>
/// What a retention window costs, read before it is applied.
/// </summary>
/// <remarks>
/// <para>
/// The same three numbers on both screens that set a window, because the
/// operator is deciding about one disk (ADR 0048). Two of them are the
/// installation's and do not move with the field; the third does, which is why
/// the whole answer is one route rather than a fixed part and a live one.
/// </para>
/// <para>
/// <b>Nothing here is a refusal or a threshold.</b> There is no flag saying the
/// window is too large, because no window is: the arithmetic is advisory, and
/// what the screen makes of three numbers is the screen's.
/// </para>
/// </remarks>
/// <param name="RetentionDays">
/// The window that was asked about, echoed back so that an answer arriving after
/// the operator has moved the field on is recognizable as the answer to the
/// question it was — the arrangement the count of what a lowering removes
/// already has.
/// </param>
/// <param name="HeldBytes">
/// What the whole database holds this moment: every table and every index, not
/// the entries alone, because the disk does not distinguish them.
/// </param>
/// <param name="ImpliedBytes">
/// What this window will hold in steady state, or <c>null</c> when there is
/// nothing to work it out from — a project with less than a fortnight of history
/// behind it, or an installation no host has ever reported to.
/// </param>
/// <param name="DiskFreeBytes">
/// What the filesystem under the database has left, or <c>null</c> when this
/// installation names no host, the host it names is not reporting, or the mount
/// it names is not among what arrives.
/// </param>
/// <param name="DiskTotalBytes">
/// How large that filesystem is, absent exactly when
/// <paramref name="DiskFreeBytes"/> is. It rides beside it because how full a
/// disk is and how large it is answer different questions.
/// </param>
public sealed record FootprintResponse(
    int RetentionDays,
    long HeldBytes,
    long? ImpliedBytes,
    long? DiskFreeBytes,
    long? DiskTotalBytes)
{
    public static FootprintResponse Of(int retentionDays, Footprint footprint) => new(
        retentionDays,
        footprint.Held,
        footprint.Implied,
        footprint.Disk?.Free,
        footprint.Disk?.Total);
}

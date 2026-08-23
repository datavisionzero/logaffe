using Logaffe.Domain.Alerts;

namespace Logaffe.Infrastructure.Alerts;

/// <summary>
/// The notification itself: a title, a sentence of numbers, and somewhere to
/// tap.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the place ADR 0049 is checkable.</b> What composes a notification
/// takes an <see cref="Alert"/> — names and numbers, with nowhere in it for a
/// rendered message, an exception, a property value, a logger name or an
/// instance — so the rule about what leaves the installation is a property of
/// what this code can reach rather than a discipline it remembers. There is no
/// setting here, no template, and no flag that turns content on.
/// </para>
/// <para>
/// <b>The four read as four sentences and not as a format.</b> "Seven hours
/// silent against five tolerated" and "twelve thousand entries against a usual
/// three hundred" are different facts, and a shared template would have made
/// them one shape with the numbers relabelled. The two rate conditions are the
/// case that proves it: they run the same arithmetic and they do not say the
/// same sentence, because one is about how much a project wrote and the other
/// is about how much of it went wrong.
/// </para>
/// <para>
/// No priority, no tags and no severity ride along: every alert is the same
/// weight on the way out, because a per-condition priority is a routing model
/// and a routing model is the thing after it (<c>docs/alerts.md</c>).
/// </para>
/// </remarks>
public sealed record NtfyMessage(string Title, string Body, Uri? Link)
{
    /// <summary>What this alert says on a phone.</summary>
    public static NtfyMessage For(Alert alert, Uri? link) => alert switch
    {
        Alert.StoreFillingUp filling => new NtfyMessage(
            $"logaffe: {filling.HostName} is filling up",
            $"The filesystem holding the database is {filling.Percent} per cent full, "
            + $"past {filling.Threshold}.",
            link),

        Alert.ProjectGoneQuiet quiet => new NtfyMessage(
            $"logaffe: {quiet.ProjectName} has gone quiet",
            $"Nothing received for {quiet.Hours} hours, against {quiet.Tolerated} "
            + "this project is ordinarily quiet for.",
            link),

        Alert.ProjectFlooding flood => new NtfyMessage(
            $"logaffe: {flood.ProjectName} is flooding",
            $"{flood.Entries} entries in the hour from "
            + $"{flood.Hour.UtcDateTime:yyyy-MM-dd HH:mm} UTC, against a usual "
            + $"{flood.Baseline}.",
            link),

        // The second hour is the one it fired on, and the hour before it rides
        // along because it is the answer to "why now, and not an hour ago".
        Alert.ProjectFailing failing => new NtfyMessage(
            $"logaffe: {failing.ProjectName} is failing",
            $"{failing.Errors} entries at Error or above in the hour from "
            + $"{failing.Hour.UtcDateTime:yyyy-MM-dd HH:mm} UTC, after "
            + $"{failing.Previous} in the hour before it, against a usual "
            + $"{failing.Baseline}.",
            link),

        _ => throw new ArgumentOutOfRangeException(
            nameof(alert), alert, "The conditions are a closed set (ADR 0050)."),
    };

    /// <summary>
    /// The operator's own, which is the shape a real alert has and belongs to no
    /// condition.
    /// </summary>
    /// <remarks>
    /// It says what it is in the title, because the point of it is that nobody
    /// reading it at three in the morning mistakes it for an incident — and it
    /// carries the link every other notification carries, so that what is proved
    /// is the whole path and not only the sending.
    /// </remarks>
    public static NtfyMessage Test(Uri? home) => new(
        "logaffe: a test notification",
        "This is what an alert from this installation looks like: a name, its numbers "
        + "and a link, and never anything an entry said.",
        home);
}

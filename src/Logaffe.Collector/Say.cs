namespace Logaffe.Collector;

/// <summary>
/// What this collector writes about itself, which goes to its container log and
/// nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// A timestamp and a sentence. There is no logging framework here and no
/// package for one: this program has three settings, one loop and one thing it
/// can say, and a level to filter by would be a fourth setting for a stream
/// that carries a line a minute at its noisiest.
/// </para>
/// <para>
/// Everything goes to standard output, including the failures. Docker collects
/// both streams into one log either way, and splitting them would only mean
/// that the line saying a delivery failed and the line saying it recovered can
/// arrive out of order.
/// </para>
/// <para>
/// The clock here is this machine's, and it is the only place this collector
/// uses one: a sample carries no timestamp, because the installation stamps it
/// (<c>docs/metrics.md</c>). A line in a container log is read beside other
/// container logs on the same machine, so this one is local time's to state.
/// </para>
/// </remarks>
internal static class Say
{
    public static void Line(string sentence) =>
        Console.Out.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {sentence}");
}

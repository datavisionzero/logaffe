namespace Logaffe.Domain.Queries;

/// <summary>
/// How many entries one page holds.
/// </summary>
/// <remarks>
/// <para>
/// It is a <b>product value</b> and not a setting: the same in every
/// installation, the same for the operator and the agent, and not something a
/// caller raises. A page size a caller chose would be the read path's version of
/// the batch cap the ingestion path already refuses to make configurable — one
/// request deciding on its own behalf how much of the installation it may
/// occupy.
/// </para>
/// <para>
/// <b>A page carries no total.</b> Counting the matches of a substring search is
/// a scan, and paying for it on every page to display a number nobody asked for
/// is the wrong default. What a page carries instead is the cursor for the next
/// one, and a count is its own deliberate act.
/// </para>
/// <para>
/// The MCP adapter's caps of 200 and 50 are not this number. Those bound one
/// answer to an agent and are that adapter's, and they sit above this: a tool
/// filling its cap pages until it is full.
/// </para>
/// </remarks>
public static class Page
{
    /// <summary>
    /// A screenful and some, so that the operator's first act after a filter
    /// change is reading rather than paging, and few enough that the entries
    /// behind it — messages, exceptions and properties — stay a response and not
    /// a download.
    /// </summary>
    public const int Size = 100;
}

namespace Logaffe.Domain.Projects;

/// <summary>
/// The projects one request may see — the one filter every read narrows to
/// (ADR 0055).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a type rather than a habit.</b> Reading a project takes one of
/// these, so a call site that has none does not compile, and there is one place
/// that can be got wrong instead of eight. What it stands in front of is every
/// entry query, the search, the tail, the filter values, the volume figures, the
/// alerts and the retention screen — and a leak is a call site that forgot,
/// which is what having one filter is meant to make impossible rather than
/// unlikely.
/// </para>
/// <para>
/// <b>A project out of reach does not exist.</b> It is absent from lists rather
/// than present and refused, and asking for it by id answers the way asking for
/// a project that was never created answers. A refusal that told the two apart
/// would be a probe for what else the installation holds.
/// </para>
/// </remarks>
public sealed class Reach
{
    private readonly HashSet<Guid>? projects;

    private Reach(HashSet<Guid>? projects) => this.projects = projects;

    /// <summary>
    /// Everything the installation holds, for the work that has nobody behind
    /// it: the retention sweep, the hourly alert pass, the tally flush, the
    /// ingest path and Host Recovery.
    /// </summary>
    /// <remarks>
    /// Named rather than implied, because this is the one value that is not a
    /// person's — a use of it that is answering somebody's request is the bug
    /// this whole type exists to make visible in a diff.
    /// </remarks>
    public static Reach TheInstallation { get; } = new(null);

    /// <summary>What one identity was assigned, which may be nothing at all.</summary>
    public static Reach Of(IEnumerable<Guid> projects) => new([.. projects]);

    /// <summary>Nothing: an account that has been given no project yet.</summary>
    public static Reach Nothing { get; } = new([]);

    /// <summary>
    /// Whether this is the installation's own reach. Read by the stores, which
    /// narrow their queries by <see cref="Projects"/> when it is not.
    /// </summary>
    public bool IsEverything => projects is null;

    /// <summary>
    /// The ids to narrow by. Empty when the identity holds none, which narrows
    /// every read to nothing rather than to everything — the direction a mistake
    /// here has to fail in.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This is <see cref="TheInstallation"/>, which names no ids because it
    /// excludes none.
    /// </exception>
    public IReadOnlyCollection<Guid> Projects =>
        projects
        ?? throw new InvalidOperationException(
            "The installation's own reach names no projects; it excludes none.");

    public bool Includes(Guid projectId) => projects?.Contains(projectId) ?? true;
}

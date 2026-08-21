namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The one row holding what the operator has set for the whole installation.
/// </summary>
/// <remarks>
/// <para>
/// It is here rather than in Domain because there is no rule in it. The rule is
/// <see cref="Domain.Projects.RetentionWindow"/>, which already holds the floor
/// and the ceiling no installation can raise (ADR 0020); this is only where the
/// current value is kept, and a Domain type carrying one integer that is already
/// validated elsewhere would be a class pretending to be a decision.
/// </para>
/// <para>
/// Single-row, by the same column and for the same reason the account and the
/// claim guard are: two containers writing it at once are decided by the
/// database rather than by a check either of them could have run first.
/// </para>
/// </remarks>
public sealed class InstallationSettings
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// How long samples are kept, in days. One window for the installation
    /// rather than one per host, because there is no reason to keep one
    /// machine's numbers longer than another's (<c>docs/metrics.md</c>).
    /// </summary>
    public int SampleRetentionDays { get; set; }

    /// <summary>
    /// The machine logaffe is itself on, or <c>null</c> for an installation that
    /// names none — which is every installation until the operator says
    /// otherwise (<c>docs/metrics.md</c>).
    /// </summary>
    public Guid? HostId { get; set; }

    /// <summary>
    /// Which of that machine's filesystems holds the database, as the mount its
    /// collector reports it under. The pair is written and cleared together, and
    /// read as a pair: deleting the host sets <see cref="HostId"/> to null and
    /// leaves this behind, and a mount without a machine names nothing.
    /// </summary>
    public string? MountPath { get; set; }
}

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
    /// Whether the store filling up is watched for, which is off until the
    /// operator switches it on (<c>docs/alerts.md</c>).
    /// </summary>
    /// <remarks>
    /// Three columns rather than one set of flags, because they are three
    /// switches on a screen and the set is closed: a fourth is a change to
    /// ADR 0050 and a migration, which is the friction that decision was built
    /// with.
    /// </remarks>
    public bool AlertOnFillingUp { get; set; }

    /// <summary>Whether a project going quiet is watched for.</summary>
    public bool AlertOnGoneQuiet { get; set; }

    /// <summary>Whether a project flooding is watched for.</summary>
    public bool AlertOnFlooding { get; set; }

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

    /// <summary>
    /// The ntfy server notifications are posted to, or <c>null</c> on an
    /// installation that has no notifier (<c>docs/alerts.md</c>).
    /// </summary>
    /// <remarks>
    /// Three columns rather than one, for the reason the switches are three:
    /// they are three fields on a screen. They are written and cleared together
    /// and read as a set — a topic naming no server addresses nothing, and a
    /// token belonging to neither is a secret held for nothing.
    /// </remarks>
    public string? NotifierServer { get; set; }

    /// <summary>The topic on that server.</summary>
    public string? NotifierTopic { get; set; }

    /// <summary>
    /// The access token sealed under the key on the host volume (ADR 0022), or
    /// <c>null</c> for the public topic that needs none. It is stored the way
    /// every token in this product is, and for the same reason: the operator can
    /// read it back rather than reissue it at the notifier.
    /// </summary>
    public byte[]? NotifierAccessToken { get; set; }
}

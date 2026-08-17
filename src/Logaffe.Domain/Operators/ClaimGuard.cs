namespace Logaffe.Domain.Operators;

/// <summary>
/// What stands in front of the claim on an installation nobody owns: the secret
/// it drew, and the half hour it would otherwise be open for.
/// </summary>
/// <remarks>
/// <para>
/// There are two ways to guard a claim and whoever installs picks one
/// (ADR 0040). A <see cref="ClaimSecret"/> is the default and has no deadline; the
/// window is the other, and it opens when the installation first runs, lasts
/// <see cref="WindowDuration"/>, and is not extended by a restart — the deadline
/// belongs to the installation rather than to the process, so nobody gains
/// anything by forcing one (<c>docs/setup.md</c>).
/// </para>
/// <para>
/// Which of the two is in force is configuration and is not held here: it is read
/// on every start while the installation is unclaimed, so a compose file written
/// wrong is fixed by editing it. What is held here is what only the installation
/// can know — when it first ran, and the hash of the secret it drew.
/// </para>
/// <para>
/// There is one of these, in a table holding one row, and it lives in the
/// database rather than on the host volume beside the key (ADR 0034) — so "first
/// run" is exactly the run that created the schema. The drawn secret's hash is in
/// the same row for the same reasons; the secret itself is on the volume until
/// the claim, and never here.
/// </para>
/// <para>
/// Thirty minutes is short on purpose, and it is short because the way back is
/// cheap. It is measured against a person walking back to their desk rather than
/// against a scanner's patience: a fresh hostname appears in the public
/// Certificate Transparency logs within seconds of its certificate being issued,
/// so an installation reachable under its own name is discoverable almost
/// immediately.
/// </para>
/// </remarks>
public sealed class ClaimGuard
{
    /// <summary>
    /// Thirty minutes, from <see cref="OpenedAt"/>, in window mode.
    /// </summary>
    public static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(30);

    private ClaimGuard()
    {
        // EF Core materializes through this; every other route goes through
        // OpenedOnFirstRun.
    }

    private ClaimGuard(Guid id, DateTimeOffset openedAt)
    {
        Id = id;
        OpenedAt = openedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// When the installation last became claimable: its first run, or the last
    /// Host Recovery. It is not a secret — the screen an unclaimed installation
    /// shows in window mode counts down from it.
    /// </summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>
    /// The hash of the secret this installation drew, or <c>null</c> when it
    /// never drew one — because it is in window mode, or because configuration
    /// supplies the secret and a supplied one is stored nowhere (ADR 0040).
    /// </summary>
    public byte[]? DrawnSecretHash { get; private set; }

    /// <summary>Whether this installation is holding a secret it drew itself.</summary>
    public bool HasDrawnSecret => DrawnSecretHash is not null;

    /// <summary>
    /// Derived rather than stored, so that it cannot disagree with the instant
    /// it is measured from.
    /// </summary>
    public DateTimeOffset WindowClosesAt => OpenedAt + WindowDuration;

    public bool WindowIsOpenAt(DateTimeOffset when) => when < WindowClosesAt;

    /// <summary>
    /// Opens the window the installation gets for having been started for the
    /// first time.
    /// </summary>
    /// <remarks>
    /// Called on every start and written on exactly one of them: what makes a
    /// restart not extend anything is that the row is only inserted when there
    /// is none, and the store is what holds that. The instant is written whichever
    /// mode the installation is in, because which mode that is can change and the
    /// first run cannot.
    /// </remarks>
    public static ClaimGuard OpenedOnFirstRun(DateTimeOffset at) =>
        new(Guid.CreateVersion7(), at);

    /// <summary>
    /// Takes the secret the installation just drew, which is what a first start
    /// in secret mode does when configuration supplied none.
    /// </summary>
    /// <remarks>
    /// Only the hash stays here. The secret itself goes to the host volume for
    /// the operator to read, and is removed from there by the claim.
    /// </remarks>
    public void DrewSecret(ClaimSecret secret) => DrawnSecretHash = secret.Hash;

    /// <summary>
    /// Whether this is the secret that was drawn. A presented secret is compared
    /// against the drawn hash in constant time, and an installation that drew
    /// none admits nothing this way.
    /// </summary>
    public bool AdmitsDrawn(ClaimSecret presented) => presented.Matches(DrawnSecretHash);

    /// <summary>
    /// Opens the way in again on an installation that is being handed back, which
    /// is the half of Host Recovery that is not the account going away
    /// (ADR 0013).
    /// </summary>
    /// <remarks>
    /// It moves the instant rather than making a second row. There is one guard
    /// per installation and it is the current one; a previous one is not a thing
    /// the product has any use for.
    /// <para>
    /// <b>It forgets the drawn secret.</b> This is exactly the moment at which the
    /// installation's notion of who may claim it changes, so a secret that
    /// survived it would be one the previous operator still holds; the command
    /// draws a fresh one where the mode calls for it.
    /// </para>
    /// </remarks>
    public void ArmAt(DateTimeOffset when)
    {
        OpenedAt = when;
        DrawnSecretHash = null;
    }
}

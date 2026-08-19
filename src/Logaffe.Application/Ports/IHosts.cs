using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Ports;

/// <summary>
/// The host rows an installation holds, found by the identity everything else
/// attaches to and by the name the operator typed.
/// </summary>
/// <remarks>
/// <para>
/// The list is read whole, for the reason the project list is: an installation
/// holds a handful of machines, the list is read when a settings screen is
/// opened and rarely again, and paging a screen that fits is a management
/// surface bought for nothing.
/// </para>
/// <para>
/// <see cref="FindAsync(string, CancellationToken)"/> takes no group, unlike its
/// counterpart on <see cref="IProjects"/>: a host sits in nothing, so a name is
/// taken across the installation or it is free.
/// </para>
/// <para>
/// Removing takes the tokens with it, which is the database's doing. It does not
/// take the samples: those follow in the background exactly as a deleted
/// project's entries do (ADR 0019), and it does not take the projects either —
/// they are left sitting on no host, which destroys nothing that belongs to
/// them.
/// </para>
/// </remarks>
public interface IHosts
{
    /// <summary>Every host, oldest first, which is the operator's list.</summary>
    Task<IReadOnlyList<Host>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The host the caller named, or <c>null</c> when there is none — which is
    /// what a host deleted in another browser tab looks like.
    /// </summary>
    Task<Host?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The host holding this name, or <c>null</c> when it is free. The name
    /// given is the one <see cref="Host.NormalizeName"/> produced.
    /// </summary>
    Task<Host?> FindAsync(string name, CancellationToken cancellationToken);

    Task AddAsync(Host host, CancellationToken cancellationToken);

    /// <summary>Writes back the name just given to <paramref name="host"/>.</summary>
    Task RecordAsync(Host host, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the host and the tokens that admitted samples to it, and leaves
    /// the projects that sat on it sitting on none. Its samples are not this
    /// act's business.
    /// </summary>
    Task RemoveAsync(Host host, CancellationToken cancellationToken);
}

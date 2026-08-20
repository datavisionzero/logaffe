using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Operations;

/// <summary>How creating a host ended.</summary>
public enum CreateHostOutcome
{
    /// <summary>The host exists, and it is the only way one ever comes about.</summary>
    Created,

    /// <summary>
    /// The installation already holds a host by that name. There is no group to
    /// relax it the way a project's name is relaxed: a host sits in nothing.
    /// </summary>
    NameTaken,
}

/// <summary>The end of a creation, and the host it made when it succeeded.</summary>
public sealed record HostCreationAttempt(CreateHostOutcome Outcome, Host? Host);

/// <summary>
/// The operator bringing a host into existence, which is the only way one ever
/// comes about.
/// </summary>
/// <remarks>
/// <para>
/// There is no implicit creation on first delivery, for the reason a project has
/// none: a token that names nothing admits nothing, and an installation's list
/// of machines is exactly what the operator put there.
/// </para>
/// <para>
/// It creates the host and nothing else. A host with no token receives nothing
/// until one is issued, and issuing is its own act — the same one the operator
/// reaches for when they rotate.
/// </para>
/// <para>
/// It is the operator's act and an administering agent's, and it is unreachable
/// from a reading token — a log entry asking the agent that read it to make a
/// host has to find nothing to call, which is a property of the tool list that
/// token is handed rather than a permission (ADR 0046).
/// </para>
/// </remarks>
public sealed class CreateHost(IHosts hosts, TimeProvider clock)
{
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="Host.NameMaxLength"/>.
    /// </exception>
    public async Task<HostCreationAttempt> ExecuteAsync(
        string name, CancellationToken cancellationToken)
    {
        var taken = await hosts.FindAsync(Host.NormalizeName(name), cancellationToken);
        if (taken is not null)
        {
            return new HostCreationAttempt(CreateHostOutcome.NameTaken, null);
        }

        var host = Host.Create(name, clock.GetUtcNow());
        await hosts.AddAsync(host, cancellationToken);

        return new HostCreationAttempt(CreateHostOutcome.Created, host);
    }
}

/// <summary>How renaming a host ended.</summary>
public enum RenameHostOutcome
{
    Renamed,

    /// <summary>
    /// There is no such host. A second browser tab deleted it, or the address
    /// was typed.
    /// </summary>
    NoSuchHost,

    /// <summary>The installation already holds a host by that name.</summary>
    NameTaken,
}

/// <summary>
/// Giving a host another name.
/// </summary>
/// <remarks>
/// The identity survives it, so nothing moves: the samples, the token and the
/// projects sitting on this machine are attached to the identity and none of
/// them notices. That is the whole reason a host is a row rather than a word
/// written on each project (ADR 0039).
/// </remarks>
public sealed class RenameHost(IHosts hosts)
{
    /// <inheritdoc cref="CreateHost.ExecuteAsync"/>
    public async Task<RenameHostOutcome> ExecuteAsync(
        Guid id, string name, CancellationToken cancellationToken)
    {
        var host = await hosts.FindAsync(id, cancellationToken);
        if (host is null)
        {
            return RenameHostOutcome.NoSuchHost;
        }

        var normalized = Host.NormalizeName(name);

        var taken = await hosts.FindAsync(normalized, cancellationToken);
        if (taken is not null && taken.Id != id)
        {
            return RenameHostOutcome.NameTaken;
        }

        host.Rename(normalized);
        await hosts.RecordAsync(host, cancellationToken);

        return RenameHostOutcome.Renamed;
    }
}

/// <summary>
/// Ending a host: the machine's history, and the credential that was writing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The projects that sat on it are left sitting on none</b>, and nothing else
/// about them changes. That half is the group's behaviour and it is right here
/// for the group's reason: a host is where a project runs, and forgetting where
/// it runs destroys nothing that belongs to the project.
/// </para>
/// <para>
/// <b>The samples follow in the background</b>, exactly as a deleted project's
/// entries do (ADR 0019). The host and its token go at once, so nothing can
/// reach them in the meantime — every read of samples names a host, and that
/// host is gone.
/// </para>
/// <para>
/// The typed name that guards this in the interface is the interface's
/// (<c>docs/ui.md</c>). This takes an identity and no typed name: repeating the
/// name back would protect nobody who issued the request deliberately.
/// </para>
/// </remarks>
public sealed class DeleteHost(IHosts hosts)
{
    /// <summary>
    /// Whether there was a host to delete. <c>false</c> is one already gone — a
    /// second click, or another tab — and not a failure of anything.
    /// </summary>
    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var host = await hosts.FindAsync(id, cancellationToken);
        if (host is null)
        {
            return false;
        }

        await hosts.RemoveAsync(host, cancellationToken);
        return true;
    }
}

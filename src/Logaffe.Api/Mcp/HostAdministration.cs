using System.ComponentModel;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Host = Logaffe.Domain.Hosts.Host;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The host acts an administering token earns, less the one that destroys, and
/// the installation's sample window in the direction that keeps samples.
/// </summary>
/// <remarks>
/// <para>
/// <b>Creating a host does not hand back the command that starts its
/// collector.</b> Issuing its token does, which is the ingest token's
/// arrangement exactly: the command is a thing made out of a credential, and
/// creating the row it hangs off is not the moment there is one.
/// </para>
/// <para>
/// <b>The sample window is the installation's and not a host's.</b> There is one
/// of it, so <c>extend_sample_retention</c> takes no identity — which is also
/// why it sits here beside the hosts rather than beside a project's window.
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.AdministeringPolicy)]
[McpServerToolType]
public static class HostAdministration
{
    [McpServerTool(
        Name = "create_host",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Brings a machine into existence as something this installation tracks. It
        collects nothing yet: issue_host_token hands back the command that starts
        a collector on it, and put_project_on_host says which projects run there.

        A host is not a scope. Naming two projects onto one machine does not make
        them askable together, and no query takes a host.
        """)]
    public static async Task<AdministeredHost> CreateAsync(
        CreateHost create,
        [Description("What the operator calls this machine. Unique across the installation.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AName(name, Host.NameMaxLength);

        var created = await create.ExecuteAsync(wanted, cancellationToken);

        return created.Outcome is CreateHostOutcome.NameTaken
            ? throw Refused.HostNameTaken(wanted)
            : AdministeredHost.Of(created.Host!);
    }

    [McpServerTool(
        Name = "rename_host",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Gives a machine another name. Its identity does not change, so the
        projects on it stay on it, the samples already delivered stay attached to
        it, and a collector already reporting carries on without noticing.
        """)]
    public static async Task<AdministeredHost> RenameAsync(
        RenameHost rename,
        ListHosts hosts,
        [Description("The machine to rename, as get_settings gives it.")]
        Guid hostId,
        [Description("What it should be called instead.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AName(name, Host.NameMaxLength);

        return await rename.ExecuteAsync(hostId, wanted, cancellationToken) switch
        {
            RenameHostOutcome.Renamed => AdministeredHost.Of(
                await FoundAsync(hosts, hostId, cancellationToken)),
            RenameHostOutcome.NameTaken => throw Refused.HostNameTaken(wanted),
            _ => throw Refused.NoSuchHost(hostId),
        };
    }

    [McpServerTool(
        Name = "extend_sample_retention",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Makes the installation keep host samples for longer. There is one window
        for every host rather than one per machine, so this takes no host.

        Nothing is removed by it and nothing comes back either: samples already
        swept out under the old window are gone. A value below the window as it
        stands is refused, and the refusal names the tool that lowers one.
        """)]
    public static async Task<SampleRetentionAnswer> ExtendAsync(
        ChangeSampleRetention change,
        [Description("The new window, between 1 and 90 days, and not below the current one.")]
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AWindow(retentionDays);
        var now = await change.ReadAsync(cancellationToken);

        // Equal is neither direction and is a no-op, for `extend_project_retention`'s
        // reason: what is refused is the direction that removes samples.
        if (wanted.Days < now.Days)
        {
            throw Refused.WrongDirection(now.Days, wanted.Days, "shorten_sample_retention");
        }

        await change.ExecuteAsync(wanted, cancellationToken);

        return new SampleRetentionAnswer { RetentionDays = wanted.Days };
    }

    /// <remarks>
    /// There is no act that reads one host: a list of them is what the settings
    /// screen reads and what the application layer offers, so this finds the row
    /// in that list rather than holding a query of its own (ADR 0030). It is the
    /// same reading <c>GroupAdministration</c> makes, for the same reason.
    /// </remarks>
    internal static async Task<ListedHost> FoundAsync(
        ListHosts hosts, Guid hostId, CancellationToken cancellationToken) =>
        (await hosts.ExecuteAsync(cancellationToken)).FirstOrDefault(host => host.Id == hostId)
        ?? throw Refused.NoSuchHost(hostId);
}

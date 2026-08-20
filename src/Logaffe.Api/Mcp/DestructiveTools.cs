using System.ComponentModel;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The four acts that remove data that does not come back, and the only ones an
/// administering token has to have been issued to reach.
/// </summary>
/// <remarks>
/// <para>
/// <b>They are absent from a token without the flag, not present and refusing.</b>
/// That is the whole reason the retention window is two tools per direction
/// rather than one setter: a token that may not destroy is handed no shortening
/// tool at all, so what it can do is legible in the list it was given rather than
/// discoverable by making a call and reading the error (ADR 0046).
/// </para>
/// <para>
/// <b>Destructive means data that does not come back</b>, which is exactly these
/// four. Deleting a project takes its entries with it (ADR 0019); deleting a host
/// takes its samples; and each shortening removes what now falls outside the
/// window. The two shortenings are why the flag is not called <i>delete</i>: they
/// read like settings and they remove stored entries, which makes them the ones
/// worth being unable to do by accident.
/// </para>
/// <para>
/// <b>Nothing else on this surface belongs here.</b> Creating, renaming, moving,
/// extending a window, deleting a group and revoking a token all leave what is
/// stored where it is — a revoked token stops a sender delivering and the entries
/// that would have arrived never exist, but nothing that is already there is gone
/// afterwards, and another token closes the gap. Reading the flag the other way
/// would make it mean two unrelated things and its name would stop describing it.
/// </para>
/// <para>
/// <b>There is no confirmation step and there is no undo.</b> Confirming a
/// deletion by typing a name is a guard that belongs to the screen an operator is
/// standing in front of; repeated back over a tool call it would protect nobody,
/// and the thing that actually bounds this surface is that the token was issued
/// saying so and reads no entry.
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.DestroyingPolicy)]
[McpServerToolType]
public static class DestructiveTools
{
    [McpServerTool(
        Name = "delete_project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Ends a project, immediately and irreversibly, and its entries go with it.
        Its ingest tokens go at once, so anything still delivering is answered
        401; the entries are removed in the background afterwards and nothing can
        reach them in the meantime.

        There is no undelete, no archive and no grace period. Ask
        count_entries_outside_window or get_settings first if the operator should
        be told what is about to go.
        """)]
    public static async Task<RemovedAnswer> DeleteProjectAsync(
        DeleteProject delete,
        ReadProject read,
        [Description("The project to end, as get_settings gives it.")]
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Read before the removal: afterwards nothing can say what the project
        // was called, and an agent reporting back that it deleted `orders` is
        // saying something the operator can check.
        var project = await read.ExecuteAsync(projectId, cancellationToken)
            ?? throw Refused.NoSuchProject(projectId);

        if (!await delete.ExecuteAsync(projectId, cancellationToken))
        {
            throw Refused.NoSuchProject(projectId);
        }

        return new RemovedAnswer { Id = project.Id, Name = project.Name };
    }

    [McpServerTool(
        Name = "delete_host",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Ends a host, immediately and irreversibly, and the samples it reported go
        with it. Its host tokens go at once, so a collector still running on the
        machine is answered 401 and stops reporting.

        Projects that ran on it stay, with their entries; what they lose is the
        machine behind them, and put_project_on_host is what gives them another.
        """)]
    public static async Task<RemovedAnswer> DeleteHostAsync(
        DeleteHost delete,
        ListHosts hosts,
        [Description("The machine to end, as get_settings gives it.")]
        Guid hostId,
        CancellationToken cancellationToken = default)
    {
        var host = await HostAdministration.FoundAsync(hosts, hostId, cancellationToken);

        if (!await delete.ExecuteAsync(hostId, cancellationToken))
        {
            throw Refused.NoSuchHost(hostId);
        }

        return new RemovedAnswer { Id = host.Id, Name = host.Name };
    }

    [McpServerTool(
        Name = "shorten_project_retention",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Makes a project keep its entries for less time. Everything that now falls
        outside the window is removed by the next sweep and does not come back.

        Call count_entries_outside_window first: it says how many entries this
        would drop, and it changes nothing. A value above the window the project
        has now is refused, and the refusal names the tool that raises one.
        """)]
    public static async Task<AdministeredProject> ShortenProjectRetentionAsync(
        ChangeRetentionWindow change,
        ReadProject read,
        [Description("The project, as get_settings gives it.")]
        Guid projectId,
        [Description("The new window, between 1 and 90 days, and not above the current one.")]
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AWindow(retentionDays);
        var project = await read.ExecuteAsync(projectId, cancellationToken)
            ?? throw Refused.NoSuchProject(projectId);

        // Equal is neither direction and is a no-op, the same as on the tool
        // that raises one: what each refuses is the direction it does not do.
        if (wanted.Days > project.Retention.Days)
        {
            throw Refused.WrongDirection(
                project.Retention.Days, wanted.Days, "extend_project_retention");
        }

        await change.ExecuteAsync(projectId, wanted, cancellationToken);

        return await ProjectAdministration.AsItStandsAsync(read, projectId, cancellationToken);
    }

    [McpServerTool(
        Name = "shorten_sample_retention",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Makes the installation keep host samples for less time. Everything that
        now falls outside the window is removed by the next sweep and does not
        come back. There is one window for every host, so this takes no host.

        A value above the window as it stands is refused, and the refusal names
        the tool that raises one.
        """)]
    public static async Task<SampleRetentionAnswer> ShortenSampleRetentionAsync(
        ChangeSampleRetention change,
        [Description("The new window, between 1 and 90 days, and not above the current one.")]
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AWindow(retentionDays);
        var now = await change.ReadAsync(cancellationToken);

        if (wanted.Days > now.Days)
        {
            throw Refused.WrongDirection(now.Days, wanted.Days, "extend_sample_retention");
        }

        await change.ExecuteAsync(wanted, cancellationToken);

        return new SampleRetentionAnswer { RetentionDays = wanted.Days };
    }
}

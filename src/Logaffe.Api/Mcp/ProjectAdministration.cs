using System.ComponentModel;
using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The project acts an administering token earns, less the one that destroys.
/// </summary>
/// <remarks>
/// <para>
/// <b>They add no behaviour of their own.</b> Every one of them is the use case
/// the operator's own settings screen calls, so the two consumers cannot drift
/// into two readings of what a rename or a move does (ADR 0030). What is decided
/// here is a shape and a refusal, and nothing else is allowed to be.
/// </para>
/// <para>
/// <b>Each answers with the project as it now stands</b> rather than with
/// nothing, so an agent that made a change can report what it made without a
/// second call, and one that made several does not have to hold a picture of the
/// installation in its head.
/// </para>
/// <para>
/// <b><c>delete_project</c> is not here.</b> It removes entries that do not come
/// back and lives with the other three that do, behind the flag an operator sets
/// when they issue the token (ADR 0046) — so a token that may not destroy is
/// handed no deletion tool rather than one that refuses.
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.AdministeringPolicy)]
[McpServerToolType]
public static class ProjectAdministration
{
    [McpServerTool(
        Name = "create_project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Brings a project into existence, which is the only way one ever comes
        about — nothing is created by a delivery arriving.

        It hands back no token and the project receives nothing until one is
        issued: call issue_ingest_token afterwards, which is the same second step
        the operator's own screen takes.

        The name has to be free where the project will be listed, which is inside
        the group it is given or among the projects in no group.
        """)]
    public static async Task<AdministeredProject> CreateAsync(
        CreateProject create,
        [Description("What the operator will read at three in the morning.")]
        string name,
        [Description(
            "How long it keeps its entries, counted from receipt rather than "
            + "from when they happened. Between 1 and 90 days.")]
        int retentionDays,
        [Description(
            "The group to list it under, or nothing for none — which is where "
            + "most projects are. Groups are on get_settings.")]
        Guid? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AName(name, Project.NameMaxLength);
        var window = Given.AWindow(retentionDays);

        var created = await create.ExecuteAsync(wanted, window, groupId, cancellationToken);

        return created.Outcome switch
        {
            CreateProjectOutcome.Created => AdministeredProject.Of(created.Project!),
            CreateProjectOutcome.NameTaken => throw Refused.ProjectNameTaken(wanted),
            _ => throw Refused.NoSuchGroup(groupId!.Value),
        };
    }

    [McpServerTool(
        Name = "rename_project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Gives a project another name. Its identity does not change, so entries,
        tokens and queries stay where they are, no delivery breaks and no sender
        notices — what changes is the word the operator reads.

        The new name has to be free where the project is listed.
        """)]
    public static async Task<AdministeredProject> RenameAsync(
        RenameProject rename,
        ReadProject read,
        [Description("The project to rename, as get_settings gives it.")]
        Guid projectId,
        [Description("What it should be called instead.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AName(name, Project.NameMaxLength);

        return await rename.ExecuteAsync(projectId, wanted, cancellationToken) switch
        {
            RenameOutcome.Renamed => await AsItStandsAsync(read, projectId, cancellationToken),
            RenameOutcome.NameTaken => throw Refused.ProjectNameTaken(wanted),
            _ => throw Refused.NoSuchProject(projectId),
        };
    }

    [McpServerTool(
        Name = "move_project_to_group",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Lists a project under another group, or under none. It moves nothing but
        the heading it appears under: entries, tokens and queries hang off its
        identity, so nothing is redeployed and no sender notices.

        A group that already lists a project by this one's name refuses the move
        rather than resolving it — renaming a project nobody asked to rename is
        not this tool's decision. Rename one of the two first.
        """)]
    public static async Task<AdministeredProject> MoveAsync(
        MoveProjectToGroup move,
        ReadProject read,
        [Description("The project to move, as get_settings gives it.")]
        Guid projectId,
        [Description(
            "The group to list it under, or nothing to take it out of every "
            + "group. Groups are on get_settings; create_group makes one.")]
        Guid? groupId = null,
        CancellationToken cancellationToken = default) =>
        await move.ExecuteAsync(projectId, groupId, cancellationToken) switch
        {
            MoveProjectOutcome.Moved => await AsItStandsAsync(read, projectId, cancellationToken),
            MoveProjectOutcome.NoSuchGroup => throw Refused.NoSuchGroup(groupId!.Value),
            MoveProjectOutcome.NameTaken => throw Refused.NameTakenWhereItWasGoing(),
            _ => throw Refused.NoSuchProject(projectId),
        };

    [McpServerTool(
        Name = "put_project_on_host",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Says which machine a project runs on, or that none is tracked for it. It
        moves nothing either: what it changes is whether there is anything to
        show about the machine behind this project's entries.

        Unlike a group, a host is not where a project is listed, so no name can
        be taken — several projects may perfectly well run on one machine.
        """)]
    public static async Task<AdministeredProject> PutOnHostAsync(
        PutProjectOnHost put,
        ReadProject read,
        [Description("The project, as get_settings gives it.")]
        Guid projectId,
        [Description(
            "The machine it runs on, or nothing to track none. Hosts are on "
            + "get_settings; create_host makes one.")]
        Guid? hostId = null,
        CancellationToken cancellationToken = default) =>
        await put.ExecuteAsync(projectId, hostId, cancellationToken) switch
        {
            PutProjectOnHostOutcome.PutOn =>
                await AsItStandsAsync(read, projectId, cancellationToken),
            PutProjectOnHostOutcome.NoSuchHost => throw Refused.NoSuchHost(hostId!.Value),
            _ => throw Refused.NoSuchProject(projectId),
        };

    [McpServerTool(
        Name = "extend_project_retention",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Makes a project keep its entries for longer. Nothing is removed by it and
        nothing comes back either: entries already swept out under the old window
        are gone.

        A value below the window the project has now is refused, and the refusal
        names the tool that lowers one. get_settings says where the window is.
        """)]
    public static async Task<AdministeredProject> ExtendAsync(
        ChangeRetentionWindow change,
        ReadProject read,
        [Description("The project, as get_settings gives it.")]
        Guid projectId,
        [Description("The new window, between 1 and 90 days, and not below the current one.")]
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AWindow(retentionDays);
        var project = await read.ExecuteAsync(projectId, cancellationToken)
            ?? throw Refused.NoSuchProject(projectId);

        // Equal is neither direction and is a no-op, not a refusal: an agent
        // setting a window to what it already holds has asked for the state that
        // is already there. What is refused is the other direction, which is the
        // one that removes entries.
        if (wanted.Days < project.Retention.Days)
        {
            throw Refused.WrongDirection(
                project.Retention.Days, wanted.Days, "shorten_project_retention");
        }

        await change.ExecuteAsync(projectId, wanted, cancellationToken);

        return await AsItStandsAsync(read, projectId, cancellationToken);
    }

    [McpServerTool(
        Name = "count_entries_outside_window",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        How many entries a project holds that a proposed window would put outside
        it — asked before anything is dropped, and dropping nothing itself.

        It answers with a number and never with an entry, which is why a count
        lives on this surface at all. It is on every administering token,
        including one that cannot shorten a window: an agent that may not make
        the change can still tell the operator what it would cost.
        """)]
    public static async Task<EntriesOutsideWindowAnswer> CountOutsideAsync(
        CountEntriesOutsideWindow count,
        [Description("The project, as get_settings gives it.")]
        Guid projectId,
        [Description("The window to weigh, between 1 and 90 days.")]
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var proposed = Given.AWindow(retentionDays);

        var outside = await count.ExecuteAsync(projectId, proposed, cancellationToken)
            ?? throw Refused.NoSuchProject(projectId);

        return new EntriesOutsideWindowAnswer
        {
            RetentionDays = proposed.Days,
            Entries = outside,
        };
    }

    /// <summary>
    /// The project as it is once the act has run, which is what every one of
    /// them answers with.
    /// </summary>
    /// <remarks>
    /// It can find nothing only if the operator deleted the project between the
    /// act and this read, in another tab. That reads as the project not being
    /// there, which is what it is.
    /// </remarks>
    internal static async Task<AdministeredProject> AsItStandsAsync(
        ReadProject read, Guid projectId, CancellationToken cancellationToken) =>
        AdministeredProject.Of(
            await read.ExecuteAsync(projectId, cancellationToken)
            ?? throw Refused.NoSuchProject(projectId));
}

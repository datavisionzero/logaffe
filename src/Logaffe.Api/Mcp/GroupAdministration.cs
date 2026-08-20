using System.ComponentModel;
using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The three group acts, all of them on every administering token.
/// </summary>
/// <remarks>
/// <b><c>delete_group</c> is here rather than behind the flag, and it is the
/// clearest illustration of what that flag is doing.</b> A group holds nothing —
/// no retention its projects inherit, no token, no entry (ADR 0039) — so
/// deleting one takes nothing with it: the projects come out of the group and
/// stay, exactly as they would if they had been moved out one at a time. The
/// flag is about data that does not come back, not about the word "delete".
/// </remarks>
[Authorize(Policy = AgentAuthentication.AdministeringPolicy)]
[McpServerToolType]
public static class GroupAdministration
{
    [McpServerTool(
        Name = "create_group",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Makes a heading to list projects under — one product's environments, one
        customer's applications. It is the operator's own word for a set of
        projects that belong together.

        A group carries a name and nothing else: no retention, no token, no
        settings its projects inherit. Creating one lists nothing under it;
        move_project_to_group does that.
        """)]
    public static async Task<AdministeredGroup> CreateAsync(
        CreateGroup create,
        [Description("What to call it. Group names are unique across the installation.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AName(name, Group.NameMaxLength);

        return await create.ExecuteAsync(wanted, cancellationToken) is { } group
            ? AdministeredGroup.Of(group)
            : throw Refused.GroupNameTaken(wanted);
    }

    [McpServerTool(
        Name = "rename_group",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Gives a group another name. Its identity does not change, so the projects
        listed under it stay listed under it.
        """)]
    public static async Task<AdministeredGroup> RenameAsync(
        RenameGroup rename,
        ListGroups groups,
        [Description("The group to rename, as get_settings gives it.")]
        Guid groupId,
        [Description("What it should be called instead.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var wanted = Given.AName(name, Group.NameMaxLength);

        return await rename.ExecuteAsync(groupId, wanted, cancellationToken) switch
        {
            RenameGroupOutcome.Renamed => AdministeredGroup.Of(
                await FoundAsync(groups, groupId, cancellationToken)),
            RenameGroupOutcome.NameTaken => throw Refused.GroupNameTaken(wanted),
            _ => throw Refused.NoSuchGroup(groupId),
        };
    }

    [McpServerTool(
        Name = "delete_group",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Removes a heading. Nothing goes with it: the projects listed under it
        come out of the group and carry on, with their entries, their tokens and
        their windows untouched. This is not one of the four acts that destroy
        data, because a group holds none.
        """)]
    public static async Task<RemovedAnswer> DeleteAsync(
        DeleteGroup delete,
        ListGroups groups,
        [Description("The group to remove, as get_settings gives it.")]
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        // Read before the removal, because afterwards nothing can say what the
        // group was called — and because it is the same read that decides
        // whether there was one to remove at all.
        var group = await FoundAsync(groups, groupId, cancellationToken);

        if (!await delete.ExecuteAsync(groupId, cancellationToken))
        {
            throw Refused.NoSuchGroup(groupId);
        }

        return new RemovedAnswer { Id = group.Id, Name = group.Name };
    }

    /// <remarks>
    /// There is no act that reads one group: a group is a name and a list of
    /// them is the whole of what the application layer offers, so this reads the
    /// list the settings screen reads and finds the row in it. Inventing a
    /// single-group query here would be this adapter holding a read of its own
    /// (ADR 0030), for a list an installation holds a handful of.
    /// </remarks>
    private static async Task<ListedGroup> FoundAsync(
        ListGroups groups, Guid groupId, CancellationToken cancellationToken) =>
        (await groups.ExecuteAsync(cancellationToken))
            .FirstOrDefault(group => group.Id == groupId)
        ?? throw Refused.NoSuchGroup(groupId);
}

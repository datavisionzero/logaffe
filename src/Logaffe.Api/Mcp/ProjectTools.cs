using System.ComponentModel;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The tool an agent starts at.
/// </summary>
/// <remarks>
/// It is a read and only a read. Creating, renaming and deleting a project are
/// not offered here as anything — not read-only, not confirmable, not behind a
/// setting — because they are absent from this interface rather than forbidden
/// on it (ADR 0018).
/// <para>
/// <b>A reading token and nothing else is offered this.</b> An administering
/// token authenticates at the same endpoint and is handed a tool list that does
/// not contain it — absent rather than present and refusing, which is what keeps
/// a session that can act from ever holding a log line (ADR 0046).
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.ReadingPolicy)]
[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(
        Name = "list_projects",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Every project in this logaffe installation, with the identity that names
        it in the other tools, the group it is listed under when it is in one,
        the retention window that says how far back it can be asked about, and
        when it last received an entry. Call this first: every other tool reads
        one project and is given it by identity.
        """)]
    public static async Task<ProjectsAnswer> ListAsync(
        ListProjects projects, ListGroups groups, CancellationToken cancellationToken)
    {
        var held = await projects.ExecuteAsync(cancellationToken);

        // The group rides on the project rather than being a tool of its own:
        // an agent asked about "the production one of shop" resolves it here,
        // and a fifth tool would be a second read path for a fact this one
        // already carries (`docs/mcp.md`). Groups with no projects in them are
        // in the answer this reads and simply name nothing below.
        var names = (await groups.ExecuteAsync(cancellationToken))
            .ToDictionary(group => group.Id, group => group.Name);

        return new ProjectsAnswer([.. held.Select(project => AgentProject.Of(
            project,
            project.GroupId is { } id && names.TryGetValue(id, out var name) ? name : null))]);
    }
}

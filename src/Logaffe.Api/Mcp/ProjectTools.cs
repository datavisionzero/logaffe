using System.ComponentModel;
using Logaffe.Application.Operations;
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
/// </remarks>
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
        it in the other tools and the retention window that says how far back it
        can be asked about. Call this first: every other tool reads one project
        and is given it by identity.
        """)]
    public static async Task<ProjectsAnswer> ListAsync(
        ListProjects projects, CancellationToken cancellationToken)
    {
        var held = await projects.ExecuteAsync(cancellationToken);

        return new ProjectsAnswer([.. held.Select(AgentProject.Of)]);
    }
}

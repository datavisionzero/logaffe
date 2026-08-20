using System.ComponentModel;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The tool an administering agent starts at, and the only read on its surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>An administering token reads the surface it edits</b> and nothing else.
/// Renaming presupposes listing, and without this the other twenty tools would
/// be twenty acts with no way to name what they act on. What makes that safe is
/// that every name in the answer was written by the operator — projects, groups
/// and hosts are named by them, and a sample carries no free text (ADR 0044) —
/// so an administering session holds no sentence anybody else wrote (ADR 0046).
/// </para>
/// <para>
/// <b>No entry and no token value.</b> There is no tool here that returns a log
/// line, and the one number about entries this surface answers with is
/// <c>count_entries_outside_window</c>. A token appears at the moment it is
/// issued and never again, so what this counts is that a project holds tokens
/// and when each was last used — which is what a rotation needs to know and is
/// not the token.
/// </para>
/// <para>
/// <b>An administering token and nothing else is offered this.</b> A reading
/// token authenticates at the same endpoint and is handed a tool list that does
/// not contain it, the same way round as the five.
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.AdministeringPolicy)]
[McpServerToolType]
public static class SettingsTools
{
    [McpServerTool(
        Name = "get_settings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Everything this installation is configured to hold: its groups, its
        projects with the group each is listed under, the machine each runs on
        and how long each keeps its entries, its hosts, and the one window that
        says how long host samples are kept. Call this first: every other tool
        names what it acts on by an identity out of this answer.

        It also says, per project and per host, which tokens exist and when each
        was last used — never their values. A token is readable at the moment it
        is issued and never again; an operator who has lost one reads it back in
        their browser.

        There are no log entries here and no tool on this surface returns one.
        """)]
    public static async Task<SettingsAnswer> GetAsync(
        ListProjects projects,
        ListGroups groups,
        ListHosts hosts,
        ListIngestTokens ingestTokens,
        ListHostTokens hostTokens,
        ChangeSampleRetention samples,
        CancellationToken cancellationToken)
    {
        var heldProjects = await projects.ExecuteAsync(cancellationToken);
        var heldGroups = await groups.ExecuteAsync(cancellationToken);
        var heldHosts = await hosts.ExecuteAsync(cancellationToken);
        var sampleWindow = await samples.ReadAsync(cancellationToken);

        // The tokens are asked for one project and one host at a time, because
        // that is the act the application layer has: `ListIngestTokens` answers
        // for one project. A query that fetched every token in one statement
        // would be behaviour this adapter invented, and the counts the settings
        // screens read come from the same acts (ADR 0030). An installation has
        // a handful of projects, so the tree is a handful of reads.
        var settingsProjects = new List<SettingsProject>(heldProjects.Count);
        foreach (var project in heldProjects)
        {
            var held = await ingestTokens.ExecuteAsync(project.Id, cancellationToken) ?? [];

            settingsProjects.Add(new SettingsProject
            {
                Id = project.Id,
                Name = project.Name,
                GroupId = project.GroupId,
                HostId = project.HostId,
                RetentionDays = project.Retention.Days,
                CreatedAt = project.CreatedAt,
                LastReceivedAt = project.LastReceivedAt,
                IngestTokens = [.. held.Select(AdministeredToken.Of)],
            });
        }

        var settingsHosts = new List<SettingsHost>(heldHosts.Count);
        foreach (var host in heldHosts)
        {
            var held = await hostTokens.ExecuteAsync(host.Id, cancellationToken) ?? [];

            settingsHosts.Add(new SettingsHost
            {
                Id = host.Id,
                Name = host.Name,
                CreatedAt = host.CreatedAt,
                LastReportedAt = host.LastReportedAt,
                Projects = host.Projects,
                HostTokens = [.. held.Select(AdministeredToken.Of)],
            });
        }

        return new SettingsAnswer
        {
            Groups =
            [
                .. heldGroups.Select(group => new SettingsGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    CreatedAt = group.CreatedAt,
                }),
            ],
            Projects = settingsProjects,
            Hosts = settingsHosts,
            SampleRetentionDays = sampleWindow.Days,
        };
    }
}

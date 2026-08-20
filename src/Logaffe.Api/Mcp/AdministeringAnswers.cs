using System.ComponentModel;
using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.Api.Mcp;

/// <summary>
/// One token a project or a host holds, as <c>get_settings</c> says it exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no token in it.</b> The identity is what
/// <c>revoke_ingest_token</c> and <c>revoke_host_token</c> are asked with, the
/// dates are what says whether a credential is still in use, and the value
/// itself appears at the moment it is issued and never again — not here and not
/// anywhere on this surface (ADR 0046). The identifier the operator's own list
/// shows is left out for the same reason it is a weaker one: it is a piece of
/// the token's text, and nothing here needs it.
/// </para>
/// </remarks>
public sealed record AdministeredToken
{
    [Description("Names this token in revoke_ingest_token and revoke_host_token.")]
    public required Guid Id { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    [Description(
        "When a sender last presented this token, or absent when none ever has. "
        + "It is written to within five minutes and is not finer than that, so a "
        + "token used a moment ago may still say nothing. One that has said "
        + "nothing for months is one to ask the operator about.")]
    public DateTimeOffset? LastUsedAt { get; init; }

    public static AdministeredToken Of(ListedIngestToken token) => new()
    {
        Id = token.Id,
        IssuedAt = token.IssuedAt,
        LastUsedAt = token.LastUsedAt,
    };

    public static AdministeredToken Of(ListedHostToken token) => new()
    {
        Id = token.Id,
        IssuedAt = token.IssuedAt,
        LastUsedAt = token.LastUsedAt,
    };
}

/// <summary>
/// One project, as the administering surface reads and writes it.
/// </summary>
/// <remarks>
/// <b>The group and the host are identities here, where <c>list_projects</c>
/// gives the group as a name.</b> That is not an inconsistency between the two
/// surfaces, it is the same rule applied twice: a field carries the value the
/// tools that take it are asked with. Nothing on the reading surface takes a
/// group, so a name is the only useful form of it there; here
/// <c>move_project_to_group</c> takes one, and the names are on
/// <see cref="SettingsAnswer.Groups"/> beside it.
/// </remarks>
public sealed record AdministeredProject
{
    [Description("Names this project in every tool that reads or changes one.")]
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    [Description(
        "The group this project is listed under, or absent when it is in none. "
        + "Pass it, or nothing, to move_project_to_group.")]
    public Guid? GroupId { get; init; }

    [Description(
        "The machine this project runs on, or absent when the operator tracks "
        + "none for it. Pass it, or nothing, to put_project_on_host.")]
    public Guid? HostId { get; init; }

    [Description(
        "How long the project keeps its entries, counted from receipt. "
        + "extend_project_retention raises it and shorten_project_retention "
        + "lowers it.")]
    public required int RetentionDays { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public static AdministeredProject Of(Project project) => new()
    {
        Id = project.Id,
        Name = project.Name,
        GroupId = project.GroupId,
        HostId = project.HostId,
        RetentionDays = project.Retention.Days,
        CreatedAt = project.CreatedAt,
    };
}

/// <summary>
/// One project on <c>get_settings</c>: the row, and what it holds.
/// </summary>
public sealed record SettingsProject
{
    [Description("Names this project in every tool that reads or changes one.")]
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    [Description(
        "The group this project is listed under, or absent when it is in none. "
        + "Pass it, or nothing, to move_project_to_group.")]
    public Guid? GroupId { get; init; }

    [Description(
        "The machine this project runs on, or absent when the operator tracks "
        + "none for it. Pass it, or nothing, to put_project_on_host.")]
    public Guid? HostId { get; init; }

    [Description(
        "How long the project keeps its entries, counted from receipt. "
        + "extend_project_retention raises it and shorten_project_retention "
        + "lowers it.")]
    public required int RetentionDays { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    [Description(
        "When this project last received an entry, or absent when it never has. "
        + "A project that was set up and never delivered to is the commonest "
        + "thing wrong with an installation, and this is where it shows.")]
    public DateTimeOffset? LastReceivedAt { get; init; }

    [Description(
        "The ingest tokens this project can receive on, without their values. "
        + "Empty means nothing can deliver to it yet; issue_ingest_token is what "
        + "changes that. A project holds at most two, which is what makes a "
        + "rotation possible without a gap.")]
    public required IReadOnlyList<AdministeredToken> IngestTokens { get; init; }
}

/// <summary>One group on <c>get_settings</c>.</summary>
/// <remarks>
/// A name and nothing else (ADR 0039). There is no retention its projects
/// inherit and no token on it, which is why deleting one is not destructive: the
/// projects come out of it and stay.
/// </remarks>
public sealed record SettingsGroup
{
    [Description("Names this group in move_project_to_group, rename_group and delete_group.")]
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One host on <c>get_settings</c>.</summary>
public sealed record SettingsHost
{
    [Description("Names this machine in put_project_on_host and the host tools.")]
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    [Description(
        "When a collector on this machine last reported, or absent when none "
        + "ever has. A host with a token and nothing reported is a collector "
        + "that was never started or cannot reach this installation.")]
    public DateTimeOffset? LastReportedAt { get; init; }

    [Description("How many projects say they run on this machine.")]
    public required int Projects { get; init; }

    [Description(
        "The host tokens a collector on this machine can report on, without "
        + "their values. issue_host_token is what makes one.")]
    public required IReadOnlyList<AdministeredToken> HostTokens { get; init; }
}

/// <summary>
/// The whole administering surface in one answer.
/// </summary>
/// <remarks>
/// <para>
/// One tool rather than one per list, for the reason <c>list_projects</c> carries
/// the group and the host rather than leaving them to tools of their own: this is
/// a tree that fits in one answer, and a second read path for a fact the first
/// already carries is another thing to keep in step (<c>docs/mcp.md</c>).
/// </para>
/// <para>
/// <b>It is not called <c>list_projects</c>.</b> That name is taken on the other
/// surface by a tool that answers differently, and an operator who has wired up
/// both agents should not meet one word meaning two things. The identities are
/// the same ones, so the two are looking at one installation.
/// </para>
/// <para>
/// <b>No token value and no entry.</b> Every name in it was written by the
/// operator — projects, groups and hosts are named by them, and a sample carries
/// no free text — which is the sentence that makes reading the surface it edits
/// safe, and which stops holding the moment anything here arrives from outside
/// (ADR 0046).
/// </para>
/// </remarks>
public sealed record SettingsAnswer
{
    [Description(
        "The headings projects are listed under. A group holds nothing itself: "
        + "no retention, no token, no projects of its own beyond the ones "
        + "pointing at it.")]
    public required IReadOnlyList<SettingsGroup> Groups { get; init; }

    public required IReadOnlyList<SettingsProject> Projects { get; init; }

    public required IReadOnlyList<SettingsHost> Hosts { get; init; }

    [Description(
        "How long the installation keeps host samples. It is one window for the "
        + "whole installation rather than one per host, and "
        + "extend_sample_retention and shorten_sample_retention are what move it.")]
    public required int SampleRetentionDays { get; init; }
}

/// <summary>What a group act answers with: the row as it now stands.</summary>
public sealed record AdministeredGroup
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public static AdministeredGroup Of(Group group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        CreatedAt = group.CreatedAt,
    };

    public static AdministeredGroup Of(ListedGroup group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        CreatedAt = group.CreatedAt,
    };
}

/// <summary>What a host act answers with: the row as it now stands.</summary>
public sealed record AdministeredHost
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public static AdministeredHost Of(Domain.Hosts.Host host) => new()
    {
        Id = host.Id,
        Name = host.Name,
        CreatedAt = host.CreatedAt,
    };

    public static AdministeredHost Of(ListedHost host) => new()
    {
        Id = host.Id,
        Name = host.Name,
        CreatedAt = host.CreatedAt,
    };
}

/// <summary>
/// What a deletion answers with: what is gone, by identity and by name.
/// </summary>
/// <remarks>
/// The name is here because after the row is removed nothing can say it, and an
/// agent reporting back to the operator that it deleted <c>orders</c> is saying
/// something they can check. It is read before the removal, which is the same
/// read that decides whether there was anything to remove at all.
/// </remarks>
public sealed record RemovedAnswer
{
    public required Guid Id { get; init; }

    [Description("What the thing that is now gone was called.")]
    public required string Name { get; init; }
}

/// <summary>
/// What revoking a token answers with: the identity that no longer names one.
/// </summary>
/// <remarks>
/// There is nothing else it could carry. The token's value was never on this
/// surface, and a revoked row leaves nothing behind — an agent token is ended by
/// removing it, and so are these (<c>docs/projects.md</c>).
/// </remarks>
public sealed record RevokedAnswer
{
    public required Guid Id { get; init; }
}

/// <summary>
/// What the installation's sample window answers with, after it has been moved.
/// </summary>
public sealed record SampleRetentionAnswer
{
    [Description("How long the installation now keeps host samples.")]
    public required int RetentionDays { get; init; }
}

/// <summary>
/// What a proposed window would drop, asked before anything drops.
/// </summary>
/// <remarks>
/// A number and no entry, which is why a count is on this surface at all: it
/// answers the useful half of a shortening for an agent that may not perform one
/// (<c>docs/mcp.md</c>).
/// </remarks>
public sealed record EntriesOutsideWindowAnswer
{
    [Description("The window that was proposed, repeated back.")]
    public required int RetentionDays { get; init; }

    [Description(
        "How many entries the project holds that this window would put outside "
        + "it. They are removed by the sweep after the window is changed, not by "
        + "this call, which changes nothing.")]
    public required long Entries { get; init; }
}

/// <summary>
/// A token, at the one moment its value exists to be handed over.
/// </summary>
/// <remarks>
/// <para>
/// <b>The value is here and nowhere else.</b> Issuing is the only act on this
/// surface that produces one; there is no tool over <c>ReadTokenBack</c>, and
/// <c>get_settings</c> counts a project's tokens without carrying any of them.
/// Recovering a token afterwards is an errand at a browser (ADR 0022).
/// </para>
/// <para>
/// The snippet beside it is the finished thing to paste rather than the parts to
/// assemble, exactly as the operator's own screen hands it over — and it carries
/// the token inside it, which is the other reason nothing reads one back.
/// </para>
/// </remarks>
public sealed record IssuedTokenAnswer
{
    [Description("Names this token in the matching revoke tool.")]
    public required Guid Id { get; init; }

    [Description(
        "The token itself. It is shown once: hand it to the operator now, "
        + "because nothing on this surface can produce it again.")]
    public required string Token { get; init; }

    [Description(
        "The finished command to hand the operator, with this installation's "
        + "address and this token already in it: the delivery snippet for an "
        + "ingest token, the command that starts a collector for a host token. "
        + "It contains the token, which is the other reason nothing reads one "
        + "back.")]
    public required string Snippet { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public static IssuedTokenAnswer Of(IssuedToken token, string snippet) => new()
    {
        Id = token.Id,
        Token = token.Token.Text,
        Snippet = snippet,
        IssuedAt = token.IssuedAt,
    };
}

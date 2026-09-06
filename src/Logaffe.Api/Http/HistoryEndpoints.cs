using Logaffe.Application.Operations;
using Logaffe.Domain.History;
using Logaffe.Domain.Identities;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// One thing somebody changed.
/// </summary>
/// <param name="ActorKind">
/// Whether a person or an agent was holding the keyboard. It is here because an
/// agent acts with its owner's authority, so a row naming the owner and not
/// saying that would be true and misleading at once
/// ([ADR 0052](../../../docs/adr/0052-a-user-and-an-agent-are-one-identity.md)).
/// </param>
public sealed record ChangeResponse(
    long Id,
    Guid ActorId,
    IdentityKind ActorKind,
    string ActorName,
    DateTimeOffset At,
    Subject Subject,
    Guid? SubjectId,
    string SubjectName,
    Act Act,
    string? Field,
    string? From,
    string? To);

/// <summary>
/// What was changed on this installation and by whom.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an administrator's.</b> The rows are about the installation's
/// configuration, which is what the role is for — and unlike a project's entries
/// they are not narrowed by a reach: a change to a project somebody cannot open
/// is still a change to this installation, and the row carries a name rather
/// than anything the project holds
/// ([ADR 0055](../../../docs/adr/0055-project-access-is-one-filter.md)).
/// </para>
/// <para>
/// <b>It is read-only, and there is no act anywhere that edits or removes a
/// row.</b> What removes one is the subject going, or Host Recovery removing the
/// identity it names.
/// </para>
/// </remarks>
public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/history", async (
                long? before,
                ReadTheHistory read,
                CancellationToken cancellationToken) =>
            {
                var page = await read.ExecuteAsync(before, cancellationToken);

                return Results.Ok(page.Select(change => new ChangeResponse(
                    change.Id,
                    change.ActorId,
                    change.ActorKind,
                    change.ActorName,
                    change.At,
                    change.Subject,
                    change.SubjectId,
                    change.SubjectName,
                    change.Act,
                    change.Field,
                    change.From,
                    change.To)));
            })
            .WithName("ReadHistory")
            .WithSummary("What was changed on this installation, newest first.")
            .RequireAuthorization(SessionAuthentication.AdministratorPolicy)
            .RequireRateLimiting(PublicRateLimits.Operator)
            .Produces<IEnumerable<ChangeResponse>>();

        return endpoints;
    }
}

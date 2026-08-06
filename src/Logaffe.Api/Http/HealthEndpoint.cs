using Logaffe.Application.Operations;

namespace Logaffe.Api.Http;

/// <summary>
/// One unauthenticated endpoint answering 200 or 503, and nothing else.
/// </summary>
/// <remarks>
/// It is public because a Compose healthcheck and a reverse proxy both need to
/// reach it without credentials, and it says nothing because it sits on the open
/// internet: no version, no migration state, no database detail, no uptime. A
/// stranger learns from it exactly what they would learn by loading the sign-in
/// page, which is that a logaffe is here.
/// </remarks>
public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async (CheckReadiness readiness, CancellationToken cancellationToken) =>
            {
                var state = await readiness.ExecuteAsync(cancellationToken);

                // Which of the two unready states it is stays here. During a long
                // migration 503 is the honest answer, since nothing can be served yet.
                return state is Readiness.Ready
                    ? Results.StatusCode(StatusCodes.Status200OK)
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("Health")
            .WithSummary("Whether the installation can serve.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}

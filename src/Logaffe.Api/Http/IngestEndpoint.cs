using System.IO.Compression;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// One line of a delivery that was not an entry.
/// </summary>
/// <param name="Line">
/// Counted from one over the lines of the body, so it is the line the sender can
/// go and look at.
/// </param>
public sealed record RejectedLineResponse(int Line, string Reason);

/// <summary>
/// What a delivery is answered with.
/// </summary>
/// <remarks>
/// <b>Nothing in a sender's control flow depends on this.</b> There is no
/// acknowledgement to wait on, no receipt to store and no confirmation semantic —
/// delivery is fire-and-forget, and this body exists for the one moment anybody
/// reads it, which is a person wiring up a new integration with <c>curl</c>
/// (ADR 0006).
/// </remarks>
/// <param name="Rejected">
/// How many lines were not entries. Every one of them is counted; the first few
/// are also named in <paramref name="Reasons"/>.
/// </param>
public sealed record DeliveryReceiptResponse(
    int Accepted, int Rejected, IReadOnlyList<RejectedLineResponse> Reasons);

/// <summary>
/// Where log entries arrive: the one endpoint every sender ever talks to.
/// </summary>
/// <remarks>
/// <para>
/// <c>VISION.md</c> makes this path the adoption barrier and judges every
/// decision on it by how easily an application that writes log files today can
/// start delivering. So it takes newline-delimited CLEF over an ordinary
/// <c>POST</c> with a bearer token
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0004-the-ingestion-format-is-clef-and-the-server-renders.md">ADR 0004</see>),
/// which is a format a person can write by hand and a curl line can send.
/// </para>
/// <para>
/// <b>The answers are the whole contract.</b> A batch that could be read even in
/// part is <c>200</c> with the counts. A bad token is <c>401</c> and says nothing
/// further — not whether the project exists, not whether the token once did, not
/// whether it was revoked. Over the hard limit is <c>413</c>, over the throttle
/// is <c>429</c>, and a store that cannot be reached is <c>503</c> with the batch
/// gone. That last one is what fire-and-forget means, and it is the reason the
/// application still has its file.
/// </para>
/// <para>
/// It is the only public surface that is neither behind the operator's session
/// nor part of the claim, so the rate limit it carries is the whole of what
/// stands between an unauthenticated flood and the work of authenticating it.
/// </para>
/// </remarks>
public static class IngestEndpoint
{
    private const string Gzip = "gzip";

    public static IEndpointRouteBuilder MapIngest(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(DeliverySnippet.IngestPath, async (
                HttpContext context,
                AuthenticateToken authenticate,
                IngestBatch ingest,
                ILogger<DeliveryReceiptResponse> log,
                CancellationToken cancellationToken) =>
            {
                // First, and before the body is touched: an unadmitted delivery
                // costs one lookup and one comparison, and never the reading of
                // five mebibytes.
                var projectId = await authenticate.AdmittedProjectAsync(
                    context.Request.Headers.Authorization, cancellationToken);

                if (projectId is null)
                {
                    return Results.Unauthorized();
                }

                BatchReceipt receipt;
                try
                {
                    receipt = await ReadAsync(
                        context.Request, ingest, projectId.Value, cancellationToken);
                }
                catch (InvalidDataException)
                {
                    // A body that said it was gzip and was not. It is the one
                    // thing wrong with the request rather than with the entries
                    // in it, and there is no partial reading of it to salvage.
                    return Results.BadRequest();
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    // Nowhere to store it, and the batch is gone. It is written
                    // to logaffe's own file log, because the failures worth
                    // diagnosing here are exactly the ones in which logaffe
                    // could not record anything (ADR 0002).
                    log.LogError(
                        failure,
                        "A delivery to project {ProjectId} could not be stored.",
                        projectId);

                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                if (receipt.Outcome is BatchOutcome.OverTheHardLimit)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                return Results.Ok(new DeliveryReceiptResponse(
                    receipt.Accepted,
                    receipt.Rejected,
                    [.. receipt.Reasons.Select(
                        rejection => new RejectedLineResponse(rejection.Line, rejection.Reason))]));
            })
            .RequireRateLimiting(PublicRateLimits.Ingest)
            .WithName("Ingest")
            .WithSummary("Takes a batch of log entries for the project its token names.")
            .Accepts<string>(DeliverySnippet.ContentType)
            .Produces<DeliveryReceiptResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>
    /// The body as entries, decompressed first where the sender compressed it.
    /// </summary>
    /// <remarks>
    /// Handing the act a decompressing stream rather than a decompressed body is
    /// what makes the size cap count the decompressed bytes without ever holding
    /// them: the act stops reading at the cap, so a compression bomb is refused
    /// after five mebibytes of it have come out and not after all of it has.
    /// </remarks>
    private static async Task<BatchReceipt> ReadAsync(
        HttpRequest request,
        IngestBatch ingest,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.ContentEncoding.Any(
                encoding => string.Equals(encoding, Gzip, StringComparison.OrdinalIgnoreCase)))
        {
            return await ingest.ExecuteAsync(projectId, request.Body, cancellationToken);
        }

        // Left open, because the request's own body belongs to the server and
        // not to this.
        await using var decompressed =
            new GZipStream(request.Body, CompressionMode.Decompress, leaveOpen: true);

        return await ingest.ExecuteAsync(projectId, decompressed, cancellationToken);
    }
}

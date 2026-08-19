using Logaffe.Application.Operations;
using Logaffe.Domain.Hosts;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// Why a delivery was not a reading.
/// </summary>
/// <remarks>
/// <b>Nothing in a collector's control flow depends on this.</b> A collector
/// does not wait for it, does not read it and does not retry — it drops the
/// sample and takes the next one a minute later. It exists for the one moment
/// anybody reads it, which is a person wiring a collector up by hand with
/// <c>curl</c> and wanting to know which member they got wrong.
/// </remarks>
public sealed record SampleRejectionResponse(string Reason);

/// <summary>
/// Where samples arrive: the one endpoint every collector ever talks to.
/// </summary>
/// <remarks>
/// <para>
/// It is <see cref="IngestEndpoint"/>'s shape with the batching taken out. A
/// collector posts one reading, because it buffers nothing, retries nothing and
/// has nothing to catch up on — batching exists on the log path because an
/// application produces entries faster than it should open connections, and a
/// machine produces one reading a minute (<c>docs/metrics.md</c>).
/// </para>
/// <para>
/// <b>The answers are the whole contract.</b> A reading is <c>204</c> and no
/// body, because there is nothing to say about a stored sample that a collector
/// would do anything with. A body that is not a reading is <c>400</c> naming the
/// member at fault, and it is refused whole — half a sample, memory without
/// processor, is a band with a hole in it that looks like data (ADR 0006 is
/// about the other path and says so). A bad token is <c>401</c> and says nothing
/// further. Over the size cap is <c>413</c>, over the throttle is <c>429</c>, and
/// a store that cannot be reached is <c>503</c> with the reading gone and the
/// collector none the wiser.
/// </para>
/// <para>
/// <b>There is no timestamp on the wire and no compression on this path.</b> The
/// first is the single clock made visible (ADR 0044's companion argument in
/// <c>docs/metrics.md</c>): the installation stamps the sample when it arrives.
/// The second is arithmetic — a reading is a few hundred bytes, and gzip framing
/// on it costs more than it saves.
/// </para>
/// </remarks>
public static class SampleEndpoint
{
    public static IEndpointRouteBuilder MapSamples(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CollectorCommand.SamplePath, async (
                HttpContext context,
                AuthenticateToken authenticate,
                IngestSample ingest,
                ILogger<SampleRejectionResponse> log,
                CancellationToken cancellationToken) =>
            {
                // First, and before the body is touched, exactly as a delivery of
                // entries is: an unadmitted collector costs one lookup and one
                // comparison and never the reading of a body.
                var hostId = await authenticate.AdmittedHostAsync(
                    context.Request.Headers.Authorization, cancellationToken);

                if (hostId is null)
                {
                    return Results.Unauthorized();
                }

                // What the sender said it was sending, believed only when it is
                // over the cap: a body that announces more than a reading can be
                // is refused without reading any of it.
                if (context.Request.ContentLength > Sampling.SampleBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                var body = await ReadAsync(context.Request.Body, cancellationToken);

                SampleReceipt receipt;
                try
                {
                    receipt = await ingest.ExecuteAsync(hostId.Value, body, cancellationToken);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    // Nowhere to store it, and the reading is gone. It is written
                    // to logaffe's own file log for the ingest path's reason: the
                    // failures worth diagnosing here are the ones in which
                    // logaffe could not record anything (ADR 0002).
                    log.LogError(
                        failure,
                        "A sample from host {HostId} could not be stored.",
                        hostId);

                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                return receipt.Outcome switch
                {
                    SampleOutcome.OverTheHardLimit =>
                        Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
                    SampleOutcome.NotAReading =>
                        Results.BadRequest(new SampleRejectionResponse(receipt.Reason)),

                    // Nothing to hand back. A collector that got a body here
                    // would have somewhere to put a retry, and there is no retry.
                    _ => Results.NoContent(),
                };
            })
            .RequireRateLimiting(PublicRateLimits.Sample)
            .WithName("IngestSample")
            .WithSummary("Takes one reading from the collector on the host its token names.")
            .Accepts<string>(CollectorCommand.ContentType)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<SampleRejectionResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>
    /// The body, up to one byte past the cap.
    /// </summary>
    /// <remarks>
    /// The extra byte is what tells a reading at exactly the cap from one that
    /// runs past it, and stopping there is what keeps a sender that lied about
    /// its <c>Content-Length</c> from being read at all. The act decides what to
    /// make of the length, so the cap is stated once (<see cref="Sampling"/>) and
    /// enforced where the bytes are.
    /// </remarks>
    private static async Task<ReadOnlyMemory<byte>> ReadAsync(
        Stream body, CancellationToken cancellationToken)
    {
        var buffer = new byte[Sampling.SampleBytes + 1];
        var filled = 0;

        while (filled < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(filled), cancellationToken);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return buffer.AsMemory(0, filled);
    }
}

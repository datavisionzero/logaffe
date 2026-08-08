using System.IO.Compression;
using System.Net;
using System.Text;

namespace Logaffe.UnitTests.Client;

/// <summary>
/// One delivery, as the installation would have received it.
/// </summary>
public sealed record Delivered(
    string Body,
    bool Gzipped,
    string? Authorization,
    string? ContentType)
{
    /// <summary>The CLEF lines the batch carried, in order.</summary>
    public string[] Lines =>
        Body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// An installation that is not there, standing in for the one the client talks
/// to.
/// </summary>
/// <remarks>
/// It records what arrived rather than answering it, because everything worth
/// asking of this client is about what it sends and what it does when the
/// sending goes wrong — nothing in a sender's control flow depends on the answer
/// (ADR 0006).
/// </remarks>
public sealed class TakingDeliveries : HttpMessageHandler
{
    private readonly List<Delivered> _taken = [];
    private readonly Lock _guard = new();

    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>What to answer with, when it answers at all.</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>The receipt body, which a sender reads only to report it.</summary>
    public string Receipt { get; set; } = """{"accepted":0,"rejected":0,"reasons":[]}""";

    /// <summary>Thrown instead of answering, for the installation that is down.</summary>
    public Exception? Fails { get; set; }

    /// <summary>Held closed to keep a delivery in flight while a test fills the queue.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public IReadOnlyList<Delivered> Taken
    {
        get
        {
            lock (_guard)
            {
                return [.. _taken];
            }
        }
    }

    /// <summary>
    /// Waits for the <paramref name="ordinal"/>th delivery, counting from one.
    /// </summary>
    /// <remarks>
    /// By count rather than by "the next one", so that a delivery arriving
    /// between a test's send and its wait is caught rather than waited past.
    /// </remarks>
    public async Task<Delivered> TakeAsync(int ordinal)
    {
        while (true)
        {
            Task arrived;

            lock (_guard)
            {
                if (_taken.Count >= ordinal)
                {
                    return _taken[ordinal - 1];
                }

                arrived = _arrived.Task;
            }

            await arrived.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var gzipped = request.Content!.Headers.ContentEncoding.Contains("gzip");
        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var delivered = new Delivered(
            gzipped ? Ungzip(bytes) : Encoding.UTF8.GetString(bytes),
            gzipped,
            request.Headers.Authorization?.ToString(),
            request.Content.Headers.ContentType?.MediaType);

        TaskCompletionSource arrived;

        lock (_guard)
        {
            _taken.Add(delivered);
            arrived = _arrived;
            _arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        arrived.TrySetResult();

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        if (Fails is { } failure)
        {
            throw failure;
        }

        return new HttpResponseMessage(Status)
        {
            Content = new StringContent(Receipt, Encoding.UTF8, "application/json"),
        };
    }

    private static string Ungzip(byte[] body)
    {
        using var compressed = new MemoryStream(body);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var plain = new MemoryStream();

        gzip.CopyTo(plain);

        return Encoding.UTF8.GetString(plain.ToArray());
    }
}

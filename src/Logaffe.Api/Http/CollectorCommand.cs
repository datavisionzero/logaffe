using Logaffe.Domain.Tokens;

namespace Logaffe.Api.Http;

/// <summary>
/// The finished command a host token is handed over with: this installation's
/// address, this token, and the two mounts a container needs to see the machine
/// it is running on.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/metrics.md</c> and <c>docs/ui.md</c> both promise this, and they
/// promise it for the reason <see cref="DeliverySnippet"/> and
/// <see cref="AgentClientConfiguration"/> are promised: the fiddly part is never
/// the token, it is the block around it. Here that block includes two bind
/// mounts and an <c>rslave</c> propagation flag, which is knowledge about
/// containers that has nothing to do with logging and which
/// <c>docs/metrics.md</c> says outright the operator should not have to have.
/// </para>
/// <para>
/// It is assembled here rather than in the act that issues the token, for the
/// one reason that keeps the other two out of the layer below: the
/// installation's own address is something only an adapter knows, and it knows
/// it from the request it is answering. Behind a reverse proxy that is the
/// forwarded address, which is why <see cref="Hosting.RequestSource"/> reads the
/// host and the scheme along with the caller.
/// </para>
/// <para>
/// <b>It names an image and nothing else can stand in for it.</b> That is
/// unlike <see cref="DeliverySnippet"/>, which falls back to a plain request
/// while its packages are unpublished: a collector is a program that reads
/// <c>/proc</c>, and there is no <c>curl</c> line that does its job.
/// </para>
/// </remarks>
public static class CollectorCommand
{
    /// <summary>
    /// Where a sample arrives. It is written down here for the reason
    /// <see cref="DeliverySnippet.IngestPath"/> is: it goes into the
    /// configuration of every collector the operator ever starts, so it is a
    /// promise to every machine already reporting rather than a route that can
    /// be moved.
    /// </summary>
    public const string SamplePath = "/samples";

    /// <summary>One JSON object, which is one reading (<c>docs/metrics.md</c>).</summary>
    public const string ContentType = "application/json";

    /// <summary>
    /// The image a collector runs from. <c>:latest</c> rather than a pinned
    /// version, because <c>docs/deployment.md</c> upgrades the collectors the
    /// way it upgrades the installation — the tag moves when a release is cut,
    /// and each machine is restarted on its own timer.
    /// </summary>
    public const string Image = "ghcr.io/datavisionzero/logaffe-collector:latest";

    /// <summary>
    /// What the operator pastes into a terminal on the machine they want numbers
    /// from.
    /// </summary>
    /// <remarks>
    /// <c>LOGAFFE_MOUNTS</c> is the root filesystem and nothing else. It is the
    /// one value in here an operator is expected to edit — a machine with a
    /// separate data disk names it too — and starting with the one mount every
    /// machine has means the command works unedited, which is the whole point of
    /// handing it over finished.
    /// </remarks>
    public static string For(HttpRequest request, TokenText token) =>
        $"""
         docker run -d --name logaffe-collector --restart unless-stopped \
           -v /proc:/host/proc:ro \
           -v /:/rootfs:ro,rslave \
           -e LOGAFFE_ENDPOINT={request.Scheme}://{request.Host} \
           -e LOGAFFE_HOST_TOKEN={token.Text} \
           -e LOGAFFE_MOUNTS=/ \
           {Image}
         """;
}

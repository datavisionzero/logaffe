using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

namespace Logaffe.Collector;

/// <summary>
/// The installation this collector reports to, and the one thing it does with
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delivery is fire-and-forget</b> (<c>docs/metrics.md</c>): it does not
/// wait, does not retry and learns nothing about whether a sample landed beyond
/// what it puts in its own log. A collector that cannot reach the installation
/// drops the reading and takes the next one a minute later, so a machine that
/// was unreachable for an hour has an hour of gap rather than an hour of samples
/// arriving at once — and a gap is what the band draws.
/// </para>
/// <para>
/// <b>It says so once and not once a minute.</b> An installation down for a
/// night is one line about it going and one about it coming back; sixty lines an
/// hour would bury the one line that says which of them happened.
/// </para>
/// </remarks>
internal sealed class Installation(HttpClient client, Uri endpoint, string token)
{
    /// <summary>Where a sample arrives (<c>docs/metrics.md</c>).</summary>
    private const string SamplePath = "/samples";

    private const string ContentType = "application/json";

    private string _said = string.Empty;

    public async Task DeliverAsync(Reading reading, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, SamplePath))
            {
                Content = new ByteArrayContent(reading.ToJson())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(ContentType) },
                },
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                Reached();
                return;
            }

            Report(await WhyAsync(response, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The collector is stopping, and a sample abandoned on the way out
            // is the ordinary end of one.
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // A timeout arrives as a cancellation that is not this collector's,
            // which is why it is caught beside the request failures rather than
            // above them.
            Report($"This sample did not reach {endpoint}: {exception.Message}");
        }
    }

    /// <summary>
    /// What an answer that is not a `204` means, said in the operator's terms
    /// rather than as a status code on its own.
    /// </summary>
    private static async Task<string> WhyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "This installation does not accept this token. It may have been revoked, "
                + "or the host it belonged to may have been deleted.",

            // The one answer that means this build and that installation
            // disagree about what a reading is — which the additive format is
            // meant to prevent, so it says exactly what was refused.
            HttpStatusCode.BadRequest =>
                "This installation did not read this sample: "
                + await Trimmed(response, cancellationToken),

            HttpStatusCode.TooManyRequests =>
                "This installation is throttling this address. The sample is dropped.",

            HttpStatusCode.ServiceUnavailable =>
                "This installation could not reach its store. The sample is dropped.",

            // Ordinarily an endpoint written as `http` in front of a proxy that
            // only serves `https`. Nothing is posted to where it points.
            >= HttpStatusCode.MovedPermanently and < HttpStatusCode.BadRequest =>
                $"{CollectorSettings.EndpointVariable} redirects to "
                + $"{response.Headers.Location?.ToString() ?? "somewhere else"}, and a sample is "
                + "not posted to an address this one was not given. Set the variable to that "
                + "address.",

            _ => $"This installation answered {(int)response.StatusCode} to this sample.",
        };

    private static async Task<string> Trimmed(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // The reason is a sentence; anything longer is not the answer this
        // expects and is not worth a screenful of somebody else's HTML.
        return body.Length <= 500 ? body : body[..500];
    }

    private void Report(string sentence)
    {
        if (_said == sentence)
        {
            return;
        }

        _said = sentence;
        Say.Line(sentence);
    }

    private void Reached()
    {
        if (_said.Length == 0)
        {
            return;
        }

        _said = string.Empty;
        Say.Line($"Samples are reaching {endpoint} again.");
    }

    /// <summary>
    /// A request that outlives the interval is a request that would still be in
    /// flight when the next reading is taken, and the next reading is the better
    /// one — so it is cut off well inside the minute. Nothing waits for a
    /// retry, because there is none.
    /// </summary>
    public static HttpClient Client() =>

        // **Redirects are not followed**, and this is the one place that
        // matters. Every request carries the host token in a header, and a
        // redirect is somebody else deciding where a credential goes — the
        // handler strips the header across hosts but keeps it within one, so
        // following is a choice rather than a safety. An address that redirects
        // is an address that was written down wrong, and saying so is more use
        // to an operator than quietly posting somewhere else.
        new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", UserAgent() } },
        };

    /// <summary>
    /// The only thing a collector ever says about itself, and the only way an
    /// installation can learn which build is reporting to it.
    /// </summary>
    /// <remarks>
    /// The informational version rather than the assembly's, because that is the
    /// one the release stamps in full: a prerelease keeps its suffix and a trunk
    /// build carries the commit it was built from, both of which an assembly
    /// version rounds off to four numbers. It is what the installation names
    /// itself with in the MCP handshake, for the same reason.
    /// </remarks>
    private static string UserAgent()
    {
        var version = typeof(Installation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return $"logaffe-collector/{version ?? "0.0.0"}";
    }
}

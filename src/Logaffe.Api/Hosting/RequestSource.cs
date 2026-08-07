using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Where a request came from, which two things in the product act on: the
/// throttle that keeps the sign-in path from being a free oracle
/// (ADR 0017), and the column in the session list that is the only way the
/// operator can ever notice a session that is not theirs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Forwarded headers are not trusted unless a proxy is named.</b>
/// <c>X-Forwarded-For</c> is written by whoever sent the request, so honouring
/// it blindly hands both of those to the caller: the throttle is partitioned by
/// a value the attacker chooses, and the session list shows whatever an
/// intruder wanted it to show. An installation that sits behind a reverse proxy
/// says so by configuring <c>Logaffe:TrustedProxies</c>, and one reached
/// directly says nothing and gets the connection's own address.
/// </para>
/// <para>
/// Loopback stays trusted, which is the framework's own default and covers the
/// proxy running in the same Compose network reaching the container over it.
/// </para>
/// </remarks>
public static class RequestSource
{
    /// <summary>
    /// Addresses and networks whose <c>X-Forwarded-For</c> is believed, written
    /// as a comma-separated list of addresses and CIDR ranges.
    /// </summary>
    public const string TrustedProxiesKey = "Logaffe:TrustedProxies";

    public static IServiceCollection AddLogaffeRequestSource(
        this IServiceCollection services, IConfiguration configuration)
    {
        var trusted = (configuration[TrustedProxiesKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            // The host and the scheme along with the caller, because the address
            // an agent's configuration is assembled with is the one the operator
            // reached the installation at rather than the one the container
            // answers on.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;

            foreach (var entry in trusted)
            {
                if (entry.Contains('/'))
                {
                    // Qualified, because the framework carries a second type of
                    // this name that the options no longer take.
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
                }
                else
                {
                    options.KnownProxies.Add(IPAddress.Parse(entry));
                }
            }
        });
    }

    /// <summary>
    /// The address as the product will write it down, or <c>null</c> when there
    /// is none to read — a request over a unix socket, or a test that never had
    /// a connection. What that becomes in the row is
    /// <see cref="Domain.Operators.Session"/>'s business, and it is the word
    /// <c>unknown</c> rather than a blank.
    /// </summary>
    public static string? SeenFrom(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();
}

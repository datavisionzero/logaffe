using System.Net;
using System.Net.Http.Json;
using System.Text;
using Logaffe.Domain.Hosts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The collector's door, asked of an installation that is actually running.
/// </summary>
/// <remarks>
/// <para>
/// It is the second public write surface and the second one an unauthenticated
/// caller can reach, so what is worth a composition root here is the same thing
/// as on the deliveries: <b>that a token that admits nothing is turned away, and
/// that which of the three kinds it was makes no difference to the answer.</b>
/// </para>
/// <para>
/// The rest is what only a real database answers — that one host reporting twice
/// in a minute leaves one row, which is the natural key of
/// <c>docs/storage.md</c> and the rounding in <c>IngestSample</c> doing together
/// what neither does alone.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class SampleEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Reading =
        """
        {"cpu":0.42,"memoryUsed":6115295232,"memoryTotal":16769712128,
         "load1":0.52,"load5":0.61,"load15":0.58,
         "filesystems":[{"mount":"/","used":41234567890,"total":107374182400}]}
        """;

    private readonly string _volume = InstallationVolume.Create(nameof(SampleEndpointTests));

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Postgres", await postgres.CreateDatabaseAsync());
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var enrolled = await AClaimedInstallation.ClaimAsync(_installation, _volume);
        _secondFactorSecret = enrolled.SecondFactorSecret;
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Fact]
    public async Task Nothing_but_a_host_token_admits_a_sample()
    {
        using var operatorClient = await SignedInAsync();
        var project = await CreateProjectAsync(operatorClient, "orders");

        var ingest = await IssueIngestTokenAsync(operatorClient, project.Id);
        var agent = await IssueAgentTokenAsync(operatorClient);

        // Nothing at all, then each of the other two kinds. An ingest token
        // pasted into a collector's configuration is the mistake that will
        // happen, and it is refused on its prefix before the database is asked
        // anything (ADR 0031) — but the answer says none of that.
        foreach (var presented in new[] { null, ingest, agent, "logaffe_host_nonsense" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(presented, Reading));
        }

        // And the operator's own session, which is a person's credential.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/samples")
        {
            Content = new StringContent(Reading, Encoding.UTF8, "application/json"),
        };

        using var refused = await operatorClient.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_reading_is_stored_and_read_back_over_the_range_it_landed_in()
    {
        using var client = await SignedInAsync();
        var host = await CreateHostAsync(client, "web-01");
        var token = await IssueHostTokenAsync(client, host.Id);

        // No body at all on the way in: there is nothing to say about a stored
        // sample that a collector would do anything with.
        Assert.Equal(HttpStatusCode.NoContent, await DeliverAsync(token, Reading));

        var window = await ReadSamplesAsync(client, host.Id);

        // The name rides along with the read, which is what the band over a
        // project's entries has to draw: the project carries the host's
        // identity and nothing that names it.
        Assert.Equal("web-01", window.HostName);

        var bucket = Assert.Single(window.Samples);
        Assert.Equal(0.42, bucket.CpuAverage, 3);
        Assert.Equal(0.42, bucket.CpuPeak, 3);
        Assert.Equal(6115295232, bucket.MemoryUsedAverage);
        Assert.Equal(16769712128, bucket.MemoryTotal);

        var filesystem = Assert.Single(window.Filesystems);
        Assert.Equal("/", filesystem.Mount);
        Assert.Equal(41234567890, filesystem.UsedAverage);
        Assert.Equal(107374182400, filesystem.Total);
    }

    [Fact]
    public async Task One_host_reporting_twice_in_a_minute_leaves_one_reading()
    {
        using var client = await SignedInAsync();
        var host = await CreateHostAsync(client, "web-01");
        var token = await IssueHostTokenAsync(client, host.Id);

        // Two deliveries seconds apart, which is a collector whose timer drifted
        // across a minute boundary and not a machine that doubled. The second is
        // taken as an answer and stored as nothing: which of two readings of one
        // minute is the right one has no answer, so the first one stands.
        Assert.Equal(HttpStatusCode.NoContent, await DeliverAsync(token, Reading));
        Assert.Equal(
            HttpStatusCode.NoContent,
            await DeliverAsync(token, Reading.Replace("0.42", "0.99")));

        var window = await ReadSamplesAsync(client, host.Id);

        var bucket = Assert.Single(window.Samples);
        Assert.Equal(0.42, bucket.CpuAverage, 3);
    }

    [Fact]
    public async Task A_member_the_installation_does_not_know_is_passed_over()
    {
        using var client = await SignedInAsync();
        var host = await CreateHostAsync(client, "web-01");
        var token = await IssueHostTokenAsync(client, host.Id);

        // What makes the format additive, and what keeps a collector working
        // across an upgrade of the installation it reports to: a number this
        // build has never heard of costs the delivery nothing.
        var newer = Reading.Replace(
            "\"cpu\":0.42", "\"cpu\":0.42,\"swapUsed\":123,\"temperature\":41.5");

        Assert.Equal(HttpStatusCode.NoContent, await DeliverAsync(token, newer));

        Assert.Single((await ReadSamplesAsync(client, host.Id)).Samples);
    }

    [Theory]

    // The member that is missing, named — because "not a reading" sends
    // somebody reading their own JSON character by character.
    [InlineData("""{"memoryUsed":1,"memoryTotal":2,"load1":0,"load5":0,"load15":0}""", "cpu")]
    [InlineData("""{"cpu":0.4,"memoryTotal":2,"load1":0,"load5":0,"load15":0}""", "memoryUsed")]

    // A number outside its range is not a large reading, it is not a reading.
    [InlineData(
        """{"cpu":4,"memoryUsed":1,"memoryTotal":2,"load1":0,"load5":0,"load15":0}""", "cpu")]

    // Not JSON at all, which is the one a person wiring a collector up by hand
    // meets first.
    [InlineData("not json", "JSON")]
    public async Task A_body_that_is_not_a_reading_is_refused_whole_and_says_which_member(
        string body, string named)
    {
        using var client = await SignedInAsync();
        var host = await CreateHostAsync(client, "web-01");
        var token = await IssueHostTokenAsync(client, host.Id);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/samples")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("Authorization", $"Bearer {token}");

        using var httpClient = _installation.CreateClient();
        using var response = await httpClient.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var rejection = (await response.Content.ReadFromJsonAsync<RejectionBody>(
            TestContext.Current.CancellationToken))!;

        Assert.Contains(named, rejection.Reason, StringComparison.Ordinal);

        // Nothing was stored. Half a sample is a band with a hole in it that
        // looks like data, so a delivery is taken whole or not at all.
        Assert.Empty((await ReadSamplesAsync(client, host.Id)).Samples);
    }

    [Fact]
    public async Task A_body_larger_than_a_reading_can_be_is_refused_without_being_read()
    {
        using var client = await SignedInAsync();
        var host = await CreateHostAsync(client, "web-01");
        var token = await IssueHostTokenAsync(client, host.Id);

        // A reading is a few hundred bytes. Something this size arriving here is
        // not a collector, whatever it says in its header.
        var oversized = new string('x', Sampling.SampleBytes + 1);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge, await DeliverAsync(token, oversized));
    }

    [Fact]
    public async Task A_revoked_token_admits_nothing_and_the_collector_carries_on()
    {
        using var client = await SignedInAsync();
        var host = await CreateHostAsync(client, "web-01");
        var token = await IssueHostTokenAsync(client, host.Id);

        var held = await ReadAsync<IReadOnlyList<ListedHostTokenBody>>(
            await client.GetAsync(
                $"/hosts/{host.Id}/host-tokens", TestContext.Current.CancellationToken));

        using (var revoked = await client.DeleteAsync(
            $"/host-tokens/{Assert.Single(held).Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        // Revoking removes the row, so the identifier names nothing and the
        // refusal costs what a mismatch costs.
        Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(token, Reading));
    }

    private async Task<HttpStatusCode> DeliverAsync(string? token, string body)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/samples")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (token is not null)
        {
            request.Headers.Add("Authorization", $"Bearer {token}");
        }

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    /// <summary>
    /// A range wide enough to hold whatever minute the installation's own clock
    /// stamped the delivery with — which is the only clock a sample has.
    /// </summary>
    private async Task<SampleWindowBody> ReadSamplesAsync(HttpClient client, Guid hostId)
    {
        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.AddHours(1).ToString("O");

        return await ReadAsync<SampleWindowBody>(await client.GetAsync(
            $"/hosts/{hostId}/samples?from={Uri.EscapeDataString(from)}"
            + $"&to={Uri.EscapeDataString(to)}",
            TestContext.Current.CancellationToken));
    }

    private async Task<HostBody> CreateHostAsync(HttpClient client, string name) =>
        await ReadAsync<HostBody>(await client.PostAsJsonAsync(
            "/hosts", new { name }, TestContext.Current.CancellationToken));

    private async Task<string> IssueHostTokenAsync(HttpClient client, Guid hostId) =>
        (await ReadAsync<IssuedHostTokenBody>(await client.PostAsync(
            $"/hosts/{hostId}/host-tokens", null, TestContext.Current.CancellationToken))).Token;

    private async Task<ProjectBody> CreateProjectAsync(HttpClient client, string name) =>
        await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken));

    private async Task<string> IssueIngestTokenAsync(HttpClient client, Guid projectId) =>
        (await ReadAsync<IssuedTokenBody>(await client.PostAsync(
            $"/projects/{projectId}/ingest-tokens",
            null,
            TestContext.Current.CancellationToken))).Token;

    private async Task<string> IssueAgentTokenAsync(HttpClient client) =>
        (await ReadAsync<IssuedTokenBody>(await client.PostAsJsonAsync(
            "/agent-tokens",
            new { name = "a reader" },
            TestContext.Current.CancellationToken))).Token;

    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password = AClaimedInstallation.TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(
                response.IsSuccessStatusCode,
                $"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}");

            return (await response.Content.ReadFromJsonAsync<T>(
                TestContext.Current.CancellationToken))!;
        }
    }

    private sealed record RejectionBody(string Reason);

    private sealed record HostBody(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record ProjectBody(Guid Id, string Name);

    private sealed record IssuedTokenBody(Guid Id, string Token);

    private sealed record IssuedHostTokenBody(
        Guid Id, string Token, string CollectorCommand, DateTimeOffset IssuedAt);

    private sealed record ListedHostTokenBody(
        Guid Id, string Identifier, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

    private sealed record SampleBucketBody(
        DateTimeOffset Start,
        double CpuAverage,
        double CpuPeak,
        long MemoryUsedAverage,
        long MemoryUsedPeak,
        long MemoryTotal,
        double LoadAverage,
        double LoadPeak);

    private sealed record FilesystemBucketBody(
        DateTimeOffset Start, string Mount, long UsedAverage, long UsedPeak, long Total);

    private sealed record SampleWindowBody(
        string HostName,
        double BucketSeconds,
        IReadOnlyList<SampleBucketBody> Samples,
        IReadOnlyList<FilesystemBucketBody> Filesystems);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The operator's token acts over HTTP, asked of an installation that is
/// actually running.
/// </summary>
/// <remarks>
/// <para>
/// The property worth a test here is not the mapping — that a list endpoint
/// returns a list is visible by reading it — but that <b>every one of these is
/// behind the operator's session</b>. A route that quietly stops requiring one
/// is an installation minting credentials for strangers, and it is a one-line
/// mistake, so it is asked of a running composition root rather than read off a
/// registration.
/// </para>
/// <para>
/// The cookie is carried by hand rather than by a cookie container, because it
/// is issued <c>Secure</c> and these requests are not: what is being checked is
/// that the secret admits, and a container that declined to send it over
/// <c>http</c> would be checking something else.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TokenEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private readonly string _volume = InstallationVolume.Create(nameof(TokenEndpointTests));

    private string _connectionString = null!;
    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        // Read by the composition root before anything a factory could
        // configure, so it goes where the composition root looks.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        // The migrations run as a hosted service, so the first request is what
        // waits for them.
        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await ClaimAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Theory]
    [InlineData("GET", "/agent-tokens")]
    [InlineData("POST", "/agent-tokens")]
    [InlineData("GET", "/agent-tokens/0195f0d4-0000-7000-8000-000000000000/token")]
    [InlineData("PATCH", "/agent-tokens/0195f0d4-0000-7000-8000-000000000000")]
    [InlineData("DELETE", "/agent-tokens/0195f0d4-0000-7000-8000-000000000000")]
    [InlineData("GET", "/projects/0195f0d4-0000-7000-8000-000000000000/ingest-tokens")]
    [InlineData("POST", "/projects/0195f0d4-0000-7000-8000-000000000000/ingest-tokens")]
    [InlineData("GET", "/ingest-tokens/0195f0d4-0000-7000-8000-000000000000/token")]
    [InlineData("DELETE", "/ingest-tokens/0195f0d4-0000-7000-8000-000000000000")]
    public async Task Every_token_endpoint_is_behind_the_operator_s_session(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { name = "claude-code" }),
        };

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_agent_token_is_issued_listed_read_back_and_revoked()
    {
        using var client = await SignedInAsync();

        var issued = await ReadAsync<IssuedAgentToken>(
            await client.PostAsJsonAsync(
                "/agent-tokens",
                new { name = "claude-code" },
                TestContext.Current.CancellationToken));

        Assert.StartsWith(TokenText.AgentPrefix, issued.Token);

        // What the product hands over is the finished configuration, not the
        // bare token: the address and the token already in place.
        var configuration = JsonDocument.Parse(issued.ClientConfiguration)
            .RootElement.GetProperty("mcpServers").GetProperty("logaffe");
        Assert.EndsWith("/mcp", configuration.GetProperty("url").GetString());
        Assert.Equal(
            $"Bearer {issued.Token}",
            configuration.GetProperty("headers").GetProperty("Authorization").GetString());

        // A list carries names and no secrets. Six agent tokens on a screen is
        // six names read, not six secrets.
        var listed = await ReadAsync<ListedAgentToken[]>(
            await client.GetAsync("/agent-tokens", TestContext.Current.CancellationToken));
        var only = Assert.Single(listed);
        Assert.Equal("claude-code", only.Name);
        Assert.Null(only.LastUsedAt);
        Assert.DoesNotContain(issued.Token, await ListBodyAsync(client));

        // Reading it back is its own request, and it produces the same token.
        var readBack = await ReadAsync<ReadAgentToken>(await client.GetAsync(
            $"/agent-tokens/{issued.Id}/token", TestContext.Current.CancellationToken));
        Assert.Equal(issued.Token, readBack.Token);

        using var renamed = await client.PatchAsJsonAsync(
            $"/agent-tokens/{issued.Id}",
            new { name = "the laptop" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        using var revoked = await client.DeleteAsync(
            $"/agent-tokens/{issued.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        // A token already gone is not a failure of anything — a second click, or
        // another tab.
        using var again = await client.DeleteAsync(
            $"/agent-tokens/{issued.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_name_that_is_not_one_is_refused_before_the_domain_backstops_it()
    {
        using var client = await SignedInAsync();

        foreach (var name in new[] { "", "   ", new string('x', AgentToken.NameMaxLength + 1) })
        {
            using var response = await client.PostAsJsonAsync(
                "/agent-tokens", new { name }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_project_holds_two_ingest_tokens_and_a_third_is_refused()
    {
        using var client = await SignedInAsync();
        var project = await ProjectAsync("api");

        var first = await ReadAsync<IssuedIngestToken>(await client.PostAsync(
            $"/projects/{project}/ingest-tokens", null, TestContext.Current.CancellationToken));
        var second = await ReadAsync<IssuedIngestToken>(await client.PostAsync(
            $"/projects/{project}/ingest-tokens", null, TestContext.Current.CancellationToken));

        Assert.StartsWith(TokenText.IngestPrefix, first.Token);
        Assert.NotEqual(first.Token, second.Token);

        // Two is what moving deployments over one at a time needs. A third means
        // the operator has lost track of which one they are retiring, so it is a
        // conflict with what the project holds rather than a bad request.
        using var third = await client.PostAsync(
            $"/projects/{project}/ingest-tokens", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        // The list names the two by their identifiers, which is how the operator
        // tells the tokens of a rotation apart, and carries neither secret.
        var listed = await ReadAsync<ListedIngestToken[]>(await client.GetAsync(
            $"/projects/{project}/ingest-tokens", TestContext.Current.CancellationToken));
        Assert.Equal(2, listed.Length);
        Assert.All(listed, token => Assert.Equal(TokenIdentifier.Length, token.Identifier.Length));

        var readBack = await ReadAsync<ReadIngestToken>(await client.GetAsync(
            $"/ingest-tokens/{first.Id}/token", TestContext.Current.CancellationToken));
        Assert.Equal(first.Token, readBack.Token);

        using var revoked = await client.DeleteAsync(
            $"/ingest-tokens/{first.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        // And with one revoked there is room again, which is what makes
        // revoking-first cost nothing.
        using var room = await client.PostAsync(
            $"/projects/{project}/ingest-tokens", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, room.StatusCode);
    }

    [Fact]
    public async Task An_ingest_token_is_handed_over_as_a_delivery_that_can_be_pasted()
    {
        using var client = await SignedInAsync();
        var project = await ProjectAsync("checkout");

        var issued = await ReadAsync<IssuedIngestToken>(await client.PostAsync(
            $"/projects/{project}/ingest-tokens", null, TestContext.Current.CancellationToken));

        // What the product hands over is one finished delivery, not the bare
        // token: this installation's address, this token, and an entry in the
        // format the endpoint reads.
        Assert.Contains($"{client.BaseAddress}ingest", issued.DeliverySnippet);
        Assert.Contains($"Authorization: Bearer {issued.Token}", issued.DeliverySnippet);
        Assert.Contains("Content-Type: application/x-ndjson", issued.DeliverySnippet);

        // It names no logaffe package, because none of the three is published
        // and a snippet whose first line cannot be installed is worse than none.
        Assert.DoesNotContain("Logaffe.", issued.DeliverySnippet);

        // Reading a token back and being able to use it are one errand, so the
        // read-back carries the same snippet rather than the token alone.
        var readBack = await ReadAsync<ReadIngestToken>(await client.GetAsync(
            $"/ingest-tokens/{issued.Id}/token", TestContext.Current.CancellationToken));
        Assert.Equal(issued.DeliverySnippet, readBack.DeliverySnippet);

        // A list carries neither the token nor the snippet it sits inside.
        var body = await client.GetStringAsync(
            $"/projects/{project}/ingest-tokens", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(issued.Token, body);
    }

    [Fact]
    public async Task A_signed_out_session_admits_nothing_afterwards()
    {
        using var client = await SignedInAsync();

        using var signedOut = await client.PostAsync(
            "/sign-out", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        // Ending a session removes the row and there is no cache between here
        // and authentication, so the next request is refused by the same lookup
        // that would have admitted it.
        using var after = await client.GetAsync(
            "/agent-tokens", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    /// A client carrying the cookie a sign-in handed out.
    /// </summary>
    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password = TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    /// <summary>
    /// Puts the installation in the state a completed claim leaves it in, by
    /// claiming it — the flow <see cref="ClaimEndpointTests"/> covers, walked
    /// here for its result rather than for itself.
    /// </summary>
    private async Task ClaimAsync()
    {
        var enrolled = await AClaimedInstallation.ClaimAsync(_installation, _volume);

        _secondFactorSecret = enrolled.SecondFactorSecret;
    }

    private async Task<Guid> ProjectAsync(string name)
    {
        await using var context = ContextFor(_connectionString);
        var project = Project.Create(name, RetentionWindow.OfDays(7), DateTimeOffset.UtcNow);

        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return project.Id;
    }

    private static async Task<string> ListBodyAsync(HttpClient client) =>
        await client.GetStringAsync("/agent-tokens", TestContext.Current.CancellationToken);

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

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private sealed record Enrolment(
        string SecondFactorSecret, IReadOnlyList<string> BackupCodes, string Ticket);

    private sealed record IssuedIngestToken(
        Guid Id, string Token, string DeliverySnippet, DateTimeOffset IssuedAt);

    private sealed record ListedIngestToken(
        Guid Id, string Identifier, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

    private sealed record ReadIngestToken(string Token, string DeliverySnippet);

    private sealed record IssuedAgentToken(
        Guid Id, string Name, string Token, string ClientConfiguration, DateTimeOffset IssuedAt);

    private sealed record ListedAgentToken(
        Guid Id, string Name, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

    private sealed record ReadAgentToken(string Token, string ClientConfiguration);
}

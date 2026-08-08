using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logaffe.Api.Http;
using Logaffe.Api.Mcp;
using Logaffe.Domain.Entries;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The MCP surface, asked the way an agent asks it: a handshake, a tool list and
/// calls over the wire against an installation that is actually running.
/// </summary>
/// <remarks>
/// <para>
/// Calling the tool methods directly would prove nothing worth starting a
/// composition root for. What is asked here is what only the endpoint can
/// answer: that <c>/mcp</c> is where <c>docs/mcp.md</c> promised every agent
/// configuration it would be, that an agent token is what opens it and neither
/// of the other two credentials is, that the tool list is four names long, and
/// that the caps and the total are what an answer actually carries.
/// </para>
/// <para>
/// The rest is ADR 0012 read off the wire: entries arrive as named fields, the
/// properties as the object they were delivered as, and the five seconds as
/// values to narrow rather than as a sentence.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class AgentToolTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    private static readonly DateTimeOffset Ten = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;
    private string _connectionString = null!;
    private string _agentToken = null!;
    private long _nextId = 1;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await ClaimAsync();

        using var operatorClient = await SignedInAsync();
        _agentToken = await IssueAgentTokenAsync(operatorClient);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        Directory.Delete(_volume, recursive: true);
    }

    [Fact]
    public async Task The_endpoint_is_at_the_path_every_configuration_carries()
    {
        // Not a route that can be moved: it goes into the configuration of every
        // agent that ever connects, so it is a promise to all of them.
        Assert.Equal("/mcp", AgentClientConfiguration.McpPath);

        await using var agent = await ConnectAsync();

        Assert.Equal("logaffe", agent.ServerInfo.Name);

        // No paragraph addressed to the model: the tool descriptions are the
        // only prose this adapter sends.
        Assert.Null(agent.ServerInstructions);
    }

    [Fact]
    public async Task Only_an_agent_token_opens_it()
    {
        using var client = _installation.CreateClient();

        // Nothing at all.
        Assert.Equal(HttpStatusCode.Unauthorized, (await HandshakeAsync(client)).StatusCode);

        // An ingest token, which is the mistake that will happen: it is a write
        // credential for one project and it is refused here on its prefix,
        // before the database is asked anything (ADR 0031).
        using var operatorClient = await SignedInAsync();
        var project = await CreateAsync(operatorClient, "orders");
        var ingest = await IssueIngestTokenAsync(operatorClient, project.Id);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await HandshakeAsync(client, $"Bearer {ingest}")).StatusCode);

        // And the operator's own session, which is a person's credential and
        // not an agent's — there is no second, weaker door.
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await HandshakeAsync(operatorClient)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await HandshakeAsync(client, $"Bearer {_agentToken}")).StatusCode);
    }

    [Fact]
    public async Task An_agent_token_opens_nothing_on_the_operators_surface()
    {
        using var client = _installation.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_agentToken}");

        // A read credential is not a session. Projects and tokens are absent
        // from the agent's interface (ADR 0018), and this is the other half of
        // that: the routes that reach them do not take this token either.
        foreach (var path in new[] { "/projects", "/agent-tokens", "/sessions" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task There_are_four_tools_and_no_others()
    {
        await using var agent = await ConnectAsync();

        var tools = await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // Not a subset check. The claim is that there is nothing here that
        // writes, nothing that reaches a project or a token, and nothing that
        // follows the logs — which is a statement about the whole list.
        Assert.Equal(
            ["count_entries", "get_entry", "list_projects", "search_entries"],
            tools.Select(tool => tool.Name).Order());

        Assert.All(tools, tool => Assert.NotEmpty(tool.Description ?? string.Empty));
    }

    [Fact]
    public async Task There_are_no_resources_and_no_prompts()
    {
        await using var agent = await ConnectAsync();

        // A log store answers parameterized questions. Exposing projects as
        // readable resources would be a second way to ask the same thing, with
        // its own caching and its own surface.
        Assert.Null(agent.ServerCapabilities.Resources);
        Assert.Null(agent.ServerCapabilities.Prompts);
    }

    [Fact]
    public async Task Nothing_is_delivered_without_a_call()
    {
        using var client = _installation.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_agentToken}");

        using var response = await client.GetAsync(
            AgentClientConfiguration.McpPath, TestContext.Current.CancellationToken);

        // There is no stream for a client to hold open waiting to be told
        // something, and the endpoint says so rather than letting the
        // single-page application's fallback answer with the operator's page.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task A_project_is_named_by_what_list_projects_gives()
    {
        using var client = await SignedInAsync();
        var created = await CreateAsync(client, "orders");

        await using var agent = await ConnectAsync();

        var listed = await CallAsync<ProjectsBody>(agent, "list_projects", []);
        var project = Assert.Single(listed.Projects);

        Assert.Equal(created.Id, project.Id);
        Assert.Equal("orders", project.Name);
        Assert.Equal(7, project.RetentionDays);
    }

    [Fact]
    public async Task A_compact_search_carries_five_fields_and_the_identity_to_follow_up_with()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        var id = await StoreAsync(
            project.Id,
            Ten,
            Level.Error,
            "Orders.Api.CheckoutController",
            "api-7c4f",
            message: "Checkout 4711 failed",
            exception: "System.NullReferenceException: Object reference not set");

        await using var agent = await ConnectAsync();

        var raw = await CallAsync(agent, "search_entries", new() { ["projectId"] = project.Id });
        var answer = raw.Deserialize<SearchBody>(Web)!;

        Assert.Equal("compact", answer.Verbosity);

        var entry = Assert.Single(answer.Entries);
        Assert.Equal(id, entry.Id);
        Assert.Equal(Ten, entry.EventTime);
        Assert.Equal("Error", entry.Level);
        Assert.Equal("Orders.Api.CheckoutController", entry.LoggerName);
        Assert.Equal("api-7c4f", entry.Instance);
        Assert.Equal("Checkout 4711 failed", entry.Message);

        // Compact leaves the rest out rather than writing it as null: it exists
        // to keep a broad search from spending an agent's whole context, and
        // seven null fields on each of two hundred entries would spend a good
        // part of what it saved. The entry has an exception and the compact
        // shape still does not carry the field.
        Assert.DoesNotContain("exception", raw.GetRawText());
        Assert.DoesNotContain("receiptTime", raw.GetRawText());
    }

    [Fact]
    public async Task A_full_search_carries_the_properties_as_the_object_they_arrived_as()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(
            project.Id,
            Ten,
            Level.Error,
            trace: "0af7651916cd43dd8448eb211c80319c",
            message: "Checkout 4711 failed",
            exception: "System.NullReferenceException: Object reference not set",
            properties: """{"UserId": 42, "Ip": "203.0.113.7"}""");

        await using var agent = await ConnectAsync();

        var answer = await CallAsync<SearchBody>(
            agent,
            "search_entries",
            new() { ["projectId"] = project.Id, ["verbosity"] = "full" });

        Assert.Equal("full", answer.Verbosity);

        var entry = Assert.Single(answer.Entries);
        Assert.Equal(Ten, entry.ReceiptTime);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", entry.Trace);
        Assert.StartsWith("System.NullReferenceException", entry.Exception);
        Assert.False(entry.MessageTruncated);

        // Data and never prose (ADR 0012): the object the sender delivered,
        // handed back as one, with nothing read inside it and nothing rendered.
        Assert.Equal(JsonValueKind.Object, entry.Properties!.Value.ValueKind);
        Assert.Equal(42, entry.Properties.Value.GetProperty("UserId").GetInt32());
    }

    [Theory]

    // The two caps of docs/mcp.md, and they are this adapter's rather than the
    // page's: a tool pages the use case underneath it until it is full.
    [InlineData("compact", AgentCap.Compact)]
    [InlineData("full", AgentCap.Full)]
    public async Task A_search_fills_its_cap_and_says_what_it_left(string verbosity, int cap)
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        var stored = cap + 30;
        for (var minute = 0; minute < stored; minute++)
        {
            await StoreAsync(project.Id, Ten.AddMinutes(-minute), message: $"Line {minute}");
        }

        await using var agent = await ConnectAsync();

        var first = await CallAsync<SearchBody>(
            agent,
            "search_entries",
            new() { ["projectId"] = project.Id, ["verbosity"] = verbosity });

        Assert.Equal(cap, first.Entries.Count);

        // An agent that receives fifty entries and is not told there were nine
        // thousand will answer as though there were fifty.
        Assert.True(first.Capped);
        Assert.Equal(stored, first.Matched);
        Assert.NotNull(first.Cursor);
        Assert.Equal("Line 0", first.Entries[0].Message);

        var second = await CallAsync<SearchBody>(
            agent,
            "search_entries",
            new()
            {
                ["projectId"] = project.Id,
                ["verbosity"] = verbosity,
                ["cursor"] = first.Cursor!,
            });

        // Neither repeating the last entry of the first answer nor skipping the
        // one after it, and the total is the same total.
        Assert.Equal(stored - cap, second.Entries.Count);
        Assert.False(second.Capped);
        Assert.Null(second.Cursor);
        Assert.Equal(stored, second.Matched);
        Assert.Empty(first.Entries.Select(e => e.Id).Intersect(second.Entries.Select(e => e.Id)));
    }

    [Fact]
    public async Task An_answer_that_was_not_capped_is_its_own_total()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(project.Id, Ten, Level.Error, message: "Checkout failed");
        await StoreAsync(project.Id, Ten.AddMinutes(-5), message: "Checkout started");

        await using var agent = await ConnectAsync();

        var answer = await CallAsync<SearchBody>(
            agent,
            "search_entries",
            new() { ["projectId"] = project.Id, ["minimumLevel"] = "warning" });

        // The filters narrowed it to one, and the count that would say so is not
        // run: the answer already contains it.
        Assert.Equal("Checkout failed", Assert.Single(answer.Entries).Message);
        Assert.False(answer.Capped);
        Assert.Equal(1, answer.Matched);
        Assert.Null(answer.Cursor);
    }

    [Fact]
    public async Task A_count_answers_a_number_and_a_grouped_one_answers_names()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(project.Id, Ten, Level.Fatal);
        await StoreAsync(project.Id, Ten, Level.Information);
        await StoreAsync(project.Id, Ten, Level.Information);

        await using var agent = await ConnectAsync();

        var plain = await CallAsync<CountBody>(
            agent, "count_entries", new() { ["projectId"] = project.Id });

        Assert.Null(Assert.Single(plain.Groups).Value);
        Assert.Equal(3, plain.Groups[0].Entries);

        var byLevel = await CallAsync<CountBody>(
            agent,
            "count_entries",
            new() { ["projectId"] = project.Id, ["groupBy"] = "level" });

        Assert.Equal(["Fatal", "Information"], byLevel.Groups.Select(group => group.Value));
        Assert.Equal([1, 2], byLevel.Groups.Select(group => group.Entries));
    }

    [Fact]
    public async Task One_entry_is_fetched_in_full_by_the_identity_a_search_gave()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");
        var other = await CreateAsync(client, "billing");

        var id = await StoreAsync(
            project.Id, Ten, Level.Error, message: "Checkout failed",
            properties: """{"UserId": 42}""");

        await using var agent = await ConnectAsync();

        var entry = await CallAsync<EntryBody>(
            agent, "get_entry", new() { ["projectId"] = project.Id, ["entryId"] = id });

        Assert.Equal(id, entry.Id);
        Assert.Equal("Error", entry.Level);
        Assert.Equal(42, entry.Properties!.Value.GetProperty("UserId").GetInt32());

        // Asked inside a project, so an entry cannot be reached from one it does
        // not belong to by guessing a number.
        var refused = await agent.CallToolAsync(
            "get_entry",
            new Dictionary<string, object?> { ["projectId"] = other.Id, ["entryId"] = id },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(refused.IsError is true);
    }

    [Fact]
    public async Task Every_tool_reads_one_project_and_never_across_them()
    {
        using var client = await SignedInAsync();
        var orders = await CreateAsync(client, "orders");
        await CreateAsync(client, "billing");

        await StoreAsync(orders.Id, Ten, message: "Checkout failed");

        await using var agent = await ConnectAsync();

        // The three reads take a project and there is no argument that widens
        // them. What that leaves is a tool that cannot be asked a question
        // spanning two, which is the same rule the UI keeps.
        var tools = await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var tool in tools.Where(tool => tool.Name is not "list_projects"))
        {
            var required = tool.JsonSchema.GetProperty("required").EnumerateArray()
                .Select(value => value.GetString());

            Assert.Contains("projectId", required);
        }
    }

    [Theory]

    // Two characters is not a narrower search, it is a scan of the project
    // (ADR 0025), and the rule binds the surface rather than one caller.
    [InlineData("search_entries", "search", "ab")]
    [InlineData("count_entries", "exception", "ab")]

    // Values that could only ever match nothing, refused where they were
    // written rather than run.
    [InlineData("search_entries", "minimumLevel", "loud")]
    [InlineData("search_entries", "trace", "nothex")]
    [InlineData("search_entries", "cursor", "not-a-cursor")]
    public async Task A_filter_that_cannot_be_read_is_refused_rather_than_run(
        string tool, string argument, string value)
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await using var agent = await ConnectAsync();

        var refused = await agent.CallToolAsync(
            tool,
            new Dictionary<string, object?> { ["projectId"] = project.Id, [argument] = value },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(refused.IsError is true);

        // Named, so that a model correcting itself has something to correct.
        Assert.Contains(
            argument,
            string.Concat(refused.Content.OfType<TextContentBlock>().Select(block => block.Text)));
    }

    [Fact]
    public async Task A_project_that_does_not_exist_is_an_error_and_not_an_empty_answer()
    {
        await using var agent = await ConnectAsync();

        foreach (var tool in new[] { "search_entries", "count_entries" })
        {
            var refused = await agent.CallToolAsync(
                tool,
                new Dictionary<string, object?> { ["projectId"] = Guid.CreateVersion7() },
                cancellationToken: TestContext.Current.CancellationToken);

            // A caller told "no entries" would go looking for a delivery problem.
            Assert.True(refused.IsError is true);
        }
    }

    /// <summary>
    /// One connected agent, holding the token the operator issued it.
    /// </summary>
    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://localhost{AgentClientConfiguration.McpPath}"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {_agentToken}",
                },

                // Nothing is delivered without a call (`docs/mcp.md`), so there
                // is no stream for the client to hold open waiting for one.
                EnableStandaloneGetStream = false,
            },
            _installation.CreateClient(),
            loggerFactory: null,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<T> CallAsync<T>(
        McpClient agent, string tool, Dictionary<string, object?> arguments) =>
        (await CallAsync(agent, tool, arguments)).Deserialize<T>(Web)!;

    /// <summary>
    /// One tool call, as the tool wrote it.
    /// </summary>
    /// <remarks>
    /// Off the structured content rather than off the text block, because that
    /// is the named-field form the tools declare a schema for — and reading the
    /// text back as prose is the thing ADR 0012 exists to prevent. It is kept
    /// unparsed here so that a claim about a field being absent is a claim about
    /// what went over the wire rather than about what a record put back.
    /// </remarks>
    private static async Task<JsonElement> CallAsync(
        McpClient agent, string tool, Dictionary<string, object?> arguments)
    {
        var result = await agent.CallToolAsync(
            tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(
            result.IsError is true,
            string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text)));

        Assert.NotNull(result.StructuredContent);

        return result.StructuredContent.Value;
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<string> IssueAgentTokenAsync(HttpClient client) =>
        (await ReadAsync<IssuedTokenBody>(await client.PostAsJsonAsync(
            "/agent-tokens",
            new { name = "a terminal agent" },
            TestContext.Current.CancellationToken))).Token;

    private async Task<string> IssueIngestTokenAsync(HttpClient client, Guid projectId) =>
        (await ReadAsync<IssuedTokenBody>(await client.PostAsync(
            $"/projects/{projectId}/ingest-tokens",
            null,
            TestContext.Current.CancellationToken))).Token;

    /// <summary>
    /// The one request every MCP session starts with, sent by hand so that what
    /// the endpoint answers an unadmitted caller is a status code this test can
    /// read.
    /// </summary>
    private static async Task<HttpResponseMessage> HandshakeAsync(
        HttpClient client, string? authorization = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, AgentClientConfiguration.McpPath)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "a test", version = "0.0.0" },
                },
            }),
        };

        request.Headers.Accept.Clear();
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept", "text/event-stream");

        if (authorization is not null)
        {
            request.Headers.Add("Authorization", authorization);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<ProjectBody> CreateAsync(HttpClient client, string name) =>
        await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken));

    /// <inheritdoc cref="EntryEndpointTests"/>
    private async Task<long> StoreAsync(
        Guid projectId,
        DateTimeOffset at,
        Level level = Level.Information,
        string? loggerName = null,
        string? instance = null,
        string? trace = null,
        string? message = null,
        string? exception = null,
        string? properties = null)
    {
        var id = _nextId++;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            insert into log_entry (
                id, project_id, event_time, receipt_time, level, logger_name, instance,
                trace_id, message_template, rendered_message, exception, properties,
                message_truncated, exception_truncated)
            values (
                @id, @project_id, @at, @at, @level, @logger_name, @instance,
                @trace_id, @text, @text, @exception, @properties::jsonb, false, false)
            """,
            connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("at", at);
        command.Parameters.AddWithValue("level", (short)level);
        command.Parameters.AddWithValue("logger_name", (object?)loggerName ?? DBNull.Value);
        command.Parameters.AddWithValue("instance", (object?)instance ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "trace_id", trace is null ? DBNull.Value : Convert.FromHexString(trace));
        command.Parameters.AddWithValue("text", message ?? "Handled /orders");
        command.Parameters.AddWithValue("exception", (object?)exception ?? DBNull.Value);
        command.Parameters.AddWithValue("properties", (object?)properties ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return id;
    }

    /// <inheritdoc cref="EntryEndpointTests"/>
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

    /// <inheritdoc cref="EntryEndpointTests"/>
    private async Task ClaimAsync()
    {
        using var client = _installation.CreateClient();

        var enrolment = await ReadAsync<Enrolment>(await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken));

        _secondFactorSecret = enrolment.SecondFactorSecret;

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = Authenticator.CodeFor(enrolment.SecondFactorSecret),
                backupCode = enrolment.BackupCodes[0],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
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

    private sealed record Enrolment(
        string SecondFactorSecret, IReadOnlyList<string> BackupCodes, string Ticket);

    private sealed record ProjectBody(
        Guid Id, string Name, int RetentionDays, DateTimeOffset CreatedAt);

    private sealed record IssuedTokenBody(Guid Id, string Token);

    private sealed record ProjectsBody(IReadOnlyList<AgentProjectBody> Projects);

    private sealed record AgentProjectBody(
        Guid Id, string Name, int RetentionDays, DateTimeOffset CreatedAt);

    private sealed record SearchBody(
        string Verbosity,
        IReadOnlyList<EntryBody> Entries,
        long? Matched,
        bool Capped,
        string? Cursor,
        IReadOnlyList<string>? Narrow);

    /// <summary>
    /// One entry as a tool answers with it. Everything below the message is
    /// nullable because the compact shape leaves it out.
    /// </summary>
    private sealed record EntryBody(
        long Id,
        DateTimeOffset EventTime,
        string Level,
        string? LoggerName,
        string? Instance,
        string Message,
        DateTimeOffset? ReceiptTime,
        string? Trace,
        string? Span,
        string? MessageTemplate,
        string? Exception,
        JsonElement? Properties,
        bool? MessageTruncated,
        bool? ExceptionTruncated);

    private sealed record CountBody(
        IReadOnlyList<CountedGroupBody> Groups, IReadOnlyList<string>? Narrow);

    private sealed record CountedGroupBody(string? Value, long Entries);
}

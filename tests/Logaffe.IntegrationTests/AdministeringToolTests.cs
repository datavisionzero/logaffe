using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logaffe.Api.Http;
using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The administering half of <c>/mcp</c>, asked the way an agent asks it: a
/// handshake with an administering token, the tool list it earns, and calls over
/// the wire against an installation that is actually running.
/// </summary>
/// <remarks>
/// <para>
/// What is asked here is what only the endpoint can answer, and most of it is
/// ADR 0046 read off the wire. That the two kinds are handed two lists and that
/// neither is a subset of the other. That the four which destroy data are
/// <i>absent</i> from a token without the flag rather than present and refusing,
/// which is the whole reason the retention window is two tools per direction.
/// That no tool anywhere on this surface produces the value of a token that
/// already exists. And that an agent token, an operator credential and a session
/// are unreachable on any token at all.
/// </para>
/// <para>
/// The blast radius 0046 states plainly is asserted rather than described: an
/// administering agent creates a project, mints a live ingest credential for it,
/// and something delivers with it. That is the trade the decision made, and a
/// test that shows it is a test that will fail if the door is ever narrowed by
/// accident.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class AdministeringToolTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";

    /// <summary>The size the settings tree is held to, and twenty machines with it.</summary>
    private const int HundredProjects = 100;

    /// <inheritdoc cref="HundredProjects"/>
    private const int TwentyHosts = 20;

    private static readonly Guid NoSuchThing = new("0195f0d4-0000-7000-8000-000000000000");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The seventeen every administering token is handed.</summary>
    private static readonly string[] Administering =
    [
        "count_entries_outside_window",
        "create_group",
        "create_host",
        "create_project",
        "delete_group",
        "extend_project_retention",
        "extend_sample_retention",
        "get_settings",
        "issue_host_token",
        "issue_ingest_token",
        "move_project_to_group",
        "put_project_on_host",
        "rename_group",
        "rename_host",
        "rename_project",
        "revoke_host_token",
        "revoke_ingest_token",
    ];

    /// <summary>The four that remove data which does not come back.</summary>
    private static readonly string[] Destroying =
    [
        "delete_host",
        "delete_project",
        "shorten_project_retention",
        "shorten_sample_retention",
    ];

    private readonly string _volume = InstallationVolume.Create(nameof(AdministeringToolTests));

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;
    private string _administering = null!;
    private string _destroying = null!;

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

        using var operatorClient = await SignedInAsync();

        _administering = await IssueAgentTokenAsync(operatorClient, "administering", false);
        _destroying = await IssueAgentTokenAsync(operatorClient, "administering", true);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Fact]
    public async Task There_are_seventeen_tools_on_a_token_that_may_not_destroy()
    {
        await using var agent = await ConnectAsync(_administering);

        var tools = await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // Not a subset check. The claim is that there is nothing here that reads
        // an entry, nothing that reaches an agent token, an operator credential
        // or a session, and nothing that removes data — which is a statement
        // about the whole list.
        Assert.Equal(Administering, tools.Select(tool => tool.Name).Order());

        Assert.All(tools, tool => Assert.NotEmpty(tool.Description ?? string.Empty));
    }

    [Fact]
    public async Task The_four_that_destroy_arrive_only_on_a_token_issued_saying_so()
    {
        await using var agent = await ConnectAsync(_destroying);

        var tools = await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            Administering.Concat(Destroying).Order(),
            tools.Select(tool => tool.Name).Order());

        // And they say what they are, so a client that surfaces destructive
        // calls differently can.
        Assert.All(
            tools.Where(tool => Destroying.Contains(tool.Name)),
            tool => Assert.True(tool.ProtocolTool.Annotations?.DestructiveHint));
    }

    [Fact]
    public async Task A_token_without_the_flag_is_not_handed_a_shortening_tool_that_refuses()
    {
        await using var agent = await ConnectAsync(_administering);

        var tools = await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // Absent from the list is the whole point of the window being two tools
        // per direction rather than one setter: what this token can do is
        // legible in what it was handed.
        Assert.DoesNotContain("shorten_project_retention", tools.Select(tool => tool.Name));

        // And naming one anyway is refused, so the flag is not walked around by
        // a client that read the other token's list.
        foreach (var tool in Destroying)
        {
            var refused = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await agent.CallToolAsync(
                    tool,
                    new Dictionary<string, object?> { ["projectId"] = NoSuchThing },
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("authorization", refused.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_two_kinds_do_not_meet()
    {
        // No entry reaches this token, which is the sentence the whole surface
        // rests on: the attack needs one session that holds untrusted text and
        // can act, and this one cannot hold the text (ADR 0046).
        await using var agent = await ConnectAsync(_destroying);

        var tools = (await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToList();

        foreach (var reading in
            new[] { "list_projects", "search_entries", "count_entries", "get_entry", "get_host_samples" })
        {
            Assert.DoesNotContain(reading, tools);

            var refused = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await agent.CallToolAsync(
                    reading,
                    new Dictionary<string, object?>(),
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("authorization", refused.Message, StringComparison.OrdinalIgnoreCase);
        }

        // And the other way round, which is the half that keeps a log line from
        // ever reaching a session that can act.
        using var operatorClient = await SignedInAsync();
        var readingToken = await IssueAgentTokenAsync(operatorClient, "reading", false);

        await using var reader = await ConnectAsync(readingToken);

        var offered = (await reader.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToList();

        Assert.Empty(offered.Intersect(Administering.Concat(Destroying)));

        var second = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await reader.CallToolAsync(
                "get_settings",
                new Dictionary<string, object?>(),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("authorization", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Three_things_are_absent_from_the_interface_on_every_token()
    {
        await using var agent = await ConnectAsync(_destroying);

        var tools = (await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToList();

        // An agent that could issue an agent token would grant itself the kind
        // and the flag the operator withheld, and both would be decoration. The
        // operator's credentials and their sessions are the other two, and none
        // of the three is a flag anywhere.
        Assert.DoesNotContain(tools, name =>
            name.Contains("agent_token", StringComparison.Ordinal)
            || name.Contains("password", StringComparison.Ordinal)
            || name.Contains("session", StringComparison.Ordinal)
            || name.Contains("backup", StringComparison.Ordinal)
            || name.Contains("second_factor", StringComparison.Ordinal));

        // And the routes that reach them do not take this token either, which is
        // the other half of the same claim: there is no second, weaker door.
        using var client = _installation.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_destroying}");

        foreach (var path in new[] { "/agent-tokens", "/sessions", "/projects", "/hosts" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_settings_are_the_whole_surface_in_one_answer_and_no_token_is_in_it()
    {
        await using var agent = await ConnectAsync(_administering);

        var group = await CallAsync<GroupBody>(
            agent, "create_group", new() { ["name"] = "the shop" });

        var host = await CallAsync<HostBody>(
            agent, "create_host", new() { ["name"] = "web-01" });

        var project = await CallAsync<ProjectBody>(
            agent,
            "create_project",
            new()
            {
                ["name"] = "orders",
                ["retentionDays"] = 14,
                ["groupId"] = group.Id,
            });

        await CallAsync<ProjectBody>(
            agent,
            "put_project_on_host",
            new() { ["projectId"] = project.Id, ["hostId"] = host.Id });

        var issued = await CallAsync<IssuedBody>(
            agent, "issue_ingest_token", new() { ["projectId"] = project.Id });

        var raw = await CallAsync(agent, "get_settings", []);
        var settings = raw.Deserialize<SettingsBody>(Web)!;

        var listed = Assert.Single(settings.Projects);
        Assert.Equal(project.Id, listed.Id);
        Assert.Equal("orders", listed.Name);
        Assert.Equal(group.Id, listed.GroupId);
        Assert.Equal(host.Id, listed.HostId);
        Assert.Equal(14, listed.RetentionDays);

        // A project that has never received leaves the field out rather than
        // writing null, and the schema says so — the rule the whole adapter
        // follows.
        Assert.Null(listed.LastReceivedAt);
        Assert.False(
            raw.GetProperty("projects")[0].TryGetProperty("lastReceivedAt", out _));

        Assert.Equal("the shop", Assert.Single(settings.Groups).Name);
        Assert.Equal("web-01", Assert.Single(settings.Hosts).Name);
        Assert.True(settings.SampleRetentionDays > 0);

        // The token it holds is counted and dated, and its value is nowhere in
        // the answer. This is the claim in full: not that the field is empty,
        // but that the string handed over at issue does not appear anywhere in
        // what this tool wrote.
        var token = Assert.Single(listed.IngestTokens);
        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Null(token.LastUsedAt);
        Assert.DoesNotContain(issued.Token, raw.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("logaffe_", raw.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_settings_tree_is_answered_whole_at_the_size_groups_exist_for()
    {
        // A hundred projects and twenty hosts, every one of them mid-rotation:
        // the size an installation reaches before it is organised into groups,
        // and the size at which this tool used to be a read per project and per
        // host on the one connection the request holds.
        await SeedAsync(HundredProjects, TwentyHosts);

        await using var agent = await ConnectAsync(_administering);

        var started = Stopwatch.GetTimestamp();
        var raw = await CallAsync(agent, "get_settings", []);
        var took = Stopwatch.GetElapsedTime(started);

        var settings = raw.Deserialize<SettingsBody>(Web)!;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"get_settings over {HundredProjects} projects and {TwentyHosts} hosts, " +
            $"two tokens each: {took.TotalMilliseconds:F0} ms");

        // Whole, and not merely quick: every project and every host is in it
        // with both of the tokens it holds, which is what says the one read
        // per kind took nothing away from the answer.
        Assert.Equal(HundredProjects, settings.Projects.Count);
        Assert.Equal(TwentyHosts, settings.Hosts.Count);
        Assert.All(
            settings.Projects,
            project => Assert.Equal(IngestToken.MaximumPerProject, project.IngestTokens.Count));
        Assert.All(
            settings.Hosts,
            host => Assert.Equal(HostToken.MaximumPerHost, host.HostTokens.Count));
        Assert.DoesNotContain("logaffe_", raw.GetRawText(), StringComparison.Ordinal);

        // A ceiling rather than a benchmark, with room for a machine running CI
        // and a container: what it holds is that the first call an agent makes
        // finishes inside the budget a read gets (ADR 0026) at this size, rather
        // than outlasting the client's timeout with nothing to show.
        Assert.True(
            took < TimeSpan.FromSeconds(5),
            $"The settings tree took {took.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task A_token_is_issued_once_and_no_tool_reads_one_back()
    {
        await using var agent = await ConnectAsync(_administering);

        var tools = (await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToList();

        // Not directly, and not through a snippet that carries the token inside
        // it. Recovering one is an errand at a browser (ADR 0022).
        Assert.DoesNotContain(tools, name => name.StartsWith("read_", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, name => name.Contains("snippet", StringComparison.Ordinal));

        var project = await CallAsync<ProjectBody>(
            agent, "create_project", new() { ["name"] = "orders", ["retentionDays"] = 7 });

        var first = await CallAsync<IssuedBody>(
            agent, "issue_ingest_token", new() { ["projectId"] = project.Id });

        Assert.StartsWith("logaffe_ingest_", first.Token, StringComparison.Ordinal);
        Assert.Contains(first.Token, first.Snippet, StringComparison.Ordinal);

        // Issuing where one already exists is rotation and is allowed outright:
        // the narrow rule does not survive revoking not being destructive.
        var second = await CallAsync<IssuedBody>(
            agent, "issue_ingest_token", new() { ["projectId"] = project.Id });

        Assert.NotEqual(first.Token, second.Token);

        // Two is as many as there is a reason for, and the refusal names the
        // tool that makes room.
        var refused = await CallForErrorAsync(
            agent, "issue_ingest_token", new() { ["projectId"] = project.Id });

        Assert.Contains("revoke_ingest_token", refused, StringComparison.Ordinal);

        var revoked = await CallAsync<RevokedBody>(
            agent, "revoke_ingest_token", new() { ["tokenId"] = first.Id });

        Assert.Equal(first.Id, revoked.Id);
    }

    [Fact]
    public async Task An_administering_agent_can_put_a_live_credential_into_a_project()
    {
        // The blast radius ADR 0046 states plainly, asserted rather than
        // described. What bounds it is not a confirmation step: it is that this
        // token reads no entry, so the sentence asking for the credential never
        // enters its context, and that an ingest token is write-only.
        await using var agent = await ConnectAsync(_administering);

        var project = await CallAsync<ProjectBody>(
            agent, "create_project", new() { ["name"] = "orders", ["retentionDays"] = 7 });

        var issued = await CallAsync<IssuedBody>(
            agent, "issue_ingest_token", new() { ["projectId"] = project.Id });

        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, DeliverySnippet.IngestPath)
        {
            Content = new StringContent(
                """{"@t":"2026-08-20T10:00:00Z","@mt":"Handled /orders"}""",
                System.Text.Encoding.UTF8,
                DeliverySnippet.ContentType),
        };

        request.Headers.Add("Authorization", $"Bearer {issued.Token}");

        using var delivered = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
    }

    [Fact]
    public async Task A_window_call_in_the_wrong_direction_names_the_tool_that_does_it()
    {
        await using var agent = await ConnectAsync(_destroying);

        var project = await CallAsync<ProjectBody>(
            agent, "create_project", new() { ["name"] = "orders", ["retentionDays"] = 7 });

        var wrongWay = await CallForErrorAsync(
            agent,
            "extend_project_retention",
            new() { ["projectId"] = project.Id, ["retentionDays"] = 3 });

        Assert.Contains("shorten_project_retention", wrongWay, StringComparison.Ordinal);

        var theOtherWay = await CallForErrorAsync(
            agent,
            "shorten_project_retention",
            new() { ["projectId"] = project.Id, ["retentionDays"] = 30 });

        Assert.Contains("extend_project_retention", theOtherWay, StringComparison.Ordinal);

        // Both directions work when they are the direction they are for, and
        // each answers with the project as it now stands.
        var extended = await CallAsync<ProjectBody>(
            agent,
            "extend_project_retention",
            new() { ["projectId"] = project.Id, ["retentionDays"] = 30 });

        Assert.Equal(30, extended.RetentionDays);

        var shortened = await CallAsync<ProjectBody>(
            agent,
            "shorten_project_retention",
            new() { ["projectId"] = project.Id, ["retentionDays"] = 2 });

        Assert.Equal(2, shortened.RetentionDays);
    }

    [Fact]
    public async Task The_refusal_reads_the_same_whether_or_not_the_caller_could_have_shortened()
    {
        // A refusal that said "and you could not have called it anyway" would
        // turn every wrong-direction call into a probe for what the operator
        // withheld. What a token may do is what its list says.
        await using var weaker = await ConnectAsync(_administering);
        await using var stronger = await ConnectAsync(_destroying);

        var project = await CallAsync<ProjectBody>(
            stronger, "create_project", new() { ["name"] = "orders", ["retentionDays"] = 7 });

        var arguments = new Dictionary<string, object?>
        {
            ["projectId"] = project.Id,
            ["retentionDays"] = 3,
        };

        Assert.Equal(
            await CallForErrorAsync(stronger, "extend_project_retention", arguments),
            await CallForErrorAsync(weaker, "extend_project_retention", arguments));
    }

    [Fact]
    public async Task A_count_of_what_a_window_would_drop_is_on_a_token_that_cannot_shorten()
    {
        // The useful half of the answer for an agent that may not make the
        // change: it can still tell the operator what it would cost. A number
        // and no entry, which is why a count is on this surface at all.
        await using var agent = await ConnectAsync(_administering);

        var project = await CallAsync<ProjectBody>(
            agent, "create_project", new() { ["name"] = "orders", ["retentionDays"] = 30 });

        var counted = await CallAsync<OutsideBody>(
            agent,
            "count_entries_outside_window",
            new() { ["projectId"] = project.Id, ["retentionDays"] = 1 });

        Assert.Equal(1, counted.RetentionDays);
        Assert.Equal(0, counted.Entries);
    }

    [Fact]
    public async Task Deleting_a_group_is_not_destructive_and_leaves_its_projects()
    {
        // The clearest illustration of what the flag means: a group holds
        // nothing, so removing one takes nothing with it — and it is therefore
        // on a token that may not destroy (ADR 0039).
        await using var agent = await ConnectAsync(_administering);

        var group = await CallAsync<GroupBody>(
            agent, "create_group", new() { ["name"] = "the shop" });

        var project = await CallAsync<ProjectBody>(
            agent,
            "create_project",
            new() { ["name"] = "orders", ["retentionDays"] = 7, ["groupId"] = group.Id });

        var removed = await CallAsync<RemovedBody>(
            agent, "delete_group", new() { ["groupId"] = group.Id });

        Assert.Equal(group.Id, removed.Id);
        Assert.Equal("the shop", removed.Name);

        var settings = (await CallAsync(agent, "get_settings", [])).Deserialize<SettingsBody>(Web)!;

        Assert.Empty(settings.Groups);

        var left = Assert.Single(settings.Projects);
        Assert.Equal(project.Id, left.Id);
        Assert.Null(left.GroupId);
    }

    [Fact]
    public async Task Deleting_a_project_says_what_went()
    {
        await using var agent = await ConnectAsync(_destroying);

        var project = await CallAsync<ProjectBody>(
            agent, "create_project", new() { ["name"] = "orders", ["retentionDays"] = 7 });

        var removed = await CallAsync<RemovedBody>(
            agent, "delete_project", new() { ["projectId"] = project.Id });

        // The name is here because after the row is gone nothing can say it, and
        // an agent reporting back that it deleted `orders` is saying something
        // the operator can check.
        Assert.Equal(project.Id, removed.Id);
        Assert.Equal("orders", removed.Name);

        var again = await CallForErrorAsync(
            agent, "delete_project", new() { ["projectId"] = project.Id });

        Assert.Contains("get_settings", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_names_the_argument_and_where_to_look()
    {
        await using var agent = await ConnectAsync(_administering);

        // A model told only that a call failed tries the same call again.
        Assert.Contains(
            "projectId",
            await CallForErrorAsync(
                agent, "rename_project", new() { ["projectId"] = NoSuchThing, ["name"] = "x" }),
            StringComparison.Ordinal);

        Assert.Contains(
            "groupId",
            await CallForErrorAsync(
                agent, "rename_group", new() { ["groupId"] = NoSuchThing, ["name"] = "x" }),
            StringComparison.Ordinal);

        Assert.Contains(
            "hostId",
            await CallForErrorAsync(
                agent, "rename_host", new() { ["hostId"] = NoSuchThing, ["name"] = "x" }),
            StringComparison.Ordinal);

        await CallAsync<GroupBody>(agent, "create_group", new() { ["name"] = "the shop" });

        Assert.Contains(
            "name",
            await CallForErrorAsync(agent, "create_group", new() { ["name"] = "the shop" }),
            StringComparison.Ordinal);

        Assert.Contains(
            "retentionDays",
            await CallForErrorAsync(
                agent,
                "create_project",
                new() { ["name"] = "orders", ["retentionDays"] = 900 }),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_schema_a_tool_carries_is_a_schema_object_all_the_way_down()
    {
        await using var agent = await ConnectAsync(_destroying);

        var tools = await agent.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // A field typed as any JSON at all exports as the boolean schema `true`,
        // which is legal and is refused by clients that hold a tool schema to
        // being an object — and what they refuse is the list, so one such field
        // costs every tool rather than the one that carries it.
        foreach (var tool in tools)
        {
            Assert.Equal(JsonValueKind.Object, tool.ProtocolTool.InputSchema.ValueKind);

            if (tool.ProtocolTool.OutputSchema is { } output)
            {
                Assert.Equal(JsonValueKind.Object, output.ValueKind);
            }
        }
    }

    private async Task<McpClient> ConnectAsync(string token)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://localhost{AgentClientConfiguration.McpPath}"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {token}",
                },
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

    /// <inheritdoc cref="AgentToolTests"/>
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

    /// <summary>
    /// A call that is meant to be refused, and the sentence it was refused with.
    /// </summary>
    private static async Task<string> CallForErrorAsync(
        McpClient agent, string tool, Dictionary<string, object?> arguments)
    {
        var result = await agent.CallToolAsync(
            tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is true, $"{tool} was expected to refuse and did not.");

        return string.Concat(
            result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    /// <summary>
    /// Projects and hosts written straight into the store, because what is being
    /// asked about is the read and not the acts that put them there.
    /// </summary>
    private async Task SeedAsync(int projects, int hosts)
    {
        using var scope = _installation.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<LogaffeDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<ISecretCipher>();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < projects; i++)
        {
            var project = Project.Create($"project-{i:D3}", RetentionWindow.OfDays(14), now);
            context.Projects.Add(project);

            for (var held = 0; held < IngestToken.MaximumPerProject; held++)
            {
                var minted = TokenText.Mint(TokenKind.Ingest);
                context.IngestTokens.Add(IngestToken.Issue(
                    project.Id,
                    minted.Identifier,
                    cipher.Encrypt(minted.Secret),
                    now.AddMinutes(held)));
            }
        }

        for (var i = 0; i < hosts; i++)
        {
            var host = Host.Create($"host-{i:D2}", now);
            context.Hosts.Add(host);

            for (var held = 0; held < HostToken.MaximumPerHost; held++)
            {
                var minted = TokenText.Mint(TokenKind.Host);
                context.HostTokens.Add(HostToken.Issue(
                    host.Id,
                    minted.Identifier,
                    cipher.Encrypt(minted.Secret),
                    now.AddMinutes(held)));
            }
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> IssueAgentTokenAsync(
        HttpClient client, string kind, bool mayDestroy)
    {
        using var response = await client.PostAsJsonAsync(
            "/agent-tokens",
            new { name = $"a {kind} agent {(mayDestroy ? "that may destroy" : string.Empty)}", kind, mayDestroy },
            TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode} from /agent-tokens");

        return (await response.Content.ReadFromJsonAsync<IssuedBody>(
            TestContext.Current.CancellationToken))!.Token;
    }

    /// <inheritdoc cref="AgentToolTests"/>
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

    private sealed record ProjectBody(
        Guid Id,
        string Name,
        Guid? GroupId,
        Guid? HostId,
        int RetentionDays,
        DateTimeOffset CreatedAt);

    private sealed record GroupBody(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record HostBody(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record RemovedBody(Guid Id, string Name);

    private sealed record RevokedBody(Guid Id);

    private sealed record IssuedBody(
        Guid Id, string Token, string Snippet, DateTimeOffset IssuedAt);

    private sealed record OutsideBody(int RetentionDays, long Entries);

    private sealed record TokenBody(Guid Id, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

    private sealed record SettingsProjectBody(
        Guid Id,
        string Name,
        Guid? GroupId,
        Guid? HostId,
        int RetentionDays,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastReceivedAt,
        IReadOnlyList<TokenBody> IngestTokens);

    private sealed record SettingsHostBody(
        Guid Id,
        string Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastReportedAt,
        int Projects,
        IReadOnlyList<TokenBody> HostTokens);

    private sealed record SettingsBody(
        IReadOnlyList<GroupBody> Groups,
        IReadOnlyList<SettingsProjectBody> Projects,
        IReadOnlyList<SettingsHostBody> Hosts,
        int SampleRetentionDays);
}

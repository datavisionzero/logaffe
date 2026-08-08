using System.Net;
using Logaffe.Application.Operations;
using Logaffe.Client;
using Logaffe.Extensions.Logging;
using Logaffe.Serilog;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The client package against a running installation.
/// </summary>
/// <remarks>
/// <para>
/// <c>Logaffe.Client</c> is tested against a substituted message handler in the
/// unit tests, which is the right place to ask what it does when a delivery
/// fails, when the queue fills and when the application shuts down — none of
/// that needs a server. What a substituted handler cannot say is whether the two
/// ends agree: a handler accepts whatever the client sends, so a client writing
/// the wrong content type, the wrong header or a body the endpoint cannot read
/// would pass every one of those tests.
/// </para>
/// <para>
/// So this asks the one question the split leaves over, and asks it of both ends
/// at once: an entry handed to the package becomes a row in Postgres.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ClientDeliveryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Happened = "2026-08-08T09:15:00.417Z";

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    private string _connectionString = null!;
    private WebApplicationFactory<Program> _installation = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        Directory.Delete(_volume, recursive: true);
    }

    [Fact]
    public async Task An_entry_handed_to_the_package_becomes_a_row()
    {
        var (project, token) = await AdmittedAsync();

        await using (var delivery = Delivery(token.Text))
        {
            delivery.Send(
                $$"""
                  {"@t":"{{Happened}}","@l":"Warning","@mt":"User {UserId} failed login from {Ip}","UserId":42,"Ip":"203.0.113.7","instance":"api-7c4f","SourceContext":"Orders.Api"}
                  """);
        }

        var only = Assert.Single(await StoredAsync(project));

        // Rendered by the server out of properties the package carried across
        // as ordinary parts of the line (ADR 0005).
        Assert.Equal("User 42 failed login from 203.0.113.7", only.Rendered);
        Assert.Equal("User {UserId} failed login from {Ip}", only.Template);
        Assert.Equal((short)Level.Warning, only.Level);

        // The two properties the installation promotes to fields of their own,
        // which is what makes a replica separable and framework noise
        // filterable.
        Assert.Equal("api-7c4f", only.Instance);
        Assert.Equal("Orders.Api", only.LoggerName);
    }

    [Fact]
    public async Task A_batch_the_package_gzipped_is_read_as_one()
    {
        var (project, token) = await AdmittedAsync();

        // Past the size at which the package compresses, so that what the
        // endpoint decompresses is what this wrote.
        await using (var delivery = Delivery(token.Text))
        {
            for (var n = 0; n < 400; n++)
            {
                delivery.Send(
                    $$"""
                      {"@t":"{{Happened}}","@mt":"a message long enough to be worth compressing, number {{n}}"}
                      """);
            }
        }

        var stored = await StoredAsync(project);

        Assert.Equal(400, stored.Count);
        Assert.Equal(
            "a message long enough to be worth compressing, number 0", stored[0].Rendered);
    }

    [Fact]
    public async Task A_token_the_installation_refuses_costs_the_entries_and_says_so()
    {
        var (project, _) = await AdmittedAsync();
        var refusals = new List<string>();

        await using (var delivery = Delivery(
            "logaffe_ingest_019fe17900007000800000000000dead_notarealsecret",
            (what, _) => refusals.Add(what)))
        {
            delivery.Send($$"""{"@t":"{{Happened}}","@mt":"nobody will read this"}""");
        }

        Assert.Empty(await StoredAsync(project));
        Assert.Contains(refusals, what => what.Contains("token was refused"));
    }

    /// <summary>
    /// The same question of the package above it: what
    /// <c>CompactJsonFormatter</c> writes is what the endpoint stores.
    /// </summary>
    /// <remarks>
    /// Nothing but a running installation can answer it. logaffe refuses an
    /// entry carrying <c>@m</c>, so the rendered formatter would produce an
    /// integration in which every line is counted invalid and nothing is stored
    /// — and a substituted handler, which accepts whatever it is sent, would
    /// have agreed with either one.
    /// </remarks>
    [Fact]
    public async Task An_event_logged_through_the_serilog_sink_becomes_a_row()
    {
        var (project, token) = await AdmittedAsync();

        using (var logger = new LoggerConfiguration()
                   .WriteTo.Sink(new LogaffeSink(
                       DeliveryOptions(token.Text), "api-7c4f", _installation.CreateClient()))
                   .CreateLogger())
        {
            logger
                .ForContext("SourceContext", "Orders.Api")
                .Warning("User {UserId} failed login from {Ip}", 42, "203.0.113.7");
        }

        var only = Assert.Single(await StoredAsync(project));

        // Rendered by the server from the template and the values the sink
        // carried across, which is the whole reason it is not the rendered
        // formatter (ADR 0005).
        Assert.Equal("User {UserId} failed login from {Ip}", only.Template);
        Assert.Equal("User 42 failed login from 203.0.113.7", only.Rendered);

        // Serilog's spelling of the level, taken as it is.
        Assert.Equal((short)Level.Warning, only.Level);

        Assert.Equal("api-7c4f", only.Instance);
        Assert.Equal("Orders.Api", only.LoggerName);
    }

    /// <summary>
    /// And of the package beside it, which has to arrive at the same place: the
    /// two are required to behave identically, and an entry that renders
    /// differently is a difference no unit test would call one.
    /// </summary>
    [Fact]
    public async Task An_entry_logged_through_the_provider_becomes_the_same_row()
    {
        var (project, token) = await AdmittedAsync();

        // Disposed by hand, because a factory given a ready-made provider does
        // not dispose it -- which is the same rule that decides how
        // `AddLogaffe` registers one.
        using (var provider = new LogaffeLoggerProvider(
                   new LogaffeLoggerOptions
                   {
                       Installation = new Uri("http://localhost"),
                       IngestToken = token.Text,
                       Instance = "api-7c4f",
                       BatchInterval = TimeSpan.FromMilliseconds(50),
                       FlushTimeout = TimeSpan.FromSeconds(30),
                   },
                   _installation.CreateClient()))
        {
            using var factory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddProvider(provider));

            factory
                .CreateLogger("Orders.Api")
                .LogWarning("User {UserId} failed login from {Ip}", 42, "203.0.113.7");
        }

        var only = Assert.Single(await StoredAsync(project));

        Assert.Equal("User {UserId} failed login from {Ip}", only.Template);
        Assert.Equal("User 42 failed login from 203.0.113.7", only.Rendered);

        // The other spelling of the same level, mapped by the installation
        // without loss.
        Assert.Equal((short)Level.Warning, only.Level);

        Assert.Equal("api-7c4f", only.Instance);

        // The category, promoted to the logger name with nothing asked of the
        // application.
        Assert.Equal("Orders.Api", only.LoggerName);
    }

    private EntryDelivery Delivery(string token, Action<string, Exception?>? onFailure = null) =>
        new(DeliveryOptions(token, onFailure), _installation.CreateClient());

    private static EntryDeliveryOptions DeliveryOptions(
        string token, Action<string, Exception?>? onFailure = null) =>
        new()
        {
            // The host is the test server's, and the path is the package's
            // own -- which is the point: a client that reached for the
            // wrong one would find nothing here either.
            Installation = new Uri("http://localhost"),
            IngestToken = token,
            BatchInterval = TimeSpan.FromMilliseconds(50),
            FlushTimeout = TimeSpan.FromSeconds(30),
            OnFailure = onFailure,
        };

    private async Task<(Guid Project, TokenText Token)> AdmittedAsync()
    {
        var project = Project.Create("orders-api", RetentionWindow.OfDays(7), DateTimeOffset.UtcNow);

        using var scope = _installation.Services.CreateScope();
        var issue = scope.ServiceProvider.GetRequiredService<IssueIngestToken>();

        await using (var context = ContextFor())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var issued = await issue.ExecuteAsync(project.Id, TestContext.Current.CancellationToken);

        return (project.Id, issued.Token!.Token);
    }

    private async Task<List<StoredLine>> StoredAsync(Guid projectId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select level, logger_name, instance, message_template, rendered_message
            from log_entry
            where project_id = @project_id
            order by id
            """,
            connection);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);

        var entries = new List<StoredLine>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            entries.Add(new StoredLine(
                reader.GetInt16(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return entries;
    }

    private LogaffeDbContext ContextFor() =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(_connectionString).Options);

    private sealed record StoredLine(
        short Level, string? LoggerName, string? Instance, string Template, string Rendered);
}

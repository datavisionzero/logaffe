using System.Diagnostics;
using System.Text.Json.Nodes;
using Logaffe.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Logaffe.UnitTests.Client;

/// <summary>
/// What arrives at the endpoint when an application logs through
/// <c>ILogger</c>.
/// </summary>
/// <remarks>
/// The template is the whole point: <c>ILogger</c> is handed a template and its
/// named state, and what goes across has to be both — an already-formatted
/// string would arrive as a template with no holes, render to itself, and cost
/// every filter that would have worked on it. The other half of these is the
/// <c>@m</c> that is never written, which is what a rendered message would have
/// become.
/// </remarks>
public sealed class LoggerProviderTests
{
    [Fact]
    public async Task The_template_and_its_state_arrive_rather_than_the_formatted_string()
    {
        var line = await LoggedAsync(logger =>
            logger.LogWarning("User {UserId} failed login from {Ip}", 42, "203.0.113.7"));

        Assert.Equal("User {UserId} failed login from {Ip}", (string?)line["@mt"]);
        Assert.Null(line["@m"]);

        Assert.Equal(42, (int?)line["UserId"]);
        Assert.Equal("203.0.113.7", (string?)line["Ip"]);
    }

    /// <summary>
    /// Plain text is a template without holes and renders to itself, which is
    /// what keeps an application that never used a placeholder fully supported.
    /// </summary>
    [Fact]
    public async Task Plain_text_arrives_as_the_template_it_is()
    {
        var line = await LoggedAsync(logger => logger.LogInformation("Disk full on /dev/sda1"));

        Assert.Equal("Disk full on /dev/sda1", (string?)line["@mt"]);
    }

    /// <summary>
    /// `docs/ingestion.md` accepts both spellings and maps them without loss, so
    /// there is no mapping table here to write and get wrong. `Information` is
    /// left off because an absent level means exactly that.
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace, "Trace")]
    [InlineData(LogLevel.Debug, "Debug")]
    [InlineData(LogLevel.Information, null)]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Critical")]
    public async Task The_level_is_written_the_way_this_framework_spells_it(
        LogLevel level, string? expected)
    {
        var line = await LoggedAsync(logger => logger.Log(level, "Something happened"));

        Assert.Equal(expected, (string?)line["@l"]);
    }

    [Fact]
    public async Task The_category_becomes_the_property_the_installation_promotes()
    {
        var line = await LoggedAsync(logger => logger.LogInformation("Started"));

        Assert.Equal(
            "Logaffe.UnitTests.Client.LoggerProviderTests", (string?)line["SourceContext"]);
    }

    /// <summary>
    /// logaffe passes over <c>@</c> keys it does not know, so an <c>@i</c> would
    /// be silently dropped — whereas a property is stored and filterable.
    /// </summary>
    [Fact]
    public async Task The_event_id_becomes_an_ordinary_property()
    {
        var line = await LoggedAsync(logger =>
            logger.Log(LogLevel.Information, new EventId(4711, "CheckoutFailed"), "Started"));

        Assert.Null(line["@i"]);
        Assert.Equal(4711, (int?)line["EventId"]);
        Assert.Equal("CheckoutFailed", (string?)line["EventName"]);
    }

    [Fact]
    public async Task The_exception_arrives_as_the_runtime_wrote_it()
    {
        var line = await LoggedAsync(logger =>
            logger.LogError(new IOException("No space left on device"), "Writing failed"));

        Assert.Contains("No space left on device", (string?)line["@x"]);

        // Not folded into the message: it is the field an operator most often
        // wants shown, collapsed, or searched on its own.
        Assert.Equal("Writing failed", (string?)line["@mt"]);
    }

    [Fact]
    public async Task The_provider_names_the_instance()
    {
        Assert.Equal(
            Environment.MachineName,
            (string?)(await LoggedAsync(logger => logger.LogInformation("Started")))["instance"]);

        Assert.Null(
            (await LoggedAsync(
                logger => logger.LogInformation("Started"),
                options => options.Instance = null))["instance"]);
    }

    /// <summary>
    /// Off by default for logaffe's own rule rather than for taste: more than 64
    /// properties makes an entry invalid rather than truncated, and nested
    /// scopes are the easiest way to cross that without noticing.
    /// </summary>
    [Fact]
    public async Task Scopes_are_carried_only_when_the_application_asks()
    {
        var withoutThem = await LoggedAsync(logger =>
        {
            using var scope = logger.BeginScope(
                new Dictionary<string, object?> { ["OrderId"] = 4711 });

            logger.LogInformation("Started");
        });

        Assert.Null(withoutThem["OrderId"]);

        var withThem = await LoggedAsync(
            logger =>
            {
                using var scope = logger.BeginScope(
                    new Dictionary<string, object?> { ["OrderId"] = 4711 });

                logger.LogInformation("Started");
            },
            options => options.IncludeScopes = true);

        Assert.Equal(4711, (int?)withThem["OrderId"]);
    }

    /// <summary>
    /// The closer the writer is to the entry, the more it meant by it: a
    /// property on the entry wins over the same name in a scope.
    /// </summary>
    [Fact]
    public async Task An_entry_outranks_the_scope_it_is_inside()
    {
        var line = await LoggedAsync(
            logger =>
            {
                using var scope = logger.BeginScope(
                    new Dictionary<string, object?> { ["OrderId"] = 1 });

                logger.LogInformation("Order {OrderId}", 2);
            },
            options => options.IncludeScopes = true);

        Assert.Equal(2, (int?)line["OrderId"]);
    }

    /// <inheritdoc cref="SerilogSinkTests.An_entry_inside_a_trace_carries_it_where_logaffe_looks"/>
    [Fact]
    public async Task An_entry_inside_a_trace_carries_it_where_logaffe_looks()
    {
        using var source = new ActivitySource("logaffe.tests.provider");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("checkout");

        Assert.NotNull(activity);

        var line = await LoggedAsync(logger => logger.LogInformation("Started"));

        Assert.Equal(activity.TraceId.ToString(), (string?)line["TraceId"]);
        Assert.Equal(activity.SpanId.ToString(), (string?)line["SpanId"]);
    }

    /// <summary>
    /// CLEF's own space. A property called <c>@t</c> would not be a property, it
    /// would be a second timestamp — and an entry with two of them is one
    /// logaffe cannot read.
    /// </summary>
    [Fact]
    public async Task A_property_cannot_be_written_into_the_reserved_names()
    {
        var line = await LoggedAsync(logger =>
        {
            using var scope = logger.BeginScope(
                new Dictionary<string, object?> { ["@t"] = "not a timestamp" });

            logger.LogInformation("Started");
        },
        options => options.IncludeScopes = true);

        Assert.NotEqual("not a timestamp", (string?)line["@t"]);
        Assert.True(DateTimeOffset.TryParse((string?)line["@t"], out _));
    }

    /// <summary>
    /// A container never disposes an object it did not create, so a ready-made
    /// provider handed to it would be one whose flush on shutdown never runs —
    /// and an application's last entries would never leave. Found while wiring
    /// the same provider up against a running installation, where nothing
    /// arrived and nothing reported a failure.
    /// </summary>
    [Fact]
    public void The_builder_registers_a_provider_the_container_will_dispose()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
            builder.AddLogaffe(new Uri("https://logs.example.com"), "lgf_i_test"));

        var registered = Assert.Single(
            services, service => service.ServiceType == typeof(ILoggerProvider));

        Assert.NotNull(registered.ImplementationFactory);
        Assert.Null(registered.ImplementationInstance);
    }

    /// <summary>
    /// One call is one sender.
    /// </summary>
    /// <remarks>
    /// A second call carries a second installation and a second project's token,
    /// which is a configuration rather than a duplicate — and de-duplicating by
    /// the provider's own type, as the framework's own providers do, dropped both
    /// of them without a word.
    /// </remarks>
    [Fact]
    public void Every_call_registers_a_sender_of_its_own()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddLogaffe(new Uri("https://logs.example.com"), "lgf_i_one");
            builder.AddLogaffe(new Uri("https://other.example.com"), "lgf_i_two");
        });

        var registered = services
            .Where(service => service.ServiceType == typeof(ILoggerProvider))
            .ToList();

        Assert.Equal(2, registered.Count);
        Assert.All(registered, service => Assert.NotNull(service.ImplementationFactory));
    }

    /// <summary>
    /// An application bringing its own <c>HttpClient</c> keeps the flush on
    /// shutdown.
    /// </summary>
    /// <remarks>
    /// The flush is the container disposing the provider, and the only thing
    /// taking a client used to be the provider's constructor — a provider the
    /// application built itself, which is exactly the one the container will not
    /// dispose. The careful sender lost its last entries for being careful.
    /// </remarks>
    [Fact]
    public async Task A_sender_bringing_its_own_client_is_still_flushed_by_the_container()
    {
        using var installation = new TakingDeliveries();
        using var http = new HttpClient(installation);

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLogaffe(
            options =>
            {
                options.Installation = new Uri("https://logs.example.com");
                options.IngestToken = "lgf_i_test";

                // Longer than this test will live: what arrives, arrives because
                // shutdown flushed it and for no other reason.
                options.BatchInterval = TimeSpan.FromMinutes(5);
            },
            http));

        var container = services.BuildServiceProvider();

        container.GetRequiredService<ILoggerFactory>()
            .CreateLogger("checkout")
            .LogInformation("last words");

        await container.DisposeAsync();

        var line = JsonNode.Parse(Assert.Single((await installation.TakeAsync(1)).Lines))!;

        Assert.Equal("last words", (string?)line["@mt"]);
    }

    private static async Task<JsonNode> LoggedAsync(
        Action<ILogger> log, Action<LogaffeLoggerOptions>? configure = null)
    {
        using var installation = new TakingDeliveries();
        using var http = new HttpClient(installation);

        var options = new LogaffeLoggerOptions
        {
            Installation = new Uri("https://logs.example.com"),
            IngestToken = "lgf_i_test",
            BatchInterval = TimeSpan.FromMilliseconds(50),
        };

        configure?.Invoke(options);

        using (var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new LogaffeLoggerProvider(options, http));
        }))
        {
            log(factory.CreateLogger<LoggerProviderTests>());
        }

        return JsonNode.Parse(Assert.Single((await installation.TakeAsync(1)).Lines))!;
    }
}

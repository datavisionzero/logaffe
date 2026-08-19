using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Logaffe.Client;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace Logaffe.UnitTests.Client;

/// <summary>
/// What arrives at the endpoint when an application logs through Serilog.
/// </summary>
/// <remarks>
/// The sink is <c>CompactJsonFormatter</c> pointed at the endpoint, so what is
/// worth asking of it is what the line says — and the one that matters most is
/// what it does <em>not</em> say: an entry carrying <c>@m</c> is refused by
/// logaffe, per entry rather than per delivery, so picking the rendered
/// formatter would fail quietly and completely.
/// </remarks>
public sealed class SerilogSinkTests
{
    [Fact]
    public async Task The_template_arrives_rather_than_the_rendered_message()
    {
        var line = await LoggedAsync(logger =>
            logger.Warning("User {UserId} failed login from {Ip}", 42, "203.0.113.7"));

        Assert.Equal("User {UserId} failed login from {Ip}", (string?)line["@mt"]);

        // The trap this package exists to avoid.
        Assert.Null(line["@m"]);

        // The values arrive as values, which is what makes them filterable.
        Assert.Equal(42, (int?)line["UserId"]);
        Assert.Equal("203.0.113.7", (string?)line["Ip"]);
    }

    /// <summary>
    /// CLEF's own rule and logaffe's: an absent level means <c>Information</c>,
    /// and the formatter leaves it out at exactly that level.
    /// </summary>
    [Fact]
    public async Task The_ordinary_level_is_left_off_and_the_others_are_written()
    {
        Assert.Null((await LoggedAsync(logger => logger.Information("Started")))["@l"]);

        Assert.Equal(
            "Warning",
            (string?)(await LoggedAsync(logger => logger.Warning("Slow")))["@l"]);

        Assert.Equal(
            "Verbose",
            (string?)(await LoggedAsync(logger => logger.Verbose("Chatter")))["@l"]);
    }

    /// <summary>
    /// A format specifier is used to find the name and then dropped by the
    /// server, so the value is delivered as it was captured. Serilog's own
    /// rendering of it arrives as <c>@r</c>, an <c>@</c> key logaffe passes over
    /// rather than counting the entry invalid.
    /// </summary>
    [Fact]
    public async Task A_format_specifier_does_not_cost_the_value()
    {
        var line = await LoggedAsync(logger =>
            logger.Information("Took {Elapsed:0.000} ms", 12.3456));

        Assert.Equal("Took {Elapsed:0.000} ms", (string?)line["@mt"]);
        Assert.Equal(12.3456, (double?)line["Elapsed"]);
    }

    /// <summary>
    /// The single most useful filter for cutting framework noise, and the sink
    /// does nothing for it: Serilog sets it and logaffe promotes it.
    /// </summary>
    [Fact]
    public async Task The_logger_name_arrives_as_an_ordinary_property()
    {
        var line = await LoggedAsync(logger =>
            logger.ForContext("SourceContext", "Orders.Api.CheckoutController")
                .Information("Started"));

        Assert.Equal("Orders.Api.CheckoutController", (string?)line["SourceContext"]);
    }

    [Fact]
    public async Task The_sink_names_the_instance()
    {
        var line = await LoggedAsync(logger => logger.Information("Started"));

        Assert.Equal(Environment.MachineName, (string?)line["instance"]);
    }

    [Fact]
    public async Task An_event_that_names_its_own_instance_keeps_it()
    {
        var line = await LoggedAsync(logger =>
            logger.ForContext("instance", "api-7c4f").Information("Started"));

        Assert.Equal("api-7c4f", (string?)line["instance"]);
    }

    [Fact]
    public async Task The_instance_can_be_turned_off()
    {
        var line = await LoggedAsync(logger => logger.Information("Started"), instance: null);

        Assert.Null(line["instance"]);
    }

    /// <summary>
    /// Serilog carries the trace on the event rather than among its properties,
    /// and the formatter writes it as CLEF's <c>@tr</c> — a key logaffe passes
    /// over. Written as properties too, so that correlating an entry with its
    /// request needs nothing of the application.
    /// </summary>
    [Fact]
    public async Task An_entry_inside_a_trace_carries_it_where_logaffe_looks()
    {
        using var source = new ActivitySource("logaffe.tests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("checkout");

        Assert.NotNull(activity);

        var line = await LoggedAsync(logger => logger.Information("Started"));

        Assert.Equal(activity.TraceId.ToString(), (string?)line["TraceId"]);
        Assert.Equal(activity.SpanId.ToString(), (string?)line["SpanId"]);
    }

    /// <summary>
    /// The event belongs to the logger and every other sink is holding the same
    /// one, so what this sink adds for logaffe must not turn up in the
    /// application's console.
    /// </summary>
    [Fact]
    public async Task What_the_sink_adds_stays_in_the_delivery()
    {
        var seen = new List<LogEvent>();

        await LoggedAsync(
            logger => logger.Information("Started"),
            Environment.MachineName,
            configure: configuration => configuration.WriteTo.Sink(new Collecting(seen)));

        Assert.DoesNotContain(
            global::Logaffe.Serilog.LogaffeSink.InstanceProperty,
            Assert.Single(seen).Properties.Keys);
    }

    /// <summary>
    /// What the package promises, and it holds however the sink was configured
    /// rather than only for the shortest way of doing it.
    /// </summary>
    /// <remarks>
    /// The sender who hands over <see cref="EntryDeliveryOptions"/> is the one
    /// naming an instance among replicas, or bringing an <c>HttpClient</c> —
    /// which is to say the one running the installation that most needs to hear
    /// that nothing is arriving. There is nowhere else for this sink to say it:
    /// through Serilog it would be handed straight back here, and to the
    /// installation it cannot be said at all.
    /// </remarks>
    [Fact]
    public async Task A_delivery_that_failed_is_reported_even_when_the_sender_named_nowhere()
    {
        var selfLog = new ConcurrentQueue<string>();

        SelfLog.Enable(selfLog.Enqueue);

        try
        {
            await IntoTheDarkAsync(onFailure: null);
        }
        finally
        {
            SelfLog.Disable();
        }

        Assert.Contains(selfLog, said => said.Contains("were not delivered", StringComparison.Ordinal));
    }

    /// <summary>
    /// A default is only added under what the sender left unset: reporting they
    /// brought themselves is reporting they want.
    /// </summary>
    [Fact]
    public async Task A_senders_own_reporting_is_what_is_used()
    {
        var selfLog = new ConcurrentQueue<string>();
        var mine = new ConcurrentQueue<string>();

        SelfLog.Enable(selfLog.Enqueue);

        try
        {
            await IntoTheDarkAsync((what, _) => mine.Enqueue(what));
        }
        finally
        {
            SelfLog.Disable();
        }

        Assert.Contains(mine, said => said.Contains("were not delivered", StringComparison.Ordinal));
        Assert.Empty(selfLog);
    }

    /// <summary>
    /// One entry logged at an installation that answers nothing, flushed on the
    /// way out — so that whatever there is to report has been reported by the
    /// time this returns.
    /// </summary>
    private static async Task IntoTheDarkAsync(Action<string, Exception?>? onFailure)
    {
        using var installation = new TakingDeliveries
        {
            Fails = new HttpRequestException("no route to host"),
        };

        using var http = new HttpClient(installation);

        using (var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new global::Logaffe.Serilog.LogaffeSink(
                new EntryDeliveryOptions
                {
                    Installation = new Uri("https://logs.example.com"),
                    IngestToken = "lgf_i_test",
                    BatchInterval = TimeSpan.FromMilliseconds(50),
                    OnFailure = onFailure,
                },
                instance: null,
                http))
            .CreateLogger())
        {
            logger.Information("nobody is listening");
        }

        await installation.TakeAsync(1);
    }

    private static async Task<JsonNode> LoggedAsync(
        Action<ILogger> log,
        string? instance = "",
        Func<LoggerConfiguration, LoggerConfiguration>? configure = null)
    {
        using var installation = new TakingDeliveries();
        using var http = new HttpClient(installation);

        var delivery = new EntryDeliveryOptions
        {
            Installation = new Uri("https://logs.example.com"),
            IngestToken = "lgf_i_test",
            BatchInterval = TimeSpan.FromMilliseconds(50),
        };

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new global::Logaffe.Serilog.LogaffeSink(
                delivery,
                // "" stands for "not said", so that a test can pass null and
                // mean it — the same distinction the overloads make.
                instance == "" ? Environment.MachineName : instance,
                http));

        using (var logger = (configure?.Invoke(configuration) ?? configuration).CreateLogger())
        {
            log(logger);
        }

        var delivered = await installation.TakeAsync(1);

        return JsonNode.Parse(Assert.Single(delivered.Lines))!;
    }

    /// <summary>A second sink, standing in for the application's console.</summary>
    private sealed class Collecting(List<LogEvent> seen) : global::Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => seen.Add(logEvent);
    }
}


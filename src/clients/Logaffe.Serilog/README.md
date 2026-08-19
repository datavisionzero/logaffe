# Logaffe.Serilog

A Serilog sink that delivers to a
[logaffe](https://github.com/datavisionzero/logaffe) installation — a
self-hostable, central logging tool for a single operator and their AI agent.

logaffe ingests [CLEF](https://clef-json.org/), the format
`Serilog.Formatting.Compact` already writes. **The sink is therefore
configuration rather than a mapping layer**: nothing is translated on the way
out, and what an operator searches is what Serilog produced.

## Use

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.Logaffe(
        new Uri("https://logs.example.com"),
        "logaffe_ingest_…")
    .CreateLogger();
```

Keep the console or file sink. logaffe is *additive* — it does not replace the
log the application already writes, and that is what makes a lost delivery
affordable.

A delivery never names a project: **the token is the project**. The address is
scheme and host as the operator reaches the installation — the ingest path is
appended and is not a setting.

### Naming the instance

Three replicas of one service are only separable if they say who they are. The
default is `Environment.MachineName`; the overload takes something else, and
`null` leaves it off:

```csharp
.WriteTo.Logaffe(delivery, instance: Environment.GetEnvironmentVariable("HOSTNAME"))
```

An event that already carries its own `instance` property keeps it.

### Everything else

```csharp
.WriteTo.Logaffe(
    new EntryDeliveryOptions
    {
        Installation  = new Uri("https://logs.example.com"),
        IngestToken   = "logaffe_ingest_…",
        QueueCapacity = 50_000,
    },
    restrictedToMinimumLevel: LogEventLevel.Information)
```

`restrictedToMinimumLevel` and `levelSwitch` work as they do on any sink.

## What it promises

**Fire-and-forget.** A bounded in-memory queue, dropping the oldest entries when
full; never throwing into the calling application and never blocking it; a flush
with a timeout when the logger is closed. An unbounded queue would turn a
logging outage into an application outage, which is the thing this exists to
prevent.

**Failures go to `SelfLog`**, Serilog's own channel for what a sink cannot report
through the logger it is part of — reporting a failed delivery through Serilog
would hand it straight back to this sink. Turn it on while setting things up:

```csharp
Serilog.Debugging.SelfLog.Enable(Console.Error);
```

That holds however the sink was configured, including the overloads taking
`EntryDeliveryOptions`. Set `OnFailure` on those options to send the reports
somewhere else instead; what you set is kept.

**Call `Log.CloseAndFlush()` on shutdown**, as with any Serilog sink. Without it
the application's last entries never leave.

## What logaffe does with what you send

**The server renders the template.** logaffe stores `@mt` and the properties,
not a pre-rendered string, and substitutes only those placeholders for which a
property of the same name arrived — everything else stays character for
character. The rule is narrower than Serilog's on purpose: log content is
untrusted, and Serilog's escaping would rewrite an application that logs a raw
request body. A format specifier such as `{Elapsed:0.000}` is read for the name
and then dropped, because values are stored as they arrived.

**`SourceContext`, `TraceId` and `SpanId` are promoted** to indexed fields when
present — and Serilog sets all three on its own in an ordinary ASP.NET Core
application, so the most useful filter for cutting framework noise costs you
nothing.

**Exceptions land in `@x`** as text, stored as delivered and never parsed, so
they can be shown, collapsed, or searched on their own.

Full detail: [docs/ingestion.md](https://github.com/datavisionzero/logaffe/blob/main/docs/ingestion.md).

## Requirements

.NET 8 or later. MIT licensed.

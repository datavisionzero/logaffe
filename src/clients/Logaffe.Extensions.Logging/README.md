# Logaffe.Extensions.Logging

An `ILoggerProvider` that delivers to a
[logaffe](https://github.com/datavisionzero/logaffe) installation — a
self-hostable, central logging tool for a single operator and their AI agent.

For applications on `Microsoft.Extensions.Logging` rather than Serilog. It
builds [CLEF](https://clef-json.org/) from `ILogger`'s message template and
state, so nothing about how the application already logs has to change.

## Use

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddLogaffe(
    new Uri("https://logs.example.com"),
    builder.Configuration["Logaffe:IngestToken"]!);
```

The console provider stays where it is. logaffe is *additive* — it does not
replace the log the application already writes, and that is what makes a lost
delivery affordable.

A delivery never names a project: **the token is the project**. The address is
scheme and host as the operator reaches the installation — the ingest path is
appended and is not a setting.

### Everything else

```csharp
builder.Logging.AddLogaffe(options =>
{
    options.Installation  = new Uri("https://logs.example.com");
    options.IngestToken   = builder.Configuration["Logaffe:IngestToken"]!;
    options.Instance      = Environment.GetEnvironmentVariable("HOSTNAME");
    options.IncludeScopes = true;
});
```

| | default | |
| --- | --- | --- |
| `Instance` | `Environment.MachineName` | what separates three replicas of one service; `null` leaves it off |
| `IncludeScopes` | `false` | carry scope state as properties |
| `QueueCapacity` | `10_000` | oldest dropped when full |
| `BatchInterval` | `1s` | how long the first entry waits for company |
| `FlushTimeout` | `5s` | how long shutdown keeps trying |
| `DeliveryTimeout` | `10s` | how long one request may take |
| `OnFailure` | `Console.Error` | `(message, exception)` — where problems are reported |

### Your own `HttpClient`

```csharp
builder.Logging.AddLogaffe(options => { … }, httpClient);
```

The client stays yours and is not disposed with the provider. Take this overload
rather than constructing `LogaffeLoggerProvider` yourself and calling
`AddProvider` — a container never disposes what it did not create, and that
disposal is the flush.

**Each call adds one sender.** Two calls are two installations, each with its own
token, queue and flush.

**The application's own filters apply first.** This is a provider like any
other, so `LogLevel` configuration and category filters decide what reaches it;
there is no second filter to keep in step.

## What it promises

**Fire-and-forget.** A bounded in-memory queue, dropping the oldest entries when
full; never throwing into the calling application and never blocking it; a flush
with a timeout on shutdown. An unbounded queue would turn a logging outage into
an application outage, which is the thing this exists to prevent.

**The host disposes it, which is what flushes it.** The provider is registered
as a factory rather than a ready-made instance, because a container never
disposes an object it did not create — and without that disposal the flush never
runs and an application's last entries never leave. Every `AddLogaffe` overload
registers one, the one taking an `HttpClient` included.

**Nothing is guaranteed to arrive.** No durable buffer, no retry outliving the
process. The application still has its own log, which is where a failed delivery
is reported through `OnFailure`.

## What logaffe does with what you send

**The server renders the template.** The message template and the properties are
stored, not a pre-rendered string, and only those placeholders are substituted
for which a property of the same name arrived — everything else stays character
for character, because log content is untrusted.

**Levels map without loss.** `Trace` is logaffe's `Verbose`, `Critical` is
`Fatal`, and the four in between are identical. Both spellings are accepted.

**`instance`, `SourceContext`, `TraceId` and `SpanId` are promoted** to indexed
fields when present, and this package fills all four for you: the logger
category arrives as `SourceContext`, which makes cutting framework noise a
one-click filter, and the trace and span are taken from `Activity.Current` — so
an entry correlates with the request it belongs to without the application
passing anything.

**Exceptions land in `@x`** as text, stored as delivered and never parsed.

Full detail: [docs/ingestion.md](https://github.com/datavisionzero/logaffe/blob/main/docs/ingestion.md).

## Requirements

.NET 8 or later. MIT licensed.

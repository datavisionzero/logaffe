# Logaffe.Client

Delivery of log entries to a [logaffe](https://github.com/datavisionzero/logaffe)
installation — a self-hostable, central logging tool for a single operator and
their AI agent.

This is the layer underneath the two logging-framework packages, and it is the
one to reach for when the application logs through neither of them:

- **[Logaffe.Serilog](https://www.nuget.org/packages/Logaffe.Serilog)** — a Serilog sink
- **[Logaffe.Extensions.Logging](https://www.nuget.org/packages/Logaffe.Extensions.Logging)** — an `ILoggerProvider`

## Use

```csharp
using Logaffe.Client;

await using var delivery = new EntryDelivery(new EntryDeliveryOptions
{
    Installation = new Uri("https://logs.example.com"),
    IngestToken  = "logaffe_ingest_…",
});

delivery.Send("""
    {"@t":"2026-08-13T14:12:03.417Z","@mt":"Order {OrderId} shipped","OrderId":4711}
    """);
```

`Send` takes one [CLEF](https://clef-json.org/) line and returns immediately.
It never throws and never blocks.

A delivery never names a project: **the token is the project**. The address is
scheme and host as the operator reaches the installation — the ingest path is
appended and is not a setting.

## What it promises

**Fire-and-forget, and it means it.** Entries go into a bounded in-memory queue
and a background loop delivers them. The queue drops the *oldest* entries when
it is full rather than growing, because an unbounded queue turns a logging
outage into an outage of the application, which is the one thing this exists to
prevent.

**Nothing is guaranteed to arrive.** There is no durable client-side buffer and
no retry that outlives the process. This is affordable because logaffe is
*additive*: the application keeps its own file logging, so a delivery that is
lost costs a convenience and never the record.

**Failures are reported to you, not swallowed.** `OnFailure` is a delegate
rather than an `ILogger`, so this package asks nothing of the application's
logging stack. An installation that could not be reached cannot be told that it
could not be reached, so the report belongs in the local log that already
exists.

**Disposal flushes, with a deadline.** `Dispose`/`DisposeAsync` spends up to
`FlushTimeout` delivering what is still queued. What does not go in that time is
lost — which is what fire-and-forget means.

## Settings

| | default | |
| --- | --- | --- |
| `Installation` | *required* | scheme and host of the installation |
| `IngestToken` | *required* | the project's ingest token; write-only, grants no reads |
| `QueueCapacity` | `10_000` | ten full batches; oldest dropped when full |
| `BatchInterval` | `1s` | how long the first entry waits for company |
| `FlushTimeout` | `5s` | how long disposal keeps trying |
| `DeliveryTimeout` | `10s` | how long one request may take |
| `OnFailure` | `null` | `(message, exception)` — where problems are reported |

The batch limits — a thousand entries, five mebibytes — are product values
rather than sender settings, and are applied for you.

An overload takes your own `HttpClient` when the application manages its own.

## The format

One JSON object per line. `@t` (an instant with an offset or `Z`) and `@mt` (a
message template) are required; `@l` defaults to `Information`; `@x` carries an
exception as text. Everything not beginning with `@` is a property.

**The server renders the template**, so `@m` is refused rather than ignored —
there is one place where rendering happens. Placeholders are substituted only
where a property of the same name was delivered; everything else stays character
for character, because log content is untrusted and an application logging a raw
request body will eventually log braces.

`instance`, `SourceContext`, `TraceId` and `SpanId` are ordinary properties that
the installation promotes to indexed fields when present. Supplying none of them
is fully supported.

Full detail: [docs/ingestion.md](https://github.com/datavisionzero/logaffe/blob/main/docs/ingestion.md).

## Requirements

.NET 8 or later. MIT licensed.

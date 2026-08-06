# Ingestion

Getting logs in is the adoption barrier, and `VISION.md` judges every decision on
this path by how easy it is for an application that writes log files today to
start delivering. What follows is the whole of that path as behaviour: the format
on the wire, what an entry carries, what happens to a batch that is partly
broken, where the limits sit, and what the .NET packages do.

Two things are settled elsewhere and only referenced here. **Delivery is
fire-and-forget** — it must never block or slow down the sending application, and
there is no durable client-side buffering and no delivery guarantee. And logaffe
is **additive**: the application keeps its local file logging, so a batch that is
refused or lost costs a convenience, never the record. Both are why this document
can be as relaxed about loss as it is.

## The format is CLEF

The body of a delivery is **newline-delimited JSON, one object per log entry**,
in [CLEF](https://clef-json.org/) — the Compact Log Event Format that Serilog's
`Serilog.Formatting.Compact` writes.

```
POST /ingest
Authorization: Bearer <ingest token>
Content-Type: application/x-ndjson

{"@t":"2026-08-06T07:12:03.417Z","@l":"Warning","@mt":"User {UserId} failed login from {Ip}","UserId":42,"Ip":"203.0.113.7","instance":"api-7c4f"}
{"@t":"2026-08-06T07:12:04.002Z","@l":"Error","@mt":"Disk full on /dev/sda1","@x":"System.IO.IOException: No space left on device\n   at …"}
```

The format is adopted rather than invented ([ADR 0004](./adr/0004-the-ingestion-format-is-clef-and-the-server-renders.md)).
Keys beginning with `@` are the entry's own fields; every other key is a
property. The second line above is what a `curl` sender writes, and it needs to
know nothing about templates, properties or Serilog to write it.

## What an entry carries

### The message is a template, and plain text is a template without holes

`@mt` is required and is always a **message template**. `"Disk full on
/dev/sda1"` is a valid one — it has no placeholders and renders to itself. There
is therefore exactly one message field on the wire, and no question of what
happens when a rendered form and a template disagree.

**`@m` is refused.** CLEF allows a pre-rendered message in `@m`, and logaffe
rejects any entry carrying it rather than ignoring it, so that there is one place
where rendering happens and it is the server.

**The rendering rule is narrower than Serilog's.** logaffe substitutes only those
placeholders for which a property of the same name was delivered. Everything
else — an unmatched `{Foo}`, a doubled `{{`, any other brace — stays character
for character as it arrived. This is deliberate and it exists because log content
is untrusted: an application logging a raw request body will sooner or later log
braces, and Serilog's escaping would rewrite that text. Under this rule a plain
line always renders to itself byte for byte.

### The level

`@l` carries one of Serilog's six levels — `Verbose`, `Debug`, `Information`,
`Warning`, `Error`, `Fatal` — matched case-insensitively. Per CLEF, **an absent
`@l` means `Information`**, which is what keeps the `curl` case short.

`Microsoft.Extensions.Logging` maps onto these without loss: `Trace` is
`Verbose`, `Critical` is `Fatal`, the four in between are identical. Both
spellings are accepted. A level that is neither makes the entry invalid; it is
not quietly coerced to `Information`, because a wrong level is worse than a
counted rejection the operator can see.

### Two clocks

`@t` is required, is the **event time**, and is supplied by the sender. It must
carry an offset or `Z`; a local time without one is invalid. logaffe records a
second timestamp of its own when the batch arrives.

The two are used for different things. **The UI orders by `@t`**, because that is
the order in which things happened. **Retention counts from the receipt time**,
because an application with a wrong clock would otherwise keep its rows forever
or lose them on arrival ([ADR 0007](./adr/0007-the-sender-orders-the-receipt-expires.md)).

### The source below the project

The token is the project, so a delivery never names one. Below the project sits
an optional **`instance`** property, set by the sender and typically a hostname or
container name, which is what makes three replicas of one service separable in
the UI and over MCP. It is an ordinary CLEF property that logaffe **promotes** to
a first-class, indexed field.

Two other properties are promoted the same way when present, and both are set by
Serilog on its own in a normal ASP.NET Core application: **`SourceContext`**, the
logger name, which is the single most useful filter for cutting framework noise,
and **`TraceId`** together with **`SpanId`**, which correlate an entry with the
request it belongs to. Promotion is the whole mechanism — nothing is required of
the sender, and an application that supplies none of them is fully supported.

### The exception

`@x` is optional and is a string: whatever the runtime produced, stack trace and
all, stored as delivered and never parsed. It is not folded into the message,
because it is the field an operator most often wants shown, collapsed, or
searched on its own.

## The batch

### A batch is accepted in part

Valid entries are stored and invalid ones are counted; one broken entry never
costs the other 499. This follows from fire-and-forget: the sender will not
retry and will not look at the answer, so refusing a batch is a permanent,
silent loss ([ADR 0006](./adr/0006-a-batch-is-accepted-in-part.md)).

The response is `200` with a small JSON body naming how many entries were
accepted, how many were rejected, and the first few reasons with their line
numbers. Nothing in a sender's control flow depends on it — it exists so that a
person debugging a new integration with `curl` can see what is wrong.

An entry is invalid when `@t` is missing or unparsable, `@mt` is missing, `@l` is
unrecognized, `@m` is present, or the line is not a JSON object.

### Backpressure and refusal

The whole batch is refused, with nothing stored, in exactly three cases: the
token is bad (`401`), the batch exceeds a hard limit (`413`), or the project is
over its rate limit or quota (`429`). If logaffe cannot store at all — the
database is unreachable — it answers `503` and the batch is gone. That is what
fire-and-forget means, and it is the reason the application still has its file.

A bad token answers `401` and says nothing further. It does not reveal whether
the project exists, whether the token once existed, or whether it was revoked;
the endpoint is on the public internet and an unauthenticated caller learns
nothing from it.

## The limits

The numbers are product values — documented, and the same in every installation
rather than something the operator tunes.

- **1 000 entries** per batch
- **5 MiB** per batch, measured **after** decompression
- **32 KiB** per rendered message, **64 KiB** per `@x`
- **64 properties** per entry, values scalar or one level of nesting

`Content-Encoding: gzip` is accepted, and the size cap counts the decompressed
body, so the cap cannot be walked around with a compression bomb.

A message or exception over its cap is **truncated and flagged**, not refused, so
that a four-megabyte stack trace costs its tail rather than the whole entry. This
is the one place on this path where what is stored differs from what arrived, and
`VISION.md` names it as the sole exception to "stored as delivered";
[ADR 0008](./adr/0008-an-over-long-message-is-truncated-not-refused.md) records
why it wins over refusing the entry.

## Authentication

The ingest token travels as `Authorization: Bearer <token>`. It is a
high-entropy random value, stored **encrypted rather than hashed**, with the key
on the host volume and never in the database — so the operator can read a token
back whenever they need it, and a stolen database backup yields nothing usable
([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)).

**Rotation overlaps.** A project can hold two valid tokens at the same time, so
the operator issues the new one, rolls the deployments over, and revokes the old
one afterwards. A rotation with a hard cutover would put a gap into delivery for
every application still holding the old value, and a logging system that drops
data when its credentials are maintained teaches operators not to maintain them.

## The .NET packages

Two convenience packages sit on top of the endpoint, and neither is required —
everything above works with `curl`.

- A **Serilog sink**, which is `CompactJsonFormatter` pointed at the endpoint.
  Because the format is CLEF, the sink is configuration rather than a mapping
  layer.
- An **`ILoggerProvider`** for applications not on Serilog, which builds the same
  CLEF from `ILogger`'s message template and state.

Both behave identically under stress, and the behaviour is the fire-and-forget
promise made concrete: a **bounded in-memory queue**, dropping the oldest entries
when it is full; **never throwing** into the calling application and **never
blocking** it; a **flush with a timeout** on shutdown; and delivery failures
reported to the application's own local log, which is exactly where the additive
model says the record already is.

## What is deliberately not here

- **No deduplication.** A proxy retry or a client-level repeat produces two rows
  and logaffe stores both. Detecting them would mean an identity for an entry
  that senders do not supply and a lookup on the hottest write path, in exchange
  for a rare cosmetic problem.
- **No acknowledgement a sender waits on.** The response is diagnostic. There is
  no confirmation semantic, no receipt to store, and no way for an application to
  learn later whether an entry landed.
- **No scrubbing, filtering or classification on the way in.** Settled in
  `VISION.md`: log entries are stored as delivered, and the only modification on
  this path is the declared truncation above.
- **No implicit project creation.** Settled in `VISION.md`: a token exists
  because the operator created a project and issued it.
- **No OTLP on the primary path.** Settled in `VISION.md`.
- **No compression other than gzip**, and no binary framing. The format has to
  stay writable by hand.

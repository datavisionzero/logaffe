# Repeated Text Is Stored, Not Interned

The logger name, the instance and the message template are written as text into
every log entry, and not as references into dictionary tables holding each
distinct string once. All three repeat endlessly — an application has a few dozen
loggers, a handful of instances and a few hundred distinct log statements, spread
over millions of entries — and none of that repetition is compressed away,
because the values are far too short for Postgres to consider them for TOAST or
column compression. A measurement at ten million entries put the whole store at
11.84 GiB, of which interning was **estimated** to save around 1.9 GiB, roughly a
sixth. That figure was never measured.

It was rejected because of what it costs on the other side. Today the ingestion
path parses a batch and copies it, and it holds nothing between requests.
Interning turns that into parsing, resolving each string against an in-memory
dictionary, inserting the ones never seen before under concurrency, and only then
copying — a stateful step on the hottest path in the product, whose entire case
is being small enough to reason about. A sixth of a store that is already bounded
by retention and by moderate volume is not what decides whether this product stays
small; the complexity of its write path is.

## Consequences

**The entry table is knowingly about a sixth larger than it needs to be**, and
the logger name index is the second-largest object in the database because its
keys carry full logger names rather than integers.

**The ingestion path stays stateless.** A batch is parsed and copied, nothing has
to be looked up or created first, and a delivery of entries the installation has
never seen before is not a different case from any other.

This does not reopen
[ADR 0005](./0005-the-rendered-message-is-stored-not-recomputed.md), which
accepted storing the message twice on the same grounds and remains the larger
contributor of the two.

If this is ever reopened — because the volume assumptions changed, or because a
sixth turned out to be a serious underestimate — the thing to do first is measure
rather than estimate. The prototype branch answers it with a parameter change,
and the estimate above is exactly the kind of number that was wrong by a factor
of four the last time this product guessed instead of measuring.

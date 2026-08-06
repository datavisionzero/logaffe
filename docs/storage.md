# Storage

One table dominates this product and everything else in the database is small.
[ADR 0003](./adr/0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)
draws the boundary: EF Core declares every table and owns the migrations that
apply themselves on startup, and it also *serves* everything except the log
entries, which are written with Npgsql's binary `COPY` and read with hand-written
SQL through Dapper.

This document is the log entry table — what each column is for, which indexes
exist and why, and what the whole thing costs. The operator account, the
projects, the tokens and the settings are ordinary relational rows and need no
document of their own.

The numbers quoted below were measured on a containerised Postgres capped at two
cores and 4 GB, holding ten million entries across twenty projects — the shape of
host `VISION.md` targets rather than a generous one.

## The log entry table

```sql
create table log_entry (
    id                  bigint      not null primary key,
    project_id          uuid        not null,

    event_time          timestamptz not null,   -- the sender's clock
    receipt_time        timestamptz not null,   -- ours

    level               smallint    not null,
    logger_name         text,
    instance            text,
    trace_id            bytea,                  -- 16 bytes, not 32 hex characters
    span_id             bytea,                  --  8 bytes

    message_template    text        not null,
    rendered_message    text        not null,
    exception           text,
    properties          jsonb,

    message_truncated   boolean     not null default false,
    exception_truncated boolean     not null default false
);
```

An entry is **never updated**. It is written once by the ingestion path and it
leaves only by ageing out, which is what allows the read path to be fitted to its
indexes without regard for write amplification on update.

## The identity is a number the installation hands out

`id` is a `bigint` assigned by the ingestion path before the row is written, not
by a database sequence.

Binary `COPY` has to carry the value with the row, so a sequence would mean a
`nextval` per entry or a round trip per batch on the hottest path in the product.
An installation is a **single writer** — one container, one ingestion endpoint —
so a counter in memory, seeded from the high-water mark at startup and handed out
in blocks, is all that is needed. Gaps are irrelevant: nothing counts ids, and
nothing assumes they are dense.

It is not a UUIDv7, which would also be time-sortable. A UUID is sixteen bytes
where this is eight, and it pays that in **every index that carries it** — which
is every index on this table, because the identity is the cursor's tie-break. The
distributed-writer problem UUIDv7 solves does not exist here.

The primary key is the only index whose job is not a query. It exists because the
cursor of [Querying](./querying.md) is `(event_time, id)` and is only total if
`id` is unique — the uniqueness is load-bearing, not decoration. At 294 MiB per
ten million entries it is also the cheapest index on the table.

## Two clocks, two indexes

The two timestamps of [ADR 0007](./adr/0007-the-sender-orders-the-receipt-expires.md)
are read by different things, so each has its own index.

```sql
create index on log_entry (project_id, event_time desc, id desc);
create index on log_entry (project_id, receipt_time, id);
```

The first is the **page**: newest first by event time, identity breaking ties,
which is exactly the order `docs/querying.md` promises and exactly the shape of
its cursor. It is what makes paging independent of depth — a page five thousand
entries into a project came back in under 1.3 ms at ten million entries, the same
as the first page.

The second serves the two things that run on receipt time: the **live tail**,
whose cursor asks what has arrived since it last asked
([ADR 0009](./adr/0009-the-tail-follows-the-receipt-the-view-keeps-the-order-of-events.md)),
and the **retention sweep**, which deletes by the same clock
([ADR 0023](./adr/0023-retention-deletes-rows-rather-than-dropping-partitions.md)).

Both lead with `project_id`, because nothing in this product reads across
projects and an index that did not lead with it would make every query pay for
every other project's entries.

## The promoted properties

`instance`, the logger name, the trace and the span are ordinary CLEF properties
that the ingestion path lifts into columns of their own
([Ingestion](./ingestion.md)). Two of them carry an index:

```sql
create index on log_entry (project_id, logger_name, event_time desc);
create index on log_entry (project_id, instance,    event_time desc);
create index on log_entry (project_id, trace_id,    event_time desc);
```

The logger name index is the second-largest object in the database at 1.39 GiB
per ten million entries, and it stays that way because
[ADR 0027](./adr/0027-repeated-text-is-stored-not-interned.md) keeps full logger
names in the keys rather than references. It earns it: filtering by logger name
is the one filter that separates application output from framework noise, and it
is the filter an operator reaches for first.

The trace index is what keeps "gather the entries of one request" from scanning
the project. Without it that filter is precisely the kind of unbounded read
[ADR 0026](./adr/0026-a-read-has-five-seconds.md) now cuts off at five seconds,
which would turn a promised filter into an error. Its cost has **not** been
measured.

### The trace is stored as bytes, not as hex

A W3C trace id is sixteen bytes and a span id is eight; CLEF delivers both as hex
text, thirty-two and sixteen characters. logaffe decodes them and stores the
bytes, which halves both columns and every key in the trace index, and costs a
hex decode on a path that is already parsing JSON.

It also makes the column self-validating: **promotion requires a well-formed
value**. A sender delivering something that is not a trace id keeps it as an
ordinary property, where it is stored and displayed like any other, rather than
having it silently accepted into a column that promises a shape it does not have.

## The message in two forms

`rendered_message` is what the operator reads and what a search matches;
`message_template` is what the sender delivered, kept for fidelity, never shown
and never searched
([ADR 0005](./adr/0005-the-rendered-message-is-stored-not-recomputed.md)).

The search index sits on the rendered form alone:

```sql
create extension btree_gin;
create index on log_entry using gin (project_id, rendered_message gin_trgm_ops);
```

Leading with `project_id` is what `btree_gin` is there for. Without it a search
inside one project would walk the trigrams of every project in the installation,
and the separation the product promises would hold everywhere except in the index
that does the most work.

At 2.07 GiB per ten million entries this is the largest object in the database,
as [ADR 0010](./adr/0010-search-is-a-substring-match-not-a-full-text-query.md)
said it would be. It is also why a search text is at least three characters:
below that the index cannot be used at all
([ADR 0025](./adr/0025-a-search-text-is-at-least-three-characters.md)).

## The level threshold rides a partial index

```sql
create index on log_entry (project_id, event_time desc, id desc) where level >= 3;
```

"Warning and above" is the question people actually ask, and a partial index over
exactly that predicate answers it while indexing only the entries that can match
— 117 MiB per ten million entries, two per cent of the heap. A full index on
`(project_id, level, event_time desc)` would cover every threshold, cost roughly
ten times as much, and serve a range scan over levels far less directly.

The other thresholds are not indexed. Asking for `Error and above` uses this
index and filters; asking for `Debug and above` is nearly every entry and is
better served by the plain paging index.

## Properties are JSONB and carry no index

Properties arrive as a JSON object and are stored as one — scalars or a single
level of nesting, within the sixty-four-property cap of [Ingestion](./ingestion.md).

**Nothing indexes them**, and that follows from a decision already made: ADR 0010
rejected an equality filter over properties, because a value a placeholder
collected is already in the rendered message and findable there, while a value no
placeholder collected is stored and displayed but not searchable. An index here
would serve a filter the product does not offer.

## What it costs

Ten million entries across twenty projects, with every index above except the
trace index, measured:

| | |
| --- | --- |
| Heap | 5.60 GiB |
| Trigram index | 2.07 GiB |
| Logger name index | 1.39 GiB |
| Instance index | 917 MiB |
| Paging index | 762 MiB |
| Receipt index | 760 MiB |
| Primary key | 294 MiB |
| Level threshold index | 117 MiB |
| **Total** | **11.84 GiB**, about 1.2 KiB per entry |

**The indexes together are larger than the table.** That is the shape of a store
whose entire purpose is being searched from several directions, and it is
affordable only because volume is moderate and retention is capped at ninety days
([ADR 0020](./adr/0020-retention-has-a-maximum.md)).

For sizing an installation, 1.2 KiB per entry is the number to multiply — and it
is the number to give the operator, because disk is the limit this design meets
first.

Ingestion sustained 11 051 entries per second on the same host with every index
in place, which is far above anything this product's traffic implies.

## Autovacuum is configured, not left alone

ADR 0023 requires it: a table where a predictable fraction expires every day is
the wrong shape for a default that waits until a fifth of it is dead.

```sql
alter table log_entry set (
    autovacuum_vacuum_scale_factor        = 0.01,
    autovacuum_vacuum_threshold           = 20000,
    autovacuum_vacuum_cost_limit          = 2000,
    autovacuum_analyze_scale_factor       = 0.02,
    autovacuum_vacuum_insert_scale_factor = 0.02
);
```

The space a sweep frees is **reused by incoming entries and not returned to the
operating system** — measured at 11.84 GiB before and after a sweep and a
`VACUUM`, exactly as ADR 0023 predicted. In steady state that is correct: the
table settles at roughly what the retention window implies and stays there. Only
a one-off shrink — a project deleted, or a retention window lowered — leaves a
file that ordinary traffic will not refill, and `docs/operations.md` names that as
an occasional operator act rather than something the product attempts.

## What is deliberately not here

- **No partitioning.** Settled in ADR 0023: per-project retention means a
  partition has no single moment at which it is expired.
- **No dictionary tables.** Settled in ADR 0027: the repeated text stays in the
  row, and the ingestion path stays stateless.
- **No index on properties.** Covered above, and settled in ADR 0010.
- **No index for every filter combination.** Filters combine only with `AND`
  ([ADR 0011](./adr/0011-filters-only-narrow-and-only-with-and.md)), so a query
  takes the most selective index available and filters the rest. Indexing the
  combinations would multiply the largest table's index set for queries that are
  already fast.
- **No update path.** An entry is written once and never edited; there is no
  correction, no reprocessing, and no backfill.
- **No second copy for the agent.** MCP reads the same table through the same
  query surface as the web UI ([Querying](./querying.md)).

# Storage

One table dominates this product and everything else in the database is small.
[ADR 0003](./adr/0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)
draws the boundary: EF Core declares every table and owns the migrations that
apply themselves on startup, and it also *serves* everything except the log
entries, which are written with Npgsql's binary `COPY` and read with hand-written
SQL through Dapper.

This document is the log entry table — what each column is for, which indexes
exist and why, and what the whole thing costs — and, at the end, the two small
tables that hold samples ([Metrics](./metrics.md)) and the smaller one that holds
the tally, which are here because their shape was decided rather than because
their size demands it. The operator account, the projects, the tokens and the
settings are ordinary relational rows and need no document of their own.

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

## The exception carries no index

`exception` is stored as delivered and is **not indexed**, though it has a filter
of its own ([ADR 0028](./adr/0028-the-exception-is-its-own-filter.md)). A stack
trace is kilobytes where a rendered message is a line, so a trigram index over it
would be the largest object in the database by a wide margin — estimated at
around a gigabyte per ten million entries, and never measured — and every
ordinary search would pay for it on every write.

The filter instead rechecks whatever the other filters have already narrowed. An
operator hunting a stack trace has set `Error and above` and a time window, which
the partial index above serves, and exceptions live overwhelmingly on exactly
those entries. Unnarrowed, the filter is a scan, and the five seconds of ADR 0026
are what make that a safe thing to leave unindexed.

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
whose entire purpose is being searched from several directions, and what makes it
affordable is that volume is moderate and that a window is chosen against what it
costs
([ADR 0048](./adr/0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)).
It used to be the ninety-day ceiling of
[ADR 0020](./adr/0020-retention-has-a-maximum.md) that was named here, and the
ceiling is now a year: days were never a bound on what this table costs, because
a week of a noisy project is more of it than a year of a quiet one.

For sizing an installation, 1.2 KiB per entry is the number to multiply — and it
is the number to give the operator, because disk is the limit this design meets
first. **The product multiplies it for them**: the retention field states what
the window in it implies, from the project's own rate over the last fortnight,
beside what the database holds today and what the disk has left
([Projects](./projects.md#the-field-says-what-the-window-will-cost)).

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

## The sample tables

Everything above is the log path, which goes around EF Core because ADR 0003 says
the volume justifies it. **Samples do not, and so they do not.** Twenty hosts
watching three filesystems each deliver eighty rows a minute — 1.3 a second,
against the 11 051 an ingestion sustained — so EF Core declares these tables,
applies their migrations *and* writes them, which is ADR 0003's rule read the way
it was written rather than an exception to it.

```sql
create table host_sample (
    host_id      uuid        not null,
    receipt_time timestamptz not null,

    cpu          real        not null,   -- the share of the interval, 0 to 1
    memory_used  bigint      not null,
    memory_total bigint      not null,
    load_1       real        not null,
    load_5       real        not null,
    load_15      real        not null,

    primary key (host_id, receipt_time)
);

create table filesystem_reading (
    host_id      uuid        not null,
    receipt_time timestamptz not null,
    mount_path   text        not null,

    used         bigint      not null,
    total        bigint      not null,

    primary key (host_id, receipt_time, mount_path)
);
```

**The clock is ours and there is only one**, which is the whole of why these
tables carry a `receipt_time` and no second column beside it
([Metrics](./metrics.md#it-carries-one-clock-and-it-is-the-installations)).

**`real` rather than `double precision`.** A share of an interval and a load
average are reported to two decimal places by the machine itself, so four bytes
against eight over every row of the largest of these tables buys nothing back
except precision nobody has.

### The key is natural, and that is what makes a repeat harmless

`log_entry` carries a `bigint` the installation hands out, for two reasons that
are both absent here: binary `COPY` has to carry the value with the row, and the
cursor of [Querying](./querying.md) needs a unique tie-break. Samples are written
through EF and are never paged with a cursor, so there is nothing for a synthetic
identity to do.

`(host_id, receipt_time)` is unique because a host reports once a minute, so the
rule is **enforced by the primary key rather than trusted of the collector**. A
delivery that arrives twice for the same minute — a collector restarted mid-minute,
a duplicated container — is a conflict the write resolves by keeping what is
there, not a second row that quietly doubles a machine's memory on the band. That
is the property the natural key was chosen for.

### One index, because there is one read

The primary key is the only index on either table, and it serves everything asked
of them:

- **The band and `get_host_samples`** — one host over a time range, which is the
  key's leading column and then a range on the second.
- **When a host last reported** — `max(receipt_time)` for one host, a backwards
  scan of one key, which is what lets [Metrics](./metrics.md#the-host) read that
  fact off the newest sample instead of storing it a second time.

**The retention sweep walks the hosts** rather than deleting by time across all of
them. Leading with `host_id` means a sweep over the whole table would scan it, and
the alternative — an index on `receipt_time` alone — is a whole index maintained on
every write to serve a background job. An installation has a handful of hosts, so
the sweep asks each of them in turn and the loop is cheaper than the index.

### The buckets are computed when they are read

A host's ninety days are 129 600 rows, and its year at the ceiling of ADR 0048 is
525 600. Aggregating either into two hundred buckets is one grouped scan of a
fraction of one key, so there are **no rollup tables and no downsampling on
write** — a second write path and a backfill story bought for a saving that does
not exist at this size.

That grouped statement is the one part of samples that is **hand-written SQL**,
and it sits with the sample store rather than in the folder the log path's
queries are kept in — that folder is the log path's, and what holds those
together is that re-reading them is the standing cost of changing one of the
entry table's indexes. This one is not in that set: there is one index here and
one read over it.

It is written out for the plainer reason that it cannot be expressed otherwise.
`date_bin` with an average and a maximum per bucket is arithmetic on an instant,
and no LINQ provider translates it — composed through EF the query does not
compile to SQL at all. The maximum rides beside the average because
[MCP](./mcp.md) hands both to the agent, an average being exactly what hides the
spike worth finding.

### What the samples cost

**Not measured — this is arithmetic from the row widths**, unlike the table above,
and it is written down because the conclusion is that the question does not need
measuring.

Five hosts watching three filesystems each, at ninety days:

| | |
| --- | --- |
| `host_sample`, heap and key | about 78 MiB |
| `filesystem_reading`, heap and key | about 175 MiB |
| **Total** | **about 250 MiB** |

Against 11.84 GiB of entries that is two per cent, and it is the reason samples
get a retention window without an argument about what it costs. Twenty hosts
would be four times that and still under a twentieth of the log store.

Per row that is **126 bytes for a sample and 94 for a filesystem reading**, key
included, and those are the two figures the sample window's footprint multiplies
([Metrics](./metrics.md#retention)): a machine writes one of the first a minute
and one of the second per mount it was told to watch, so what a window costs is
the shape of what is reporting rather than a rate anything has to measure.

**Autovacuum is left at its defaults here**, unlike `log_entry`. The tuning above
exists because a fifth of a six-gigabyte table is a great deal of dead space to
wait for; a fifth of a table this size is not, and a vacuum over it is cheap
enough that the default cadence never becomes the problem ADR 0023 describes.

## The tally table

The third thing this database holds, and the smallest. It is what each project
received in each hour, counted as the deliveries arrived rather than by asking
`log_entry` afterwards
([ADR 0047](./adr/0047-the-volume-history-is-tallied-as-it-arrives.md)) — because
a count over the largest table in the database is what
[The web UI](./ui.md) refuses a home screen for, and a history is the shape a
count is worst at.

```sql
create table project_tally (
    project_id        uuid        not null,
    hour              timestamptz not null,   -- whole, at UTC, on the receipt clock

    entries           bigint      not null,
    at_error_or_above bigint      not null,

    primary key (project_id, hour)
);
```

**Two numbers, and the set is closed.** Not per logger name, per instance, per
level or per host: each of those is the labelled series
[ADR 0044](./adr/0044-a-sample-has-a-closed-schema.md) refused for samples,
arriving by the back door on the table with the highest cardinality in the
product. A third is a change to ADR 0047 and a migration.

**The key is natural, for the sample table's reason** — nothing here is written
with binary `COPY` and nothing here is paged with a cursor, so a synthetic
identity would have no work to do. What it buys is that one project's hour is one
row by the database's doing rather than by the flush being careful.

**There is no foreign key to the project, for the entry table's reason.** A
project is deleted at once and what counted it follows in the background
([ADR 0019](./adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)),
and the rows it leaves are unreachable on their way out — nothing reads this
table except by naming a project.

**It is written once a minute and never per entry.** The counter lives in memory,
which the single writer of [the identity above](#the-identity-is-a-number-the-installation-hands-out)
already makes sufficient, and the flush is one transaction reading the hours it
is about to add to and writing them back. A restart loses up to a minute and
nothing reconciles it: this is not the record of what arrived, `log_entry` is.

**It outlives the entries it counted.** Rows are kept for 400 days whatever a
project's retention window is — a year and a month, which covers a window at the
ceiling of ADR 0048 with slack — because a project keeping entries for a week
still needs a fortnight of history to have a baseline, and that is the project
most likely to be busy. The sweep is one statement on the retention pass, not a
walk and not a portion, because the whole table expires on one clock.

**What it costs**, arithmetic rather than measured, as with the samples: a row is
a uuid, a timestamp and two bigints, so twenty projects for 400 days is about
192 000 rows and on the order of **ten mebibytes** with its key. Against the
11.84 GiB above that is under a thousandth, which is why it gets a period of its
own without an argument about what it costs.

## What is deliberately not here

- **No partitioning.** Settled in ADR 0023: per-project retention means a
  partition has no single moment at which it is expired.
- **No synthetic identity on a sample, and no cursor over samples.** A host and a
  range is the whole of what can be asked ([Metrics](./metrics.md)), so there is
  nothing for a page to resume from.
- **No rollups, and no index on a sample beyond its key.** Covered above.
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
- **No index on the tally beyond its key, and no query surface over it.** One
  project over a range of hours is the whole of what is asked, which is the key's
  leading column and then a range on the second — plus the oldest hour one
  project has, which is the same key walked to the other end. Nothing takes a
  filter here and no count from it reaches anybody: what an operator sees is the
  fortnight turned into a footprint in bytes (ADR 0048), and no agent sees even
  that.

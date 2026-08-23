# The Volume History Is Tallied as It Arrives, Not Counted When It Is Asked

The installation keeps **one row per project per hour** holding how many entries
arrived in it and how many of those were `Error` or worse, written from a
counter in memory that is flushed once a minute. Counting `log_entry` when the
number is wanted was the alternative, and it is the one this product has already
refused twice: [The web UI](../ui.md) declines a home screen with numbers on it
because a count runs over the largest table in the database, and
[Querying](../querying.md) keeps the count a thing asked for deliberately and
never something that accompanies a page —
[ADR 0026](./0026-a-read-has-five-seconds.md) names it as the one operation that
cannot stop early.

A history is the shape a count is worst at. It is not one number wanted once; it
is the same number wanted every hour for as long as the installation runs, over
a range that grows, and it is wanted by something nobody is waiting for. Paying
a scan of the entry table for it, forever, to learn a fact that was free at the
moment the rows were written, is the wrong way round.

**The counter can live in memory because the installation is a single writer.**
That is not a new assumption: [Storage](../storage.md) already hands out entry
identities from a counter in memory, seeded at startup, for the same reason —
one container, one ingestion endpoint. A tally kept beside it costs the hot path
nothing, and the flush is one small upsert a minute rather than a write per
batch or per entry ([Ingestion](../ingestion.md) is the adoption barrier
`VISION.md` judges this product by, and nothing here is allowed to touch it).

## Consequences

**A crash loses up to a minute of the tally, and nothing reconciles it.** The
tally is not the record of what arrived — `log_entry` is, and it is written
before the counter moves. Nothing counts the tally against the entries, no job
repairs it, and a gap in it is a gap. This is affordable because of what the
tally is for: a baseline over fourteen days and a footprint in gibibytes,
neither of which changes if one hour is short by a few hundred. A tally that had
to be exact would be a second write on the ingestion path, which is the thing
this decision exists to avoid.

**It is two numbers, and it is closed.** Entries and entries at `Error` or
above, per project, per hour. Not per logger name, not per instance, not per
level, not per host — each of those is the labelled series
[ADR 0044](./0044-a-sample-has-a-closed-schema.md) rejected for samples, arriving by
the back door on the one table whose cardinality is highest. The second number
was put in for the fourth alert condition of
[ADR 0050](./0050-the-alert-conditions-are-a-closed-set.md), which that decision was
deferring at the time and has since taken, and because it costs a single
comparison at flush time; adding a third is a change to this document and a
migration, deliberately. **That the condition arrived needing nothing new here is
what this decision was for.**

**It outlives the entries it counts.** Tally rows are kept for **400 days**
regardless of any project's retention window, so a project keeping entries for a
week still has fourteen days of baseline behind it and a project at the ceiling
of
[ADR 0048](./0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)
still has history covering the whole of what it holds. A baseline that expired
with the entries would leave every short-window project permanently unable to
say what normal looks like — which is exactly the project most likely to be a
busy one. At roughly fifty bytes a row, twenty projects for 400 days is about
ten mebibytes, against the 11.84 GiB of ten million entries.

**The retention sweep gains a second thing to do and keeps its shape.** The
tally is swept on the same pass, by its own clock and its own period, exactly as
samples already are — one background job, three things ageing out, no new timer.

**It is not a metric and it is not the count.** Nothing queries it, no filter
narrows it, and it is not offered as a surface. What reads it is the footprint
arithmetic of ADR 0048 and the condition evaluation of ADR 0050, both inside the
installation. What the operator and the agent ask for a number remains the count
of [Querying](../querying.md), unchanged, over the entries themselves — because
that one takes filters and this holds none.

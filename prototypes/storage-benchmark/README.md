# PROTOTYPE — storage benchmark (throwaway, wipe me)

**This is not production code.** It exists to answer one design question and is
meant to be deleted once the answer is folded into `docs/` and the ADRs.

## The question

`VISION.md` and the ADRs already commit to three things that all land on the same
table, and none of them has met a query planner yet:

- [ADR 0003](../../docs/adr/0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md) —
  the write path is Npgsql binary `COPY`.
- [ADR 0010](../../docs/adr/0010-search-is-a-substring-match-not-a-full-text-query.md) —
  substring search is served by a trigram index over the rendered message.
- [ADR 0023](../../docs/adr/0023-retention-deletes-rows-rather-than-dropping-partitions.md) —
  retention deletes rows, and names the GIN index under insert-and-delete churn
  as "the part of this design most likely to need attention in production".

So: **does the candidate schema hold up at the volume this product actually
targets, on the kind of host it is actually run on?** Concretely —

1. What does `COPY` cost per entry, with and without the trigram index present?
2. How large is the trigram index relative to the heap? ADR 0010 claims it is the
   second-largest thing stored; is that affordable at 90 days × 30 projects?
3. Do the query shapes in `docs/querying.md` stay interactive — cursor paging,
   level threshold, logger name, substring search, live tail, counts?
4. Does a retention sweep keep up, and does the GIN index degrade under
   continuous insert-and-delete?

## The host it is measured on

The container is capped at **2 vCPU and 4 GB** on purpose. `VISION.md` expects a
single operator on a public cloud host, so numbers taken on an unconstrained
developer machine would be flattering and useless.

## Running it

```sh
cd prototypes/storage-benchmark
docker compose up -d           # Postgres on localhost:55432, volume PROTOTYPE_wipe_me
./bench.sh smoke               # ~1M entries, verifies the harness end to end
./bench.sh target              # ~10M entries, the size the design must be comfortable at
./bench.sh stress              # ~25M entries, watch the disk
```

Each stage resets the schema, loads, measures, and appends a Markdown section to
`results/`. Individual commands:

```sh
dotnet run --project Bench -- reset   --indexes full
dotnet run --project Bench -- load    --entries 10000000 --projects 20 --days 30
dotnet run --project Bench -- sizes
dotnet run --project Bench -- query   --iterations 30
dotnet run --project Bench -- retention --keep-days 20 --batch 20000
dotnet run --project Bench -- churn   --minutes 20
```

## Index sets

`--indexes` selects what gets created, so the cost of each can be attributed:

| set | contents |
| --- | --- |
| `none` | primary key only — the `COPY` ceiling |
| `paging` | + event-time paging index, + receipt-time tail/retention index |
| `full` | + `btree_gin` composite trigram index, + logger/instance |
| `full-plain-gin` | as `full`, but the trigram index does not lead with the project |
| `full-fastupdate-off` | as `full`, with GIN `fastupdate=off` |

## Wiping it

```sh
docker compose down -v         # takes the PROTOTYPE_wipe_me volume with it
```

## What it settled

This branch is a throwaway. It lives outside `main` because the code measures a
schema that does not exist yet and will not survive contact with the real one.
Raw runs are in `results/`; two stages were run, at 1M and 10M entries.

**It answered one question and it changed a decision.** A search text shorter
than three characters contains no complete trigram, so the index cannot serve it
and the query scans the project: 1.7 seconds at 1M entries, 75 seconds at 10M.
ADR 0010 had allowed short searches on the grounds that a minimum length is a
rule the operator has to learn while busy;
[ADR 0025](../../docs/adr/0025-a-search-text-is-at-least-three-characters.md) on
`main` reverses that and carries the numbers.

**Three things it confirmed.** Binary `COPY` sustains 11 000 entries/s at 10M
rows with every index in place, which is far above what this product needs
(ADR 0003 stands). A retention sweep returns no space to the operating system —
11.84 GiB before and after `VACUUM` — exactly as ADR 0023 predicted, and the
sweep's per-row cost fell from 92 000 to 12 856 rows/s between the two stages.
Cursor paging and the live tail stay under 1.3 ms regardless of depth.

**One thing it found that nobody had asked about.** The indexes are larger than
the heap, and `logger_name` plus `instance` cost 2.29 GiB at 10M entries — more
than the trigram index — for two handfuls of endlessly repeated strings. Interning
them, and the message template with them, is the largest unclaimed saving in the
data model. It was not measured.

**Not measured, and worth knowing:** a three-character search (index-served but
by a single trigram), the `COPY` cost attributable to the GIN alone, steady-state
churn with ingest and sweep running together, and anything above 10M entries.

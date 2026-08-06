# The Exception Is Its Own Filter

The exception is matched by a filter of its own, with the same case-insensitive
substring semantics as the search text, and it carries no index. The search text
continues to match the rendered message and nothing else.

The gap it closes was a real one. `docs/ingestion.md` already said `@x` is the
field an operator most often wants "shown, collapsed, or searched on its own",
and the query surface offered no way to search it. Worse,
[ADR 0010](./0010-search-is-a-substring-match-not-a-full-text-query.md) justifies
substring matching with the example of `nullreference` finding
`NullReferenceException` — and in a normal .NET application that text lives in
`@x`, not in the rendered message, because `_logger.LogError(ex, "Order {OrderId}
failed")` renders a sentence that names no exception type. The product's own
motivating search could not be performed.

**Folding the exception into the search text was the obvious alternative and it
is the expensive one.** The exception is where the bytes are: multi-kilobyte
stack traces against hundred-byte messages. One trigram index covering both would
become the largest object in the database by a wide margin, and every ordinary
search — the common case, over the message — would pay for it on every write.
Indexing the exceptions alone at ten million entries is estimated at around a
gigabyte, which is an estimate and not a measurement.

**It carries no index because it does not need one.** An operator hunting a stack
trace has already narrowed: `Error and above`, in a time window. That is served
by the partial index of [Storage](../storage.md) at 117 MiB per ten million
entries, and exceptions live overwhelmingly on exactly those entries. The filter
then rechecks a candidate set the other filters have already made small.

## Consequences

**This is the one filter that can be slow, and that is the point.** Making it a
separate control means it is a deliberate second act rather than a cost folded
into every search. Run without narrowing, over a whole project, it is a scan —
and it is bounded by the five seconds of
[ADR 0026](./0026-a-read-has-five-seconds.md) like every other read, which is
what makes leaving it unindexed a safe choice rather than an open one.

**The three-character minimum of
[ADR 0025](./0025-a-search-text-is-at-least-three-characters.md) applies to it
too.** The reason differs — there is no trigram index here for a shorter input to
miss — but a two-character substring narrows nothing worth returning, and the
surface is better with one rule for typed text than with two.

**The agent gets it as well**, because the operator and the agent share one query
surface and this is a filter like any other ([Querying](../querying.md)).

If it proves slow in practice, the thing to do is measure a partial trigram index
on `exception where exception is not null` before adding one. The estimate above
is the kind of number this product has already been wrong about by a factor of
four.

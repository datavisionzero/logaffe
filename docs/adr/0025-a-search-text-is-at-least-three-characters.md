# A Search Text Is at Least Three Characters

A search text shorter than three characters is refused rather than run.
[ADR 0010](./0010-search-is-a-substring-match-not-a-full-text-query.md) decided
the opposite, calling a minimum length "a rule the operator has to learn at the
moment they are busy", and a measurement overturned it. A two-character pattern
contains no complete trigram, so the index cannot serve it at all and the query
degenerates into a sequential scan of the project: **1.7 seconds** over a million
entries and **75 seconds** over ten million, on the two-core host `VISION.md`
targets. That is not a slow search but an unbounded one, and on a read surface
reachable from the open internet it is a way to occupy the installation with a
single request.

Three is the number the index itself dictates rather than a margin chosen for
safety. `pg_trgm` matches in three-character pieces, so three is the shortest
input it can serve, and the rule states its own reason: the search works in
threes because the index does.

## Consequences

The operator who wants a two-character fragment has to type a third, and the
product refuses an input it previously accepted — the cost ADR 0010 declined to
pay, now paid knowingly because the price of not paying it was measured.

**A three-character search is served by the index, but by a single trigram.** A
common fragment therefore still returns many candidates for the heap to recheck.
That case was not measured, and it is the first place to look if searching ever
feels slow; raising the minimum to four would halve the candidates by requiring
two trigrams, and is a one-word change.

The refusal belongs to the query surface rather than to the web UI, so the agent
meets the same rule over MCP and learns it from the error rather than by waiting.

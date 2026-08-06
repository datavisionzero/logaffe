# Querying

Reading the logs is what the ingestion path was for, and it serves two consumers
that `VISION.md` puts on equal footing: the operator in the web UI, and the agent
over MCP. **They share one query surface.** The agent is not given a thinner
version of the operator's view and the operator is not given a thinner version of
the agent's, because two surfaces over the same data drift apart and the
difference is discovered by whoever is debugging at the time.

A query always runs **inside one project**. Projects are separate in storage, in
the UI and in agent access, and nothing in the product reads across them.

## Filters

A query is a set of filters. There is no query language
([ADR 0011](./adr/0011-filters-only-narrow-and-only-with-and.md)).

- **Time range**, matched against **event time** — the operator asking what
  happened between 10:00 and 10:05 means when it happened, not when it arrived.
- **Level**, as a **threshold** rather than a selection: `Warning and above` is
  the question people actually ask, and it is one control instead of six.
- **Instance**, the sender copy the entry came from.
- **Logger name**, the promoted `SourceContext`, which is the filter that cuts
  framework noise from application output.
- **Trace**, gathering the entries of one request.
- **Search text**, matched against the rendered message.

### They only narrow, and only with AND

Every filter removes entries and none adds any. Filters set together all apply,
and there is no `OR`, no negation and no grouping. Two questions that need an
`OR` between them are two queries, and on a bounded store that is a cheap answer.
The value bought is that every query has an obvious meaning, an agent cannot
formulate one that parses but asks nonsense, and the surface does not grow a
grammar.

## Search is a substring match

**Searching the logs is grep, not a search engine.** The search text is found
wherever it occurs in the rendered message, including inside a word, matched
case-insensitively ([ADR 0010](./adr/0010-search-is-a-substring-match-not-a-full-text-query.md)).

This is what makes the searches an operator actually types work:
`203.0.113.7`, `/api/orders/4711`, `api-7c4f`, and `nullreference` finding
`NullReferenceException`. A word-based full-text index would tokenize the first
three apart and would not find the fourth at all.

**A search text is at least three characters**, and a shorter one is refused
rather than run. A trigram index matches in three-character pieces and cannot
serve anything shorter, so a two-character search scans the whole project — which
was measured at 75 seconds over ten million entries and is a way to occupy the
installation with one request
([ADR 0025](./adr/0025-a-search-text-is-at-least-three-characters.md)). The rule
applies to the query surface, so the operator and the agent meet the same one.

**Property values come along for free where the template used them.** Because the
rendered message is stored (ADR 0005), an entry whose template read
`User {UserId} failed` is found by searching for the user's number — the value is
in the sentence. A property that no placeholder collected, such as one an
enricher attached, is stored and displayed but **not searchable**. That is a
deliberate limit and not an oversight: a second index on the largest table, plus
an answer to whether `42` and `"42"` are the same filter value, buys less than it
costs on a store this size.

## Order and paging

Entries are ordered **newest first by event time**, with the entry's identity
breaking ties, because two entries can carry the same timestamp and a paging
cursor has to be total.

Paging is by **cursor, never by offset**. Entries keep arriving while a person
reads, and an offset would skip and repeat rows as the store grows underneath
them. A page carries the cursor for the next one, and the page size is a product
value rather than a setting.

**A page does not come with a total.** Counting the matches of a substring search
over the whole store is a scan, and paying for it on every page to display a
number nobody asked for is the wrong default. A count is available on request —
see below — as a deliberate act by whoever wants one.

## The live tail

Following the logs live is polling on the order of five seconds, and the poll
asks **what has arrived since the last poll**: the cursor runs on **receipt
time**, while the view keeps its event-time order
([ADR 0009](./adr/0009-the-tail-follows-the-receipt-the-view-keeps-the-order-of-events.md)).

The two clocks do different jobs here for the same reason they do in retention. A
sender that was disconnected delivers entries whose event times are older than
what the tail has already shown; a cursor on event time would never return them,
and the outage being watched would be the one thing the live view omits. Because
the cursor runs on receipt time they arrive, and because the view sorts by event
time they take their place among the entries they belong with — visibly below the
newest line rather than at the top.

## Counting

A count answers a question about a set of entries without returning them, over
the same filters as any other query, optionally grouped by **level**, **logger
name**, **instance**, or a **time bucket**.

This is what `VISION.md`'s example needs: *"check project mysupertestapp and tell
me whether there were critical errors in the last three days"* is answered by a
number and a grouping, not by forty thousand rows. It is the same request for
both consumers — the agent calls it to answer a question, and the operator calls
it when a number is what they want rather than a page.

## The agent's view

The agent gets the filters above, the count, and the entries, **read-only**.

**Every result is capped, and says what it left out.** A cap that silently
truncates would let an agent conclude from thirty returned entries that thirty is
all there were. A capped result therefore carries how many entries matched in
total, so the agent knows it is looking at a sample and can narrow or count
instead.

**Log content reaches the agent as data, never as prose.** Entries arrive as
structured values in named fields, never as rendered markdown, a formatted
transcript, or text folded into an instruction
([ADR 0012](./adr/0012-log-content-reaches-an-agent-as-data-never-as-prose.md)).
`VISION.md` treats log content as a prompt-injection surface and as the normal
case rather than an edge case, and this is the mechanism that claim rests on.

## What is deliberately not here

- **No query language, no `OR`, no negation, no grouping.** Settled above.
- **No cross-project query.** A search names one project, and the separation
  `VISION.md` promises holds on the read path too.
- **No property equality filter.** Covered above: property values are found
  through the rendered message or not at all.
- **No relevance, and no sort but time.** There is no ranking, no score, and no
  ordering by level or logger. A log is read in the order it happened.
- **No saved searches, no dashboards, no pinned queries.** A filter set is typed
  when it is needed. Saving them is a surface that grows names, management and
  staleness for a single operator.
- **No total alongside every page.** Counting is its own request.
- **No alerting on a query.** Settled in `VISION.md`: nothing watches, and
  nothing fires.

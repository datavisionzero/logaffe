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
- **Exception text**, matched against the exception, on its own
  ([ADR 0028](./adr/0028-the-exception-is-its-own-filter.md)).

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
`203.0.113.7`, `/api/orders/4711`, `api-7c4f`, and — in the exception filter
below — `nullreference` finding `NullReferenceException`. A word-based full-text
index would tokenize the first three apart and would not find the fourth at all.

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

## The exception is searched on its own

The search text matches the rendered message and nothing else. The **exception**
has a filter of its own, matched the same way — case-insensitive substring,
anywhere in the text, three characters minimum
([ADR 0028](./adr/0028-the-exception-is-its-own-filter.md)).

They are separate because the exception is where the bytes are. A stack trace is
kilobytes where a message is a line, and one index over both would make every
ordinary search pay for the rare one. So the exception is unindexed and is
narrowed by the filters an operator hunting a stack trace has already set —
`Error and above`, in a time window — which is what makes it cheap in the case it
is actually used in.

It is therefore **the one filter that can be slow**, deliberately: a second act
rather than a tax on every search. Run across a whole project with nothing else
set, it meets the five-second limit like any other read.

This is also where `nullreference` finds `NullReferenceException`. In a normal
.NET application the exception type is in the exception and not in the sentence,
so that search — the one ADR 0010 uses to justify substring matching in the first
place — is typed into this filter rather than the search box.

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

A tail is **a filter set that is being watched**, and not a mode with rules of its
own: the same narrowings, the same minimum on a search text, the same five
seconds. The event-time range still applies — a view showing the last quarter of
an hour does not begin showing this morning's entries because they were delivered
late — but it is not what decides what is new.

**Every poll answers a cursor, including the ones that answer nothing.** A quiet
poll hands back the position it was given, so following the logs is a loop over
the last answer rather than a state the reader has to keep. **The first poll arms
the tail**: it has no cursor, it answers no entries, and what it carries back is
where the project's arrival order currently ends — which is the position to watch
from, read out of the store rather than off a clock, so that there is no instant
in between for an entry to be lost in. Answering it the newest entries instead
would hand back what the page it is sitting on already shows.

**One poll carries at most a page**, and for the reason a page is bounded rather
than for one of its own. A poll after a quiet minute returns a handful and a poll
after a burst could otherwise return everything a project holds. What is taken is
the front of the arrival order, so the middle is never lost — it is waiting where
the cursor stopped — and a poll that filled **says so**, which is how an interval
that cannot keep up becomes something the reader is told rather than something it
discovers later.

## Counting

A count answers a question about a set of entries without returning them, over
the same filters as any other query, optionally grouped by **level**, **logger
name**, **instance**, or a **time bucket**.

This is what `VISION.md`'s example needs: *"check project mysupertestapp and tell
me whether there were critical errors in the last three days"* is answered by a
number and a grouping, not by forty thousand rows. It is the same request for
both consumers — the agent calls it to answer a question, and the operator calls
it when a number is what they want rather than a page.

## A read has five seconds

Every query on this surface is cut off after five seconds, in every installation
and with no setting that raises it
([ADR 0026](./adr/0026-a-read-has-five-seconds.md)). The number comes from the
live tail: a read that takes longer than the interval which refreshes the view
has already stopped being an interface.

Nothing measured at ten million entries came near it — the slowest ordinary query
was a third of a second — so the limit is there for the queries nobody
anticipated rather than for the ones above. The **count** is the likeliest to
meet it, because it is the one operation that cannot stop early, and the
narrowing that helps it is the time range. A read that expires says what to
narrow rather than reporting a database error.

The limit binds this surface. The retention sweep and the rest of an
installation's background work are not reads and are not held to it.

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
- **No alerting on a query.** No filter set is watched, nothing fires from one,
  and there is nothing to attach a threshold to — which is not softened by the
  installation having gained three conditions of its own
  ([Alerts](./alerts.md)). "Notify me when this filter matches more than N in an
  hour" is precisely the alternative
  [ADR 0050](./adr/0050-the-alert-conditions-are-a-closed-set.md) rejected, and
  this refusal is what that decision rests on: the three conditions can derive
  their own thresholds because they are named in the product, and a rule over a
  query could only ever take a number the operator guessed.

# The Web UI

The web UI is the operator's whole entry into the product: a single-page
application ([ADR 0001](./adr/0001-the-frontend-is-react-not-blazor.md)) reading
through the query surface of [Querying](./querying.md) — the same one the agent
gets, rather than a richer one of its own. This document is what the operator
sees: which screens exist, what an entry looks like on a row, how filters are
set, how the live tail behaves, and what happens when a read is too slow or a
project is empty.

Two things are settled elsewhere and shape every screen below. There is **one
operator and no user model**, so the interface carries none of the furniture that
exists to tell people apart — no avatars, no sharing, no "assigned to", no
notification bell. And **nothing happens unasked**: the only request this
interface repeats on its own is the poll of the view the operator is currently
looking at.

## Three surfaces

- **The project list**, where a session starts.
- **The log view**, one per project, where nearly all the time is spent.
- **Settings**, holding what is changed rarely.

There is no home screen with numbers on it. A dashboard would be a set of counts
nobody asked for, run on every sign-in, over the largest table in the database —
and [Querying](./querying.md) already refuses to put a total beside a page for
the same reason.

The project list is therefore a list of projects, one click from the thing the
operator came for. Each row carries **when that project last received an
entry**, which is the one fact an operator wants at a glance and which the
receipt-time index answers with a single lookup per project
([Storage](./storage.md)). It is also the answer to the question that actually
gets asked at that moment — whether an application is still delivering — and it
costs nothing like a count would.

A row also carries **how many ingest tokens the project holds** — one
ordinarily, two while it is being rotated, and none for a project whose door is
closed. That last case is why the number is on the list rather than only in the
project's settings: a project nothing can deliver to should be visible without
opening each one in turn.

A project switcher is present everywhere. Moving from one project to another is
the frequent act, and it should never be a trip back to a start page.

## The log view

One screen: the filters across the top, the entries below them, the detail of one
entry beside them. There is no separate search page, no "advanced" mode, and
nothing that opens in a new place — every narrowing happens in front of the list
it narrows.

**Switching project keeps the time range and the level threshold, and drops
everything else.** Those two are questions about the world — *the last fifteen
minutes*, *warnings and worse* — and carrying them over is what makes "the same
five minutes in the other service" one click. An instance, a logger name, a trace
or a search text belongs to the project it was found in, and carrying it into
another one would produce an empty list that looks like an outage.

### The filter bar

The controls sit in the order an operator reaches for them: **time range**,
**level threshold**, **search text**, then the narrowings taken from entries —
instance, logger name, trace — shown as chips that can be removed one at a time,
and finally the **exception filter** in a box of its own.

The exception filter is visibly separate rather than a mode of the search box,
because the two match different fields and the operator has to know which one
finds `nullreference`
([ADR 0028](./adr/0028-the-exception-is-its-own-filter.md)). The box says as much
where it stands, and it is where the product admits that this is the one filter
that can be slow.

The **time range** offers the ordinary spans — the last fifteen minutes, hour,
day, week — and an absolute from-and-to. A span is open-ended and keeps growing,
which is the live case; an absolute range with an end in the past is history and
cannot grow, which is what turns the tail off below.

The **level** is one control with six positions rather than six checkboxes, and
it opens at `Verbose and above` — everything. A view that hides `Information` by
default is a view that shows nothing happened when something did, and an operator
who has not yet set a filter should be looking at what actually arrived.

### The filter set is the URL

Every filter is in the address bar. A reload comes back to the same view, the
back button walks the narrowings just made, and a bookmark is a filter set that
costs nothing to keep. This is what stands in for the saved searches
[Querying](./querying.md) refuses: the browser already has a place for named
queries, it is better at it than a settings screen would be, and it grows no
management surface inside the product.

### Filter values come from the entries

The instance, the logger name and the trace are set by clicking the value on an
entry that is already on the screen, or by typing one. There is no dropdown
listing every logger a project has seen
([ADR 0029](./adr/0029-filter-values-come-from-the-entries-not-from-a-list.md)).

Narrowing from an entry is the gesture the whole view is built around: the
operator is looking at a line, and every field on it that is a filter is one
click away from being one. Because filters only narrow and only with `AND`
([ADR 0011](./adr/0011-filters-only-narrow-and-only-with-and.md)), clicking three
of them in a row has an obvious meaning and needs no explaining.

### The entry line

One entry is one row, one line, never wrapping. It carries the **event time** to
the millisecond, the **level** as a word with a colour behind it and never as a
colour alone, the **logger name** shortened to its last segments, the **rendered
message** cut off where the row ends, and a mark for an entry that carries an
**exception** or that was **truncated** on the way in
([ADR 0008](./adr/0008-an-over-long-message-is-truncated-not-refused.md)).

Reading a log is scanning, and a list whose rows change height cannot be scanned:
one four-line stack trace in the middle of the page destroys the rhythm that
makes the other forty rows readable. The message that does not fit is one
keystroke away in the detail.

### The entry detail

The detail opens beside the list without navigating anywhere; the list keeps its
position and the filters stay set. It carries both timestamps, each named — the
**event time** as the sender's clock, the **receipt time** as ours
([ADR 0007](./adr/0007-the-sender-orders-the-receipt-expires.md)) — the level,
the logger name, the instance, the trace and span, the full rendered message, the
properties as they were delivered, and the exception in full and monospaced.

**The message template is not shown.** It is stored for fidelity and never
displayed ([ADR 0005](./adr/0005-the-rendered-message-is-stored-not-recomputed.md)):
the operator reads the sentence, not the shape it was made from.

**Truncation is stated in words** where the text ends — that this message or this
exception was cut at its cap — because an operator hunting the bottom of a stack
trace has to know that the bottom is not there rather than conclude the exception
ended where the text does.

Two actions live here. Every field that is a filter narrows to its value, of
which **the trace is the valuable one**: it turns one line into the sequence of
entries the request it belonged to produced. And the entry **copies as JSON**, in
one action, for the operator who is pasting it into a chat or an issue.

### The keyboard

Up and down move through the entries, `Enter` opens the detail and `Escape`
closes it, and `/` puts the cursor in the search box. Scanning a list is a
keyboard task, and reaching for the mouse for every next line is what makes a log
viewer tiring to use.

## Time is the browser's time

Every timestamp is shown in the **time zone of the browser reading it**, stated
once in the view so that no screen is ambiguous about which zone it is in. The
same zone interprets a typed time range, and the detail additionally shows the
offset, so an instant copied out of it stands on its own.

Times are **absolute and to the millisecond**, never relative. "Three minutes
ago" is unreadable at the resolution this product works at: the interesting
distance between two log entries is regularly under a second, and it is the
distance that carries the meaning.

Local rather than UTC is a choice against the server convention, and it is made
because the operator's question is in local time — *what happened at ten this
morning* — while the UTC that a server console shows is one comparison the detail
resolves by naming the offset. There is no toggle between the two: a switch means
every screenshot and every glance carries a question about which mode it was in.

## The live tail

While the time range is open-ended, the view **follows**: it polls on the order
of five seconds and asks what has arrived since it last asked
([ADR 0009](./adr/0009-the-tail-follows-the-receipt-the-view-keeps-the-order-of-events.md)).

The cursor runs on receipt time and the list stays ordered by event time, which
has a visible consequence: **an entry that arrives late takes its place among the
entries it belongs with, below the newest line rather than at the top.** A sender
that was disconnected delivers exactly this way, and it is the case an operator is
most likely to be watching for. Newly arrived rows are therefore marked briefly
wherever they land, so that something appearing out of eyeline is still something
the operator sees appear.

**The tail follows the top of the list.** Scrolling away pauses it, because a
list that moves while it is being read is unusable; what arrived while it was
paused is counted, and returning to the top is one click that shows it. It also
stops when the browser tab is hidden, and it never starts at all when the time
range has an end in the past, since a closed range cannot grow.

## Older entries are loaded on request

The bottom of the list carries an action that loads the next page. It is not
infinite scroll.

Two things make the automatic version wrong here. The tail is inserting entries
at the top of the same list, and a list that grows at both ends while a person
reads is where scroll position stops being trustworthy. And there is no total to
scroll against — [Querying](./querying.md) refuses to count the matches of a
substring search on every page — so a scrollbar would be describing a length
nobody knows. Paging is by cursor, so a page five thousand entries deep costs
what the first one costs ([Storage](./storage.md)); making the operator ask for
it costs a click and buys a list that stays where it was put.

## The count is asked for

A count is a button beside the filters, not a number that accompanies the page.
It answers the current filter set with a number, optionally grouped by level,
logger name, instance or a time bucket, and the result is a small table of value
and number in which **every row narrows to itself** — the closest thing this
product has to a facet, computed once because somebody asked for it rather than
maintained because a screen wanted it.

There is deliberately no histogram sitting above the list. It is a grouped count
on every view open, over the largest table in the database, to draw a shape the
operator did not request.

## When a read takes too long

Every query is cut off after five seconds
([ADR 0026](./adr/0026-a-read-has-five-seconds.md)). The view says so in the
terms of the filters that are set — narrow the time range, take the exception
filter off, count a day instead of the project — and keeps them exactly as they
were, so the next attempt is one adjustment rather than a rebuild. It is never a
database error, and it never appears as a failed request in a corner.

## Empty means two different things

**A project with no entries at all** shows the delivery snippet with its token in
it — the same one the first-run guide hands over ([Setup](./setup.md)). An
operator looking at an empty project is asking whether delivery works, and the
answer is the configuration they are about to check anyway.

The snippet arrives with the token rather than being assembled here: issuing one
returns it and reading one back returns it again, because reading a token back
and being able to use it are one errand. **A project holding no token at all is
shown the act that issues one instead**, since there is nothing to deliver with
yet — that is the same closed door the token count on the project list names.

**A filter set that matches nothing** says that, names the filters responsible,
and offers to clear them.

Showing one where the other belongs is how an operator concludes their
integration is broken while the truth is that the time range is set to yesterday.

## Settings

Per project: the name, the retention window with its warning about what lowering
it removes, the ingest tokens with when each was last used, and deletion
confirmed by typing the project's name ([Projects and tokens](./projects.md)).

Per installation: the sessions with where and when each was last used and the
means to end them ([Signing in](./sign-in.md)), re-enrolment of the second
factor, a fresh set of backup codes, and the **agent tokens** ([MCP](./mcp.md)).
The agent tokens live here rather than inside a project because an agent token
reads every project — putting it under one of them would say something untrue
about what it can do.

## The interface asks for nothing unasked

The tail of the view being watched is the only repeating request the UI makes.
Nothing prefetches another project, nothing counts on load, nothing polls a
hidden tab, and closing the browser ends every request the operator was
responsible for. `VISION.md` puts this as a principle about agents; it is a
property of this interface for the same reason, which is that an installation
running on two cores has better things to do than answer questions nobody asked.

## What is deliberately not here

- **No dashboard, no home screen with numbers, no saved searches or pinned
  queries.** Settled in [Querying](./querying.md); the URL is where a filter set
  is kept.
- **No cross-project view.** A view names one project, exactly as a query does.
- **No grouping of entries by message template.** The template is not shown at
  all (ADR 0005), and collapsing repeated events into patterns is the analysis
  product `VISION.md` says logaffe is not.
- **No column configuration, no layout settings, no theme setting.** The row is
  what it is, and the interface follows the colour scheme the operating system
  asks for.
- **No export of a filtered result to a file.** A single entry copies as JSON,
  and an agent reads through [MCP](./mcp.md); a bulk export is a second read path
  with its own limits and its own answer to what happens at ten million rows.
- **No annotations, stars, comments, or marking an entry as read.** One operator,
  nothing to hand over, and entries that leave by ageing out
  ([Storage](./storage.md)).
- **No editing or deleting a single entry.** An entry is written once and is
  never updated.
- **No alerting, and nothing that watches on the operator's behalf.** Settled in
  `VISION.md`.
- **No mobile application.** The UI is readable on a phone, because an operator
  is not always at a desk, and the working surface is a desktop browser.

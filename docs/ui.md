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

### The list is grouped once there are groups

Projects that sit in a group are listed **under its name**; projects in no group
come first, with no heading over them. An installation that uses no groups
therefore reads exactly as it did before there were any, and the first group an
operator makes is the first heading the list has ever had.

Groups are in the order of their names and there is nothing to drag. A
hand-arranged order is a position stored per group, a way to change it, and an
answer to where a new group lands — bought so that a list of five headings can be
in a different order than alphabetical.

**A group with no projects in it says so** rather than being left out. It is
something the operator made and not a side effect of what the projects say
([Projects and tokens](./projects.md)), and a list that quietly omits it is a
list that answers *where did the group I just created go*.

## The shell navigates on two levels

Every screen above sits under one bar, and it is the only place this product
navigates from. It carries two levels, because that is what there are.

**The first is the installation**, and it is true wherever the operator is: the
wordmark, the **project switcher**, the zone every timestamp is in, the
installation's own settings, and the sign-out.

**The second is the project being read** — its log and its settings — and the row
holding it is present while a project is open and absent otherwise, so that the
second level appearing is itself the statement that the operator is inside one.
Which of the two surfaces is being read is marked in the bar rather than left to
be inferred from the screen.

Nothing navigates from inside a screen. A settings link in the status line of the
log view and a *back to the log* sentence at the top of the settings are
navigation hidden in the content: they are in a different place on every screen,
they say where they go rather than where the operator is, and between them they
leave no screen with a place. The bar says both at once.

**The switcher is present everywhere**, because moving from one project to
another is the frequent act and it should never be a trip back to a start page.
It is a menu rather than a bare control, and it does two jobs: it names the
project being read — which is the only place that name appears while the log is
on the screen — and it opens on the other projects and on the way back to the
list. The menu carries the same headings the list does, for the same reason.

**It names the group beside the project**, because a project's name is unique
only within its group ([Projects and tokens](./projects.md)) and this is the one
place a project is named while its list is nowhere on the screen. Reading `api`
alone above a log that could be either of two would be the three-in-the-morning
trap moved rather than removed.

## The log view

One screen: the filters across the top, the entries below them, the detail of one
entry beside them. There is no separate search page, no "advanced" mode, and
nothing that opens in a new place — every narrowing happens in front of the list
it narrows.

**A band above the entries shows what the machine was doing**, drawn for the host
the open project sits on, over exactly the range the filters already state
([Metrics](./metrics.md)). It moves when the range moves, it is absent for a
project that sits on no host, and it is the only place in the interface a sample
is drawn. It is a band and not a dashboard: nothing on it is configured, picked
or saved, and it is there so that four minutes of errors are read next to the
memory that ran out three minutes before the first one.

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

They are two screens and the bar keeps them apart: a project's sit on the second
level beside its log, and the installation's are on the first, where they are
true of every project at once.

### Each screen is its areas

Both screens are their **areas**, listed beside what is being read and marked
while it is: a project's are *the project*, *ingest tokens* and *delete this
project*; the installation's are *signed-in browsers*, *agent tokens*, *your
credentials*, *groups* and *hosts*. One is on the screen at a time. Stacked, the answer to
*where is the retention window* was to read the page.

**An area is an address**, so a reload comes back to it and the back button walks
the ones just opened — the same thing the log view does with a filter set. The
first area of a screen answers to the screen's own address rather than to one of
its own, so the ordinary way in stays one place.

**Only the area being read asks the installation for anything.** Every one of
them asks for something on the way in — the sessions, the agent tokens, a
project's ingest tokens — and the stacked screen asked for all of it whenever it
was opened, most of it for something nobody had looked at. The rule below is not
only about the log view.

**Deleting a project is an area rather than the end of one**, because an act that
destroys data and cannot be undone should be arrived at rather than scrolled
past. The three credentials stay together on one area, because they are three
acts on one account rather than three subjects.

Per project: the name, the group it is in — one of the installation's, or none —
the retention window with its warning about what lowering it removes, the ingest
tokens with when each was last used, and deletion confirmed by typing the
project's name ([Projects and tokens](./projects.md)). The group is chosen here —
and at the creation, which offers the same choice so that putting a project where
it belongs is part of making it rather than a second trip — but it is **made**
elsewhere: a screen about one project is the wrong place to bring into existence
a thing that outlives it. The creation offers no group by default, and offers
nothing at all while the installation holds none.

Per installation: the sessions with where and when each was last used and the
means to end them ([Signing in](./sign-in.md)), the second factor — enrolled,
re-enrolled or turned off from here, since this is the only place it is ever done
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)) — a fresh
set of backup codes, and the **agent tokens** ([MCP](./mcp.md)). **An
installation with no second factor says so wherever the operator is**, not only
on this screen: a banner that stays until one is enrolled, because the interface
is the only thing that can keep an omission from passing for a setting.
The session list marks the browser being read from, which the server says and
nothing else could — without it "end all others" is a guess. It shows the last
use to the minute rather than to the second, because that is how accurately it is
recorded ([ADR 0033](./adr/0033-the-last-use-of-a-token-is-written-coarsely.md)).
The agent tokens live here rather than inside a project because an agent token
reads every project — putting it under one of them would say something untrue
about what it can do.

**Issuing one asks which kind it is**, reading or administering, with reading
offered: `VISION.md` says agent access is read-only by default, and a default is
a thing a screen shows rather than a thing a document claims. **The may-destroy
flag is offered only for an administering token**, off, and what it means is
written where it is turned on — deleting a project, deleting a host, shortening a
project's retention window, shortening the one for samples, and nothing else.
Four acts named rather than a sentence about permissions, because those four are
the ones after which data does not come back
([ADR 0046](./adr/0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)).

**The list says what each token is** — the kind, and whether it may destroy —
beside the name and the last use. The list is where an operator decides what to
revoke, and a credential whose powers are not visible there is one that is never
revoked for being too strong. The screen also says that **the two do not
combine**: an administering token cannot be given the reading tools and a reading
token cannot be given the settings, so changing what a token may do is issuing
another and revoking this one. That is said where someone would otherwise look
for the switch, because there is no switch and nothing here edits a kind or a
flag. Renaming stays what it always was, a label for the list.

The configuration block carries the token that was issued and **suggests a server
name that differs by kind** — an operator wiring up both puts two servers into
one client, and two entries called `logaffe` is a mistake the product can spare
them ([MCP](./mcp.md)). And one sentence says what the split does not do, where
that operator would read it: putting both in one assistant's context is something
they can do and this installation cannot prevent or notice.

**The groups are here for the same reason**: a group is a fact about the
installation's projects taken together, and no single project's screen can hold
one. The area lists them with how many projects each holds — counted off the
project list the interface already has, because a second answer carrying the
same fact is the one that goes stale — and creates, renames and removes one. **Removing is a plain act** — it says how many projects it will
leave in no group and asks for nothing to be typed, because nothing here is
destroyed and the typed name on a project's deletion is proportionate to entries
that do not come back
([ADR 0039](./adr/0039-a-group-has-an-identity-and-holds-nothing.md)).

**The hosts are here for the same reason, and they hold more.** The area lists
them with when each last reported — read off its newest sample, not written
beside it — and makes one. **A host is then a screen of its own**, an address
inside the area like every other one, because it holds more than a group does:
what the machine was doing, the token its collector reports with, its name and
its end. Issuing that token hands back the **finished command that starts the
collector**, with this installation's address, the token and the mounts it needs
already in it, exactly as an ingest token hands back a delivery snippet and an
agent token hands back a client configuration
([Metrics](./metrics.md#the-collector)). The same command comes back whenever the
token is read.

**Removing a host is confirmed by typing its name**, unlike removing a group: a
group holds nothing, a host holds its samples, and the guard is proportionate to
what does not come back. The act says how many projects it will leave on no host,
and those projects lose nothing but the band over their entries.

That screen draws the host's samples over a plain range, for the times the
question is about the machine rather than about a project — the same band, with
a span to pick and nothing else to arrange. The **retention window for samples**
is on the list rather than on a host, because it is one number for the
installation ([Metrics](./metrics.md#retention)), and it carries the same warning
about what lowering it removes that a project's does.

## The interface asks for nothing unasked

The view being watched is the only thing the UI asks for repeatedly, and it asks
at the rate the thing it is watching changes: the entries every five seconds, and
the band above them **once a minute**, because that is how often a sample is
taken ([Metrics](./metrics.md#the-sample)) and a band redrawn twelve times per
reading would be eleven requests for a picture that did not move. Both stop on a
range with an end in the past, since a closed range cannot grow, and both stop
when the tab is hidden.

Nothing else repeats. Nothing prefetches another project, nothing counts on load,
nothing polls a hidden tab, and closing the browser ends every request the
operator was responsible for. `VISION.md` puts this as a principle about agents;
it is a property of this interface for the same reason, which is that an
installation running on two cores has better things to do than answer questions
nobody asked.

## What is deliberately not here

- **No dashboard, no home screen with numbers, no saved searches or pinned
  queries.** Settled in [Querying](./querying.md); the URL is where a filter set
  is kept.
- **No cross-project view, and a group is not one.** A view names one project,
  exactly as a query does; a group puts two of them under one heading and changes
  nothing about what either can be asked.
- **No nested groups, and no dragging them into an order.** Settled in
  [Projects and tokens](./projects.md); the headings are in the order of their
  names.
- **No grouping of entries by message template.** The template is not shown at
  all (ADR 0005), and collapsing repeated events into patterns is the analysis
  product `VISION.md` says logaffe is not.
- **No column configuration, no layout settings, no theme setting.** The row is
  what it is, and the interface follows the colour scheme the operating system
  asks for.
- **No sidebar in the shell.** There are three surfaces and one of them is a list
  of lines that must not wrap; a permanent column beside it would spend the width
  the log is read in on a menu of three entries. The settings screen lists its own
  areas down its left, which is a column inside one screen and not the frame
  around all of them.
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

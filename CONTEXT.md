# logaffe Domain Language

This glossary defines the canonical product language of logaffe. It describes
domain concepts and distinguishes them from competing ideas without prescribing a
technical implementation.

It grows one area at a time. A term appears here once the decision behind it has
been made; the behaviour itself lives in the area document under `docs/`, and the
reasoning behind a contested one lives in `docs/adr/`.

**The entry heading is the canonical name; running prose writes it in ordinary
case.** `docs/` says "a project", "a log entry", "the operator", and nothing is
lost by that. Capitals are kept only where they resolve an ambiguity the sentence
cannot — **Installation** and **Instance** against each other, **Operator** and
**Agent** as the two consumers against the ordinary words, and a term standing
beside its own definition.

## Language

**Installation**:
One deployed logaffe: a container, its database, and the host volume holding what
is not in the database. It is claimed by exactly one operator, and everything the
product knows lives inside one of them.
_Avoid_: Instance, deployment, tenant, server, node

**Operator**:
The single human account of an installation, which can see and do everything.
There is exactly one, it is established by the claim, and the product has no
concept of a second person.
_Avoid_: User, admin, account, owner, tenant

**Agent**:
An AI acting on the operator's behalf, reaching the installation over MCP as the
second first-class consumer beside the operator. It acts on request and never
watches on its own, and what it can do is what its **Agent Token** is: it reads
entries or it administers the installation, and no token is both.
_Avoid_: Bot, assistant, integration, client, sender

**Claim**:
The act by which an operator takes an unclaimed installation and establishes
their account, which is one password and nothing else. An installation is
unclaimed until it happens, and it happens once.
_Avoid_: Signup, registration, onboarding, first login, setup

**Claim Secret**:
The value that must be presented to claim an installation, either set before its
first start or drawn by the installation and written where the host can read it.
It guards the act of claiming, not the account, and it stops working the moment
the installation is claimed.
_Avoid_: Setup token, install key, invitation, licence, bootstrap password

**Claim Window**:
The limited period after an installation first runs during which a claim may be
made over the network without a **Claim Secret**. It is the other way of guarding
a claim, and once it lapses, claiming is only re-enabled from the host the
installation runs on.
_Avoid_: Grace period, trial, timeout, expiry

**Second Factor**:
The time-based one-time code from an authenticator app that an operator may give
alongside their password. It is optional and no part of the claim: a signed-in
operator enrols it, re-enrols it and can turn it off again.
_Avoid_: 2FA, MFA, OTP, passkey, authenticator

**Session**:
One signed-in browser's standing permission to act as the operator. Several exist
at once, each is listed and separately revocable, and each expires on its own
after a period of disuse.
_Avoid_: Login, token, cookie, device, connection

**Backup Code**:
One of a set of single-use codes shown once when a **Second Factor** is enrolled,
which stands in for it when it is unavailable. A fresh set can be generated at
any time and replaces the previous one entirely, and an operator who has enrolled
no second factor has none.
_Avoid_: Recovery code, one-time password, fallback, emergency key

**Host Recovery**:
The command run inside the running container that returns an installation to
unclaimed and opens the way in again — a fresh **Claim Secret** or a fresh
**Claim Window** — keeping its projects, tokens and entries. It is reachable from
the host and never over the network, and it is the only route back into a claimed
installation.
_Avoid_: Password reset, admin override, rescue mode, break-glass, escape hatch

**Project**:
The unit of separation: every log entry belongs to exactly one, the operator
creates them explicitly, and separation holds in storage, in the UI and in agent
access alike. It owns its retention window and its ingest tokens, and it sits in
at most one **Group**, which changes nothing about what it is.
_Avoid_: Application, service, tenant, stream, bucket, source, workspace

**Group**:
The set of projects an operator keeps together so they are found together — one
product's environments, one customer's applications. It has an identity that
survives its rename and carries nothing besides a name: no retention window, no
token, and nothing that can be asked of it, because a query still names one
project. A project sits in at most one, a group never holds another group, and a
group with no projects in it is an ordinary state rather than an error.
_Avoid_: Folder, tag, label, namespace, team, workspace, environment, category

**Host**:
The machine an operator runs projects on, created and named by them, holding its
samples and its token. A project sits on at most one, a host holds any number of
projects, and it is where samples come from rather than a way of asking about
entries — no query takes one.
_Avoid_: Server, node, machine, environment, instance, installation

**Collector**:
The small program an operator runs on a **Host** that reads the machine once a
minute and delivers a **Sample**. It is separate from the client packages
because an application cannot see the machine it shares, it holds no state, and
it does nothing besides read and post.
_Avoid_: Agent, exporter, daemon, probe, monitor, scraper

**Ingest Token**:
The write-only secret that admits a delivery to one project. It permits writing
and grants no read access of any kind, it is what identifies the project, it
records when it was last used, and a project holds one of them — or two while it
is being rotated.
_Avoid_: API key, password, credential, project id, access token

**Delivery Snippet**:
One finished delivery to this installation with an ingest token already in it,
handed over whenever a token is issued or read back. It is what the first-run
guide offers and what a project with no entries shows, and it is the plain path —
an address, a header and one entry — rather than the configuration of any
particular client.
_Avoid_: Example, code sample, quickstart, onboarding, sink configuration

**Agent Token**:
The secret an agent presents to MCP, issued and named by the operator, readable
again at any time, recording when it was last used, and revocable on its own.
Several exist at once, and each is issued as one kind or the other — a **Reading
Token** or an **Administering Token** — which its prefix carries and which never
changes for as long as the token exists.
_Avoid_: API key, session, connected agent, read token, credential

**Reading Token**:
The **Agent Token** issued to read: the query surface the operator has — entries,
counts and samples across every project — and no setting and no write of any
kind. It is what an agent is given unless the operator decides otherwise, and it
is the kind that meets untrusted log content.
_Avoid_: Read scope, viewer, query token, read-only key, permission

**Administering Token**:
The **Agent Token** issued to administer: the settings an operator works —
projects, groups, hosts, retention windows, and the issuing and revoking of
ingest and host tokens — and no **Log Entry**, ever. It issues a write credential
without ever reading one back, it reaches no **Agent Token**, no operator
credential and no **Session**, and it makes no **Destructive Change** unless it
was issued for that as well.
_Avoid_: Admin scope, write token, management key, root token, permission

**Destructive Change**:
An administering act after which stored data is gone: deleting a project or a
host, and lowering a **Retention Window** — a project's, or the installation's
for samples. An **Administering Token** may make one only if it was issued to,
which is settled when it is issued and never changes. Creating, renaming, moving,
raising a window and revoking a token are not one: nothing that is there stops
being there.
_Avoid_: Dangerous operation, write scope, hard delete, purge, permission

**Host Token**:
The write-only secret that admits a delivery of samples to one **Host**. It
permits writing and grants no read access of any kind, it is what identifies the
host, it records when it was last used, and a host holds one of them — or two
while it is being rotated. It is the **Ingest Token**'s model pointed at a host
instead of a project.
_Avoid_: API key, collector key, agent token, machine id, credential

**Token Identifier**:
The non-secret part a token carries between its prefix and its secret, naming the
row that holds it so that a presented token is found by one lookup rather than by
trying every token in turn. It admits nothing on its own, it is not what tells
the four token kinds apart — the prefix is — and it is not the name an agent
token carries for the operator's list.
_Avoid_: Key id, token name, project id, prefix, handle, public key

**Sender**:
An application delivering log entries to a project. It is trusted because the
operator runs it themselves, which says nothing about the content it delivers.
_Avoid_: Client, source, producer, agent, publisher

**Instance**:
One running copy of a sender — a container, a replica, a machine — named by the
sender itself so that several copies serving one project stay separable. Not to
be confused with an **Installation**, which is the deployed logaffe.
_Avoid_: Installation, node, host, source, machine, replica

**Batch**:
The set of log entries a sender hands over in one delivery. Delivery is
fire-and-forget: a sender does not wait for it, does not retry it, and learns
nothing later about whether it landed.
_Avoid_: Payload, chunk, page, upload, request

**Log Entry**:
The atomic record logaffe stores: one thing that happened in one sender, carrying
a level, two timestamps, a message template, its properties and an optional
exception. It is never edited after it arrives, and it leaves only by ageing out.
_Avoid_: Log line, log event, log message, record, row, item

**Level**:
The severity a sender assigned to an entry, being one of `Verbose`, `Debug`,
`Information`, `Warning`, `Error` and `Fatal`. An entry that names none is
`Information`.
_Avoid_: Severity, priority, importance, category

**Message Template**:
The message as the sender wrote it, which is always a template — a plain sentence
is one with no placeholders in it. It is what a sender delivers, and it is never
what the operator is shown.
_Avoid_: Format string, raw message, pattern, the message

**Rendered Message**:
The text produced by substituting an entry's properties into its message
template. It is what the operator reads, what a search matches, and it is
computed once when the entry arrives rather than each time it is read.
_Avoid_: Formatted message, display text, output, the message

**Property**:
A named value a sender delivers beside the message template, which may or may not
have a placeholder waiting for it. Properties are how an entry carries anything
the sentence itself does not.
_Avoid_: Field, tag, label, attribute, dimension, metadata

**Promoted Property**:
A property logaffe recognizes by name and lifts into a first-class, searchable
field of the entry — the instance, the logger name, and the trace and span that
an entry belongs to. Promotion asks nothing of a sender: a delivery carrying none
of them is complete.
_Avoid_: Reserved key, special property, indexed field, system field

**Event Time**:
The moment a sender says an entry happened. It is what the product orders entries
by, because it is the order in which things occurred.
_Avoid_: Timestamp, occurred at, log time, created at

**Receipt Time**:
The moment an installation received the batch an entry arrived in. It is what
retention counts from, because it is the only one of the two clocks a sender
cannot get wrong.
_Avoid_: Ingested at, server time, stored at, created at

**Filter**:
One narrowing of the entries a project holds — a time range, a level threshold,
an instance, a logger name, a trace, a search text, or an exception text. Filters
only ever remove entries, and those set together all apply at once.
_Avoid_: Query, condition, facet, clause, rule

**Search Text**:
The filter matched as a case-insensitive substring of an entry's rendered
message, anywhere in it and including inside a word. It is the product's only
free-text narrowing, it behaves like `grep` rather than like a search engine, and
it is at least three characters long — a shorter one is refused rather than run.
_Avoid_: Query, keyword, term, phrase, full-text search

**Exception Text**:
The filter matched as a case-insensitive substring of an entry's exception, and
the only narrowing that reaches a field the search text does not. It follows the
same three-character minimum and the same `grep` behaviour, and it is the one
filter no index serves — deliberately, because a stack trace is kilobytes where a
message is a line.
_Avoid_: Stack trace search, error filter, exception search, trace filter

**Page**:
One answer to a filter set: the entries it leaves, newest first by event time
with the identity breaking ties, up to a size that is the same in every
installation. It carries the cursor for the next one and never a total.
_Avoid_: Result set, batch, results, hits, window

**Cursor**:
The position a page left off at — the event time and the identity of its last
entry — which the following page is asked for. It is opaque to whoever holds it,
and it is what makes paging independent of how deep it has gone, where an offset
would skip and repeat entries as the store grows underneath the reader.
_Avoid_: Offset, page number, token, continuation, bookmark

**Tail Cursor**:
The position a live tail has already seen — the receipt time and the identity of
the latest entry to have arrived for it — which the next poll is handed back. It
is opaque in the same way and a separate thing from the cursor, because the two
name positions in two different orders: a page resumes where the log left off, a
poll resumes where delivery left off.
_Avoid_: Watermark, offset, last seen, checkpoint, timestamp

**Log View**:
The single screen on which the operator reads one project: the filters, the
entries they leave, and the detail of one of them. It is where nearly all use of
the web UI happens, it holds one project at a time, and the filters that make it
up are in its address.
_Avoid_: Dashboard, search page, console, explorer, stream

**Live Tail**:
The mode in which a view keeps itself current by asking, every few seconds, what
has arrived since it last asked. It follows receipt time while the view it feeds
stays ordered by event time.
_Avoid_: Stream, follow mode, real-time view, watch, subscription

**Count**:
The number of entries matching a set of filters, optionally grouped by level,
logger name, instance or time bucket, and answered without returning the entries
themselves. It is always asked for deliberately and never accompanies a page.
_Avoid_: Total, hits, results, statistics, metric

**Sample**:
One reading a **Collector** took of its **Host**, once a minute, carrying a
closed set of numbers — the processor, the memory, the load, and a filesystem's
used and total — and no text besides the mount path the operator named. It is
never edited after it arrives and leaves only by ageing out, as a **Log Entry**
does, and it belongs to a host rather than to a project. It carries one clock,
stamped when it arrives, and the collector's own is not stored.
_Avoid_: Metric, measurement, data point, gauge, series, reading

**Retention Window**:
The period a project keeps its entries, counted from receipt time, after which
they are removed. The operator sets it up to a ceiling no installation can raise,
and time is the only limit a project has — there is no size cap, no row quota,
and no interaction between limits. Samples have a window of their own, set once
for the installation and under the same ceiling, because they belong to a
**Host** rather than to a project.
_Avoid_: Retention policy, TTL, expiry, archive, quota

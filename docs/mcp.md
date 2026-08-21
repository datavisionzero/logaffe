# MCP

`VISION.md` puts agent access on equal footing with the web UI, and this is the
door it comes through. An agent either reads logs on the operator's behalf, over
the same query surface the UI uses ([Querying](./querying.md)), or administers the
installation over the surface the settings screens carry. Never both: which of the
two it is, is decided by the token it presents and by nothing else
([ADR 0046](./adr/0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)).

The endpoint is publicly reachable and authenticated, like everything else this
product exposes, and it carries a rate limit like every other public surface —
below the operator's, because every call behind it may be a five-second read and
an agent calls a handful of times to answer a question.

## The agent token

An agent authenticates with an **agent token**, issued by the operator and put
into the client's configuration by hand.

It is the same shape as an ingest token, and deliberately so: issued by the
operator, **readable again whenever it is wanted**
([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)),
**named** so that a list of them is readable, recording **when it was last
used**, and **revocable individually and immediately**. The product has one model
for a machine credential, pointing in four directions — an ingest token writes to
one project, a host token writes to one host
([Metrics](./metrics.md#the-host-token)), and an agent token either reads every
project or administers the installation
([ADR 0021](./adr/0021-an-agent-token-is-a-copied-secret.md)).

The name is the operator's own, and naming a token after the client it was issued
for is what makes the list readable. It is a label for the list and nothing
more — it does not identify the token to the server and changing it changes
nothing else. The installation never learns what a client calls itself: a token
is issued before any client has connected with it, and nothing about a call is
remembered afterwards.

### One kind or the other

An agent token is issued as **reading** or as **administering**, and the two do
not overlap. A reading token is given the five tools below and reaches no
setting; an administering token is given the twenty-one after them and reaches no
entry. The kind is settled when the token is issued and **cannot be changed
afterwards**: an agent that needs the other one is given a second token, and the
operator revokes whatever it replaces.

This is not a tidiness of the model, it is the whole of what makes administration
safe enough to offer at all. Prompt injection out of a log entry needs one session
that both holds untrusted text and can act, and an administering token never reads
an entry, so it never holds the text
([ADR 0046](./adr/0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)).
A capability added to a reading token would build precisely the session that
argument refuses.

**Both kinds arrive at the same endpoint.** `/mcp` answers with the tool list the
presented token earns, so how many servers an operator wires is decided by how
many tokens they hold rather than by how many addresses exist. A second URL would
be two routes, two rate-limit buckets and two things to keep in step, for nothing
the token does not already say.

**What the split does not do is separate the agent.** An operator who wires both
servers into one assistant has put both in one model's context, and nothing here
prevents that or notices it. What it buys is that an agent which never reads
entries becomes possible, that the combination is a deliberate act rather than the
default, and that the two are revoked independently — the answer to trouble is to
revoke one rather than to go dark.

### Connecting is one paste

The product hands over the **finished client configuration**, not the bare token:
the server address and the token already in place, ready to be pasted into the
agent's config. Assembling that by hand from an address, a header name and a
string is the fiddliest part of connecting an agent, and it is the part most
likely to be got wrong in a way that reports nothing useful.

What they are handed is the block itself, with this installation's address and
this token already in it — the same block for either kind, differing only in the
token inside it and in the name it suggests for the server:

```json
{
  "mcpServers": {
    "logaffe": {
      "type": "http",
      "url": "https://logs.example.com/mcp",
      "headers": {
        "Authorization": "Bearer logaffe_agent_…"
      }
    }
  }
}
```

The server is called **`logaffe`** for a reading token and **`logaffe-admin`**
for an administering one. That is a suggestion rather than a setting — what a
client calls a server is its own business and the installation never learns
it — but it is the suggestion an operator wiring up both kinds needs, because
two entries under one name in one client is one of them silently replacing the
other.

The endpoint is **`/mcp`** on the installation's own address. It is written down
here rather than only in the code because it goes into the configuration of
every agent that ever connects, which makes it a promise to all of them rather
than a route that can be moved. The address is filled in from the request the
operator asked over, so an installation reached through a reverse proxy hands
out the name they reached it by
([Operations](./operations.md#behind-a-reverse-proxy)).

This is the same move the first-run guide already makes for the Serilog sink
([Setup](./setup.md)): the shortest path from a running installation to a working
client is a block the operator does not have to assemble.

The same block comes back whenever the token is read back, because reading a
token back and being able to use it are the same errand.

**Several can exist at once**, because an operator with a terminal agent and a
desktop agent should be able to retire one without disturbing the other. The
name, the issue date and the last use are what make that list worth having: a
token that has not been used in months is one to revoke, and the list is the only
place that fact is visible. The last use is accurate to within five minutes and
is not shown as though it were finer
([ADR 0033](./adr/0033-the-last-use-of-a-token-is-written-coarsely.md)).

**The four token kinds carry different prefixes** — `logaffe_ingest`,
`logaffe_host`, `logaffe_agent` for a reading token and `logaffe_admin` for an
administering one — and none is accepted where another belongs. Pasting an ingest
token into an agent configuration is a mistake that will happen, and it should
fail immediately and legibly rather than send someone looking in the wrong place.
The two agent kinds share an endpoint and are told apart the same way, which is
why the kind is in the prefix rather than only in the row: it is read before the
token is looked up at all, so what a caller may ask for is settled without the
database being asked anything
([ADR 0031](./adr/0031-a-token-names-its-own-row.md)).

An agent token is ended by revoking it, which **removes the row** exactly as
revoking an ingest token does
([Projects and tokens](./projects.md#rotation-and-knowing-when-it-is-done)) — a
retired agent leaves no entry in the list and no sealed secret behind it. It also
ends when [Host Recovery](./setup.md#host-recovery) hands the installation to a
new operator, which is the one act that removes every agent token at once — a
credential that reads every project, or administers the installation, must not
outlive the operator who issued it
([ADR 0013](./adr/0013-host-recovery-returns-the-installation-to-unclaimed.md)).
A password change does **not** end it: an operator who has to
reconnect every agent whenever they change their password is an operator who
changes their password less often.

## The tools

Five for a reading token, and no others.

**`list_projects`** — the projects in the installation, each with its name, the
group it sits in when it sits in one, the host it sits on when it sits on one,
its retention window, and when it last received an entry. An agent has to know what exists before it can ask about it,
and "has this project received anything lately" is the cheapest possible health
question.

**The group and the host both ride on the project rather than being tools of
their own.** The group is what lets *the production one of shop* reach an
identity, and it is also what keeps the operator's own word for two projects from
being a thing the agent has to guess at. The host is what lets an agent go from
the errors in a project to the machine behind them without this adapter holding a
query that resolves one into the other. A tool listing either would be a second
read path for a fact this one already carries, and there is nothing further to
ask of a group at all: no filter, no scope and no query takes one
([Projects and tokens](./projects.md)). A project's name is unique only within
its group, which costs the agent nothing — every tool names a project by
identity, as the next paragraph says.

**`search_entries`** — a project, the filters from [Querying](./querying.md), a
verbosity, and a cursor. The filters are exactly the operator's: a time range on
event time, a level threshold, an instance, a logger name, a trace, a search
text, and an exception text, all combining with AND and nothing else
([ADR 0011](./adr/0011-filters-only-narrow-and-only-with-and.md)).

The project is named by the identity `list_projects` gives, and so is every
other tool's. A name would be friendlier to write and would mean this adapter
resolving one, which is a query it is not allowed to have — the first tool
exists so that the other three do not need to look anything up.

**The level threshold is offered as the six names themselves**, in the tool
schema rather than only in the description, and spelled the way an entry answers
its own level — so a level read out of one answer narrows the next call without
being translated. It is the only filter a schema can state: a cursor is opaque, a
trace is a length, a search text is a minimum, and those are refused in a
sentence naming the argument. This one is refused by the contract, before the
read is entered.

**`count_entries`** — the same filters, answered as a number, optionally grouped
by level, logger name, instance or time bucket. This is what turns *"were there
critical errors in the last three days"* into an answer instead of forty thousand
rows in a context window.

**The number and the groups are two answers, and only one of them is present.**
An ungrouped count is the number itself rather than a single row under the
grouped answer's key, because a row that carries no value to be grouped under is
the grouped shape with the grouping taken out — and it asks the agent to reach
into a collection for the one thing it asked for directly. Which of the two is
there says which question was asked.

**`get_entry`** — one entry by its identity, always in full. It exists for the
follow-up after a compact search: the agent sees a promising line and wants the
exception and the properties behind it.

**`get_host_samples`** — a host identity and a time range, answered with what the
machine reported about itself over it ([Metrics](./metrics.md)). This is the tool
for the question the entries cannot answer: the errors started at 03:14, and the
memory on that machine had been at the ceiling since 02:50.

The host is named by the identity `list_projects` gives, exactly as a project is,
and for the same reason — resolving a project into its host is a query this
adapter is not allowed to have.

**The answer is bucketed, and each bucket carries its average and its peak.** A
week of one-minute samples is ten thousand readings and would spend an agent's
context on the shape of a line, so the bucket is chosen from the range to keep an
answer inside a cap. The peak rides along because an average is precisely what
hides the spike that was worth finding, and a missing minute is **absent rather
than interpolated** — that the machine was too busy to report is a fact, and a
line drawn through the gap states its opposite.

**This is the one tool whose answer is not confined to a single project**, since
a host may carry several. Samples are numbers the installation's own collector
read off a machine and carry no text from anywhere, so the boundary that holds
untrusted content inside one project has nothing here to hold apart
([ADR 0045](./adr/0045-a-sample-is-not-an-entry-and-may-be-read-across-projects.md)).

## Compact and full

`search_entries` returns either shape, chosen per call.

- **Compact** — event time, level, logger name, instance, and the rendered
  message. Up to 200 entries. This is the default, because a broad search that
  silently spends an agent's whole context is worse than one that needs a second
  call.
- **Full** — everything: both timestamps, the message template, all properties,
  the exception, and the truncation flag. Up to 50 entries.

Both carry the entry's identity as well, because `get_entry` is asked with it and
the follow-up after a compact search is the whole reason the compact shape
exists. What compact leaves out is **absent rather than null**: it exists to save
context, and writing out the fields it does not carry would spend a good part of
what it saved.

The two caps are this door's and are not the page size of
[Querying](./querying.md). They sit above it — a tool fills its cap from as many
pages as that takes — and they differ from each other because the entries behind
them are different sizes.

**Every response says how many entries matched in total and whether it was
capped.** An agent that receives fifty entries and is not told there were nine
thousand will answer as though there were fifty, and that is the quietest way
this product could produce a wrong answer. Where an answer was capped, it also
carries the cursor to carry on from.

**The count behind that number is run only when the answer does not already
contain it.** A first call that was not capped returned every entry the filters
match, so the total is the length of what came back, and a second statement over
the largest table in the database would be paying to be told something already in
hand. Everything else is counted: an answer that stopped at its cap, and a
continuation, whose earlier entries are not in it.
[Querying](./querying.md) refuses a total beside a page for the operator; the cap
is why the rule differs here, and the cap is also what keeps the count from being
run on the reads that do not need it.

**A capped answer is never handed over without its total.** A count is the read
most likely to use up its five seconds, and when it is the one that does, the
whole call comes back as an expired read saying what to narrow — rather than as
entries with an unknown number behind them, which would be the failure the number
exists to prevent, wearing the shape of a success.

## What an answer leaves out, its schema leaves optional

A field carrying nothing is left out rather than written as `null`. The compact
shape is the largest case of it, but not the only one: a project in no group, a
project that has never received an entry, a search with no cursor to hand back,
and a count that came back with what to narrow all leave a field out.

A client validates the structured content of a result against the output schema
the tool declared, so the two have to agree — **the schema requires only what
every answer of that tool carries.** A field that the description calls sometimes
absent and the schema calls required is an answer the client throws away while
the installation believes it has answered, and the operator is left looking for a
fault at their end of a call that succeeded.

## Entries reach the agent as data

Entries are structured values in named fields — never markdown, never a
formatted transcript, never text folded into a sentence addressed to the agent
([ADR 0012](./adr/0012-log-content-reaches-an-agent-as-data-never-as-prose.md)).
The rendered message is a field carrying text, and nothing in it is interpreted.

## A read that runs out of its five seconds

Every read on this surface gets five seconds, the same five as the operator's,
with no setting that raises them
([ADR 0026](./adr/0026-a-read-has-five-seconds.md)). One that uses them up is not
an error to report: it comes back with the adjustments that would make the next
one finish — set a time range, make the one already set smaller, take off the
exception filter — in the order to try them. They are **named values and not a
sentence**, for the same reason entries are: the operator's screen writes the
sentence from these values, and the agent is handed the fact
([ADR 0012](./adr/0012-log-content-reaches-an-agent-as-data-never-as-prose.md)).

## Administering

**Twenty-one tools, seventeen of them on a token that may not destroy.** They are
the acts the settings screens carry, named as the product already names them
([Projects and tokens](./projects.md), [Metrics](./metrics.md)) rather than in a
vocabulary invented for this door.

**`get_settings`** — the whole surface in one answer: the groups, the projects
with the group each sits in, the host each sits on and its retention window, the
hosts, the installation's window for samples, and for each project and host how
many tokens it holds and when each was last used. **No token value is in it**, and
neither is an entry.

It is one tool rather than one per list for the reason `list_projects` carries the
group and the host rather than leaving them to tools of their own: it is a tree
that fits in one answer, and a second read path for a fact the first already
carries is another thing to keep in step. It is also why it is not called
`list_projects` — that name is taken on the other surface, by a tool that answers
differently, and an operator who has wired up both agents should not meet one word
meaning two things. The identities are the same ones, so the two are looking at
one installation.

**Projects** — `create_project`, `rename_project`, `move_project_to_group`,
`put_project_on_host` and `delete_project`. Creating a project hands back no
token; issuing one does, exactly as the screen does it
([Projects and tokens](./projects.md#the-ingest-token)).

**Groups** — `create_group`, `rename_group` and `delete_group`. Deleting one is
not destructive, and it is the clearest illustration of what that word is doing
here: a group holds nothing, so its projects come out of it and stay
([ADR 0039](./adr/0039-a-group-has-an-identity-and-holds-nothing.md)).

**Hosts** — `create_host`, `rename_host` and `delete_host`.

**Retention** — `extend_project_retention` and `shorten_project_retention`,
`extend_sample_retention` and `shorten_sample_retention`, against the one act the
installation performs. **The window is two tools because only one direction
destroys**, and splitting it is what keeps the flag legible in the tool list: a
token that may not destroy is not handed a shortening tool that refuses, it is
handed no shortening tool at all. A call that would move the window the other way
is refused in a sentence naming the tool that does it, and the agent knows which
it wants because `get_settings` told it where the window is now.

**`count_entries_outside_window`** — how many entries a proposed window would
drop, asked before anything drops. It is not destructive and it is on every
administering token, including one that cannot shorten: an agent that may not make
the change can still tell the operator what it would cost, which is the useful
half of the answer. It returns a number and no entry, so it is a count on this
surface for the reason a sample is a read on the other one
([ADR 0045](./adr/0045-a-sample-is-not-an-entry-and-may-be-read-across-projects.md)).

**Tokens** — `issue_ingest_token`, `revoke_ingest_token`, `issue_host_token` and
`revoke_host_token`. Revoking is two tools where the installation has one act that
takes any token, and the split is the whole point: an agent token is then absent
from the list rather than refused inside a call, which is what "never reachable"
has to mean to be worth saying.

### Destroying is four tools

An administering token is handed `delete_project`, `delete_host`,
`shorten_project_retention` and `shorten_sample_retention` only if it was issued
saying so, off unless asked for, and settled once like the kind itself.
**Destructive means data that does not come back**: deleting a project takes its
entries with it
([ADR 0019](./adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)),
deleting a host takes its samples, and each shortening removes what now falls
outside the window.

The two shortenings are why the flag is not called *delete*. They read like
settings and they remove stored entries, which makes them the ones worth being
unable to do by accident. Everything else on the surface stays: creating,
renaming, moving, extending a window and revoking a token are not destructive — a
revoked token stops a sender delivering and the entries that would have arrived
never exist, but nothing that is already there is gone afterwards, and another
token closes the gap.

### A token is issued, never read back

`issue_ingest_token` and `issue_host_token` work on any project or host, not only
on one the agent has just made, and there is **no tool that reads an existing
token back** — not directly, and not through the delivery snippet, which carries
the token inside it. A token value reaches an agent at the moment it is created
and never again; recovering one is an errand at a browser, where the operator
reads it back as they always could
([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)).
`get_settings` counts a project's tokens and says when each was last used, which
is what rotation needs to know and is not the token.

Issuing where a token already exists is rotation, and it is allowed outright
rather than tolerated, because the narrow rule does not survive the section above
it: revoking is not destructive, so an agent could revoke the live token and issue
a fresh one and be where the rule said it could not go. What it buys is the whole
cycle — issue the second token, hand it over, revoke the first.

**The blast radius is stated plainly rather than softened**
([ADR 0046](./adr/0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)):
an administering agent can put a live write credential into a project the operator
trusts, which is forged entries in a stream they read as real. What bounds it is
not a confirmation step. It is that an administering token reads no entry, so the
sentence asking for the credential never enters its context, and that an ingest
token is write-only, so nothing is read back out through one.

### Three things no token reaches

Absent from the interface rather than withheld by a flag, the way this whole
surface was absent before.

- **Agent tokens.** An agent that could issue one would grant itself the kind and
  the flag the operator withheld, and both would be decoration. This is the one
  that makes the rest coherent.
- **The operator's credentials** — password, second factor, backup codes. An agent
  that can re-enrol a second factor owns the account.
- **Sessions.** Ending one denies the operator their own access, and listing them
  is a record of where the operator has been.

## What the agent cannot do

- **No token writes an entry.** There is no tool that creates, edits or deletes
  one, and none that acknowledges, marks or annotates one. Deleting a project
  takes its entries with it, and that is the only way an entry leaves by an
  agent's hand.
- **A reading token manages nothing.** Not as a permission, but as an absence
  from the tool list it is handed: a log entry that asks the agent reading it to
  mint a credential must find nothing to call, which is the argument 0018 made
  and the one thing this interface still owes it in full
  ([ADR 0046](./adr/0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)).
- **It cannot follow logs live.** There is no tail, no subscription and no
  polling loop offered to an agent. `VISION.md` is explicit that the agent looks
  because the operator asked, and that passive continuous monitoring is not part
  of the product. A client that opens a stream on the endpoint expecting to be
  told something is answered that there is no such stream, rather than being left
  holding one that will never carry anything.
- **It cannot read log content across projects.** Every tool that returns an
  entry names one project, exactly as the UI does. The rule is stated by what a
  tool returns rather than by counting tools, because `get_host_samples` answers
  for a machine that may carry several and carries no entry in its answer
  ([ADR 0045](./adr/0045-a-sample-is-not-an-entry-and-may-be-read-across-projects.md)).
- **A reading token cannot manage hosts either.** Creating a host, naming one,
  deleting one, minting its token, or saying which host a project sits on are on
  the administering surface, and absent from the reading one for the same reason
  as everything else there.
- **It cannot ask for a sample to be taken.** It reads what the collectors have
  already delivered; nothing on this surface reaches out to a machine.

## What is deliberately not here

- **No resources and no prompts**, only tools. A log store answers parameterized
  questions; exposing projects as readable resources would be a second way to ask
  the same thing, with its own caching and its own surface.
- **No saved queries, no agent-side state.** Every call stands alone, and the
  installation remembers nothing about what an agent asked before.
- **No agent-initiated anything.** Nothing is scheduled, watched, or delivered
  without a call, and nothing reaches an agent that an agent did not ask for. The
  installation does now send something unasked — three conditions, to a push
  notifier ([Alerts](./alerts.md)) — and none of it comes down this interface, is
  written by a model, or is a reason for an agent to be running.
- **No alerting surface, on either kind of token.** The notifier, the three
  switches and a project's mute are absent from the tool list a reading token is
  handed and from the twenty-one an administering one earns. Adding one is a
  change to this document, and it would be a credential that could switch off the
  thing that tells the operator something is wrong.
- **No second, weaker door.** An ingest token is not a read credential, a session
  cookie is not an agent token, and there is no anonymous access to any of it.

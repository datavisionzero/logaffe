# MCP

`VISION.md` puts agent access on equal footing with the web UI, and this is the
door it comes through. The agent reads logs on the operator's behalf, over the
same query surface the UI uses ([Querying](./querying.md)), and it can do nothing
else at all.

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
for a machine credential, pointing in two directions — an ingest token writes to
one project, an agent token reads everything
([ADR 0021](./adr/0021-an-agent-token-is-a-copied-secret.md)).

The name is pre-filled with what the client calls itself and can be overwritten.
It is a label for the list and nothing more — it does not identify the token to
the server and changing it changes nothing else.

### Connecting is one paste

The product hands over the **finished client configuration**, not the bare token:
the server address and the token already in place, ready to be pasted into the
agent's config. Assembling that by hand from an address, a header name and a
string is the fiddliest part of connecting an agent, and it is the part most
likely to be got wrong in a way that reports nothing useful.

What they are handed is the block itself, with this installation's address and
this token already in it:

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

**The two token kinds carry different prefixes**, and neither is accepted at the
other's endpoint. Pasting an ingest token into an agent configuration is a
mistake that will happen, and it should fail immediately and legibly rather than
send someone looking in the wrong place. The prefix is read before the token is
looked up at all, so the wrong kind is turned away without the database being
asked anything ([ADR 0031](./adr/0031-a-token-names-its-own-row.md)).

An agent token is ended by revoking it, which **removes the row** exactly as
revoking an ingest token does
([Projects and tokens](./projects.md#rotation-and-knowing-when-it-is-done)) — a
retired agent leaves no entry in the list and no sealed secret behind it. It also
ends when [Host Recovery](./setup.md#host-recovery) removes the account it
belongs to. A password change does **not** end it: an operator who has to
reconnect every agent whenever they change their password is an operator who
changes their password less often.

## The tools

Four, and no others.

**`list_projects`** — the projects in the installation, each with its name, its
retention window, and when it last received an entry. An agent has to know what
exists before it can ask about it, and "has this project received anything
lately" is the cheapest possible health question.

**`search_entries`** — a project, the filters from [Querying](./querying.md), a
verbosity, and a cursor. The filters are exactly the operator's: a time range on
event time, a level threshold, an instance, a logger name, a trace, a search
text, and an exception text, all combining with AND and nothing else
([ADR 0011](./adr/0011-filters-only-narrow-and-only-with-and.md)).

The project is named by the identity `list_projects` gives, and so is every
other tool's. A name would be friendlier to write and would mean this adapter
resolving one, which is a query it is not allowed to have — the first tool
exists so that the other three do not need to look anything up.

**`count_entries`** — the same filters, answered as a number, optionally grouped
by level, logger name, instance or time bucket. This is what turns *"were there
critical errors in the last three days"* into an answer instead of forty thousand
rows in a context window.

**`get_entry`** — one entry by its identity, always in full. It exists for the
follow-up after a compact search: the agent sees a promising line and wants the
exception and the properties behind it.

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

## What the agent cannot do

- **It cannot write anything, anywhere.** There is no tool that creates, edits or
  deletes an entry, and none that acknowledges, marks or annotates one.
- **It cannot manage projects or tokens.** Not as a permission, but as an absence
  from the interface
  ([ADR 0018](./adr/0018-projects-and-tokens-are-never-reachable-over-mcp.md)) —
  a log entry that asks an agent to mint a credential must find nothing to call.
- **It cannot follow logs live.** There is no tail, no subscription and no
  polling loop offered to an agent. `VISION.md` is explicit that the agent looks
  because the operator asked, and that passive continuous monitoring is not part
  of the product. A client that opens a stream on the endpoint expecting to be
  told something is answered that there is no such stream, rather than being left
  holding one that will never carry anything.
- **It cannot read across projects.** Every tool names one, exactly as the UI
  does.

## What is deliberately not here

- **No resources and no prompts**, only tools. A log store answers parameterized
  questions; exposing projects as readable resources would be a second way to ask
  the same thing, with its own caching and its own surface.
- **No saved queries, no agent-side state.** Every call stands alone, and the
  installation remembers nothing about what an agent asked before.
- **No agent-initiated anything.** Nothing is scheduled, watched, or delivered
  without a call. Settled in `VISION.md`.
- **No second, weaker door.** An ingest token is not a read credential, a session
  cookie is not an agent token, and there is no anonymous access to any of it.

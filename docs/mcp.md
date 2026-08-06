# MCP

`VISION.md` puts agent access on equal footing with the web UI, and this is the
door it comes through. The agent reads logs on the operator's behalf, over the
same query surface the UI uses ([Querying](./querying.md)), and it can do nothing
else at all.

The endpoint is publicly reachable and authenticated, like everything else this
product exposes.

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

This is the same move the first-run guide already makes for the Serilog sink
([Setup](./setup.md)): the shortest path from a running installation to a working
client is a block the operator does not have to assemble.

**Several can exist at once**, because an operator with a terminal agent and a
desktop agent should be able to retire one without disturbing the other. The
name, the issue date and the last use are what make that list worth having: a
token that has not been used in months is one to revoke, and the list is the only
place that fact is visible.

**The two token kinds carry different prefixes**, and neither is accepted at the
other's endpoint. Pasting an ingest token into an agent configuration is a
mistake that will happen, and it should fail immediately and legibly rather than
send someone looking in the wrong place.

An agent token is ended by revoking it, or by
[Host Recovery](./setup.md#host-recovery) removing the account it belongs to. A
password change does **not** end it: an operator who has to reconnect every agent
whenever they change their password is an operator who changes their password
less often.

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

**Every response says how many entries matched in total and whether it was
capped.** An agent that receives fifty entries and is not told there were nine
thousand will answer as though there were fifty, and that is the quietest way
this product could produce a wrong answer.

## Entries reach the agent as data

Entries are structured values in named fields — never markdown, never a
formatted transcript, never text folded into a sentence addressed to the agent
([ADR 0012](./adr/0012-log-content-reaches-an-agent-as-data-never-as-prose.md)).
The rendered message is a field carrying text, and nothing in it is interpreted.

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
  of the product.
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

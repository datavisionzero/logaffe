# Metrics

Logs say what an application did. They do not say that the machine it ran on had
been out of memory for twenty minutes, and that is the question an operator asks
immediately after reading an error and cannot answer from the log store at all.

This is the whole of what metrics are for here: **the numbers a machine reports
about itself, kept long enough to be looked at next to the entries they explain.**
An operator with a terminal answers that question with `htop` and gets the
machine as it is now. What this adds is the two things `htop` cannot give —
**the machine as it was at three in the morning**, and **a form the agent can
reach**, because the agent has no shell on the host and MCP is its only window.

It is deliberately not a metrics system. There is no metric to define, no label to
choose, no query language, and no alert
([ADR 0044](./adr/0044-a-sample-has-a-closed-schema.md)).

## The sample

A **sample** is one reading of one host, taken **once a minute**. The interval is
the product's and not a setting: a number that can be turned up is a number
someone turns up, and the storage this rests on was sized for one row per host
per minute.

It carries a closed set of numbers and no others:

- **CPU** — the share of the interval the machine spent busy.
- **Memory** — used and total, in bytes.
- **Load** — the one-, five- and fifteen-minute averages.

A **filesystem reading** is its own row beside it, one per mount per minute,
carrying the mount's path with its used and total bytes. It is separate because
a machine has several filesystems and one processor, and folding the two into one
row would mean a sample whose shape depends on how the collector was configured.

**Which mounts are read is named in the collector's configuration**, and the root
filesystem is what it reads when nothing is named. This is the one place the
operator decides what is collected, and it is bounded by a list they wrote rather
than by anything discovered at runtime — a host that mounts forty container
overlays does not silently become forty rows a minute.

**A sample is never edited and leaves only by ageing out**, exactly as a log entry
does.

### It carries one clock, and it is the installation's

An entry has two timestamps because a sender's clock can be wrong about when
something happened and retention may not be counted from a number the sender
chose ([ADR 0007](./adr/0007-the-sender-orders-the-receipt-expires.md)). A sample
has one, stamped when it arrives, and the collector's own clock is not stored at
all.

The second clock bought the entry an ordering — the order things occurred in,
which is not the order they arrived in. A sample has no such gap to bridge:
delivery is fire-and-forget with no buffer and no retry, so a reading is at most
a second old when it lands, and nothing anywhere asks for samples in an order
other than the one they arrived in. What the single clock removes is a whole
class of fault — a collector whose clock is a year fast writing samples that the
retention sweep will never reach.

**What it costs is that the band and the entries are drawn against different
clocks.** The entries under it are ordered by event time, which is the sender's,
and the band above them is on the installation's. Where the two machines disagree,
the band is offset by that much. It is accepted because the alternative does not
fix it — a collector's clock and a sender's clock are two different machines
either way — and because trusting one more clock to fix a disagreement between
two is how the wrong one becomes load-bearing.

## The host

A **host** is a machine an operator runs their projects on. It is created by the
operator, it carries a name, and it holds its samples and its token.

Like a group, it is **a row with its own identity**, surviving every rename
([ADR 0039](./adr/0039-a-group-has-an-identity-and-holds-nothing.md)). Unlike a
group, it does not have to be argued for: a host holds data from the day it
exists, so the identity is paying for itself rather than being bought early.

**A host with no samples is an ordinary state**, not an error. It is what a host
is between being created and its collector being started, and it is what a host
becomes when its machine is switched off.

**When a host last reported is read off its newest sample** rather than written
beside it. There is nothing else to keep current, and a field saying the host
reported a minute ago while its newest sample is a day old is the disagreement
that comes free with storing the same fact twice
([ADR 0039](./adr/0039-a-group-has-an-identity-and-holds-nothing.md)).

### Deleting a host

Deleting is **immediate and irreversible, and it is confirmed by typing the host's
name** — the guard a project's deletion carries
([ADR 0019](./adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)),
for the same reason: the samples do not come back.

**The projects that sat on it are left sitting on none**, and nothing else about
them changes. That half is the group's behaviour, and it is right here for the
group's reason: a host is where a project runs, and forgetting where it runs
destroys nothing that belongs to the project.

## The host token

A host holds **one token, and two while it is being rotated** — the ingest
token's model, entire ([Projects and tokens](./projects.md#the-ingest-token)).
The token *is* the host as far as the collector is concerned: it identifies which
host a delivery belongs to, and there is nothing else for the collector to be
told beyond an address.

It is **write-only**, admitting samples and granting no read of any kind. It is
stored encrypted rather than hashed and can be **read back at any time**
([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)),
it carries the **non-secret identifier** that names its row so a delivery costs
one indexed lookup ([ADR 0031](./adr/0031-a-token-names-its-own-row.md)), and it
records **when it was last used**, to within five minutes
([ADR 0033](./adr/0033-the-last-use-of-a-token-is-written-coarsely.md)).

**Its prefix is `logaffe_host`**, against `logaffe_ingest` and `logaffe_agent`.
There are now three kinds and none of them is accepted at another's endpoint. The
prefix is read before the token is looked up at all, so a host token pasted into
a Serilog sink is turned away legibly and without the database being asked
anything.

Revoking removes the row, and a collector holding a revoked token is answered
`401` and carries on doing nothing else, exactly as a sender does.

## The collector

Samples are delivered by a **collector** — a small program the operator runs on
the machine it measures. It is a separate thing from the client packages and
deliberately so: an application cannot report the machine it shares with four
other applications, and if it tried, one machine would be reported five times by
five processes that each see a different part of it
([ADR 0043](./adr/0043-metrics-come-from-the-host-not-from-the-application.md)).

**It ships as a container image**, which is the shape of everything else an
operator runs here. The awkward part is that a container sees the container, so
the machine is read through two **read-only bind mounts** — the host's `/proc`
and its root filesystem. That is what every collector of this kind does, and it
is **not something the operator should have to know**.

**Those two mounts are the whole of what it asks for.** It is not privileged, it
does not join the host's PID namespace, it does not touch the Docker socket, and
it opens no port — it posts outbound and is never connected to. That list is
short because the schema is closed
([ADR 0044](./adr/0044-a-sample-has-a-closed-schema.md)): reading processes is
what would need the PID namespace, and reading containers is what would need the
socket, and this collects neither
([Deploying](./deployment.md#the-collector-on-a-machine)).

So they are not told it. **Issuing a host's token hands back the finished command
with this installation's address, that token and those mounts already in it** —
the same move the ingest token makes with its delivery snippet
([Setup](./setup.md)) and the agent token makes with its client configuration
([MCP](./mcp.md)), and it arrives with the token for their reason: reading a
token back and being able to use it are one errand. Making the host is the act
before it and hands back nothing, exactly as making a project hands back no
snippet. The shortest path from a new host to a reporting one is a block the
operator does not assemble.

**Delivery is fire-and-forget**, on the log path's terms and for the log path's
reason: the collector does not wait, does not retry, and learns nothing about
whether a sample landed. A collector that cannot reach the installation drops the
sample and takes the next one a minute later.

**A gap is shown as a gap.** Nothing interpolates across missing minutes, because
the most interesting thing a missing minute can mean is that the machine was too
busy to report, and a line drawn through it says the opposite.

## What a collector delivers

**One reading per delivery, and never a batch.** A collector takes a sample and
posts it; it does not buffer, does not retry and has nothing to catch up on, so
there is no second reading for a delivery to carry. This is where samples part
company with entries, whose batching exists because an application produces them
faster than it should open connections.

It is a `POST` of one JSON object, with the host token as a bearer credential:

```
POST /samples
Authorization: Bearer logaffe_host_…
Content-Type: application/json

{
  "cpu": 0.42,
  "memoryUsed": 6115295232,
  "memoryTotal": 16769712128,
  "load1": 0.52,
  "load5": 0.61,
  "load15": 0.58,
  "filesystems": [
    { "mount": "/", "used": 41234567890, "total": 107374182400 }
  ]
}
```

**There is no timestamp on the wire**, which is the single clock made visible: the
installation stamps the sample when it arrives and there is nothing for a
collector's clock to be wrong about. A field for it would be a field somebody
eventually trusts.

**The shape is the schema and nothing else is read.** A member the installation
does not know is ignored rather than stored, because there is nowhere to store it
([ADR 0044](./adr/0044-a-sample-has-a-closed-schema.md)) — which is also what
makes the format additive: an older collector omitting a number added later
delivers a sample that lacks it, and a newer one sending a number an older
installation has never heard of is read by the part it does know.

**A delivery is refused whole or taken whole**, unlike a batch of entries
([ADR 0006](./adr/0006-a-batch-is-accepted-in-part.md)). Partial acceptance
exists because one broken line must not cost the other nine hundred and
ninety-nine; one reading has no other lines to protect, and half a sample —
memory without processor — is a band with a hole in it that looks like data. A
delivery that is not a reading is `400`, and the next one is a minute away.

The answers are otherwise the ingest endpoint's: `401` for a token that admits
nothing, saying no more than that; `429` over the throttle every public surface
carries; `503` when the store cannot be reached, with the sample gone and the
collector none the wiser.

## The project sits on at most one host

A project names **the host it runs on, or none** — the shape a project's group
already has, and the same sentence
([Projects and tokens](./projects.md#the-group)). It is what makes an error in
`shop / api` reachable to the memory of the machine `shop / api` runs on, and it
is the whole of the relation.

**A project on no host is ordinary**, and so is a host with no projects. The first
is every project until the operator says otherwise, and it costs nothing except
that there is no band to draw over its entries.

**A project replicated across two machines names one of them or neither.** This is
a real limitation and not an oversight: the truthful owner of a host is the
instance rather than the project, and the instance is a property a sender writes
into its own entries ([Ingestion](./ingestion.md)) rather than something the
installation manages. Making it manageable is a larger product than this one, and
an installation holding ten to thirty projects on a handful of machines does not
need it yet. What is kept open is the identity: the relation hangs off a host that
is a row, so moving it to the instance later is an addition rather than a
migration that has to invent one.

**The host is not a scope.** No query takes one, no filter narrows by one, and
naming two projects onto one machine does not make them askable together — the
rule a group already carries ([Querying](./querying.md)). A host is where samples
come from, never a way of asking about entries.

## Retention

Samples are kept for a period **set once for the installation**, counted from
receipt, up to the same **maximum of ninety days** every retention window here
has ([ADR 0020](./adr/0020-retention-has-a-maximum.md)).

It is one number rather than one per host because there is no reason to keep one
machine's numbers longer than another's, and it is capped for the reason the
project's is: a settings box without a ceiling is how a product that is not a
multi-year archive becomes one without anyone deciding it should.

**Lowering it removes samples**, and the operator is told how many before it takes
effect. Raising it again brings nothing back.

Samples are small and few — a handful of rows a minute against a log store's
thousands — so the sweep that removes them is the entry sweep's arrangement and
nothing more elaborate
([ADR 0023](./adr/0023-retention-deletes-rows-rather-than-dropping-partitions.md)).

## Reading them

### The operator reads them over the entries

The log view grows **a band above the entries**, drawn for the host the open
project sits on, over **exactly the time range the filters already state**. It
moves when the range moves, it is absent when the project sits on no host, and it
is the only place in the product where a sample is drawn.

This is the feature. Everything above it exists so that an operator looking at
four minutes of errors sees, without leaving the screen or opening a second tool,
that memory went to the ceiling three minutes before the first one.

It is **a band and not a dashboard**: no chart to configure, no metric to pick, no
second view, no arrangement to save. A host's own screen in the settings shows the
same numbers over a plain range, for the times the question is about the machine
rather than about a project.

### The agent asks for a host

MCP gains **a fifth tool**, `get_host_samples`, taking a host identity and a time
range and answering with samples.

**The host arrives on the project.** `list_projects` names the host a project sits
on beside the group it sits in, which is what lets the agent go from *the errors
in `shop / api`* to the machine behind them without a tool that resolves one into
the other — the same argument that keeps groups off a tool of their own
([MCP](./mcp.md)).

**The answer is bucketed, and each bucket carries its average and its peak.** A
week at one sample a minute is ten thousand readings and would spend an agent's
context on the shape of a line. Buckets are chosen from the range so that an
answer stays inside the cap the compact search already sets, and the peak rides
along because an average is exactly what hides the spike that was worth finding.

The read has **five seconds** like every other
([ADR 0026](./adr/0026-a-read-has-five-seconds.md)), the samples arrive as
**named values and never as prose**
([ADR 0012](./adr/0012-log-content-reaches-an-agent-as-data-never-as-prose.md)),
and the tool **writes nothing and manages nothing** — it cannot create a host,
delete one, mint a token or say which host a project sits on
([ADR 0018](./adr/0018-projects-and-tokens-are-never-reachable-over-mcp.md)).

**This is the one read that is not inside a single project**, and why that is
allowed is [ADR 0045](./adr/0045-a-sample-is-not-an-entry-and-may-be-read-across-projects.md):
a sample is a number the installation's own collector produced, carrying no text
from anywhere, so the boundary that keeps untrusted content in one project has
nothing to hold apart.

## What is deliberately not here

- **No metric an operator defines, and no labels.** The schema is closed
  ([ADR 0044](./adr/0044-a-sample-has-a-closed-schema.md)). Counters, gauges,
  histograms, request rates and latency percentiles are absent, and adding one
  reopens that document rather than adding a field.
- **No application or runtime metrics.** GC pauses, heap size and threadpool
  depth are not collected, and the client packages do not sample the process they
  live in
  ([ADR 0043](./adr/0043-metrics-come-from-the-host-not-from-the-application.md)).
- **No alerting.** No rule, no threshold, no notification, and nothing that
  watches. `VISION.md` settles it, and the data existing here does not reopen it —
  it only means that the day it is revisited, there is something to evaluate.
- **No OTLP, no Prometheus scrape, no `/metrics` endpoint.** The collector pushes,
  for the reason ingestion pushes: an installation on the public internet that
  reaches back into the operator's machines to pull is a different security
  posture than one that only ever receives.
- **No query language.** A host and a range is the whole of what can be asked.
- **No dashboard, and nothing to arrange.** One band over the entries, one screen
  per host.
- **No host as a scope for entries.** Covered above; a query names one project.
- **No sub-minute resolution, and no configurable interval.** A five-second spike
  is not what this is for.
- **No agent-initiated collection.** The agent reads samples that already exist
  and cannot ask for one to be taken.

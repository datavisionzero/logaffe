# Projects and Tokens

The project is the unit everything else hangs off: an entry belongs to one,
retention is configured on one, a query runs inside one, and a token admits a
delivery to one. `VISION.md` builds multi-project capability in from the start
rather than retrofitting it, and this is what a project actually is.

## What a project is

A **name**, a **retention window**, and its **ingest token**. Nothing else — the
group it may sit in belongs to the group rather than to it, and so does the host
it may sit on.

The name is unique **within its group** and can be changed at any time. Two
projects called `api` is a trap for the operator who reaches for one of them at
three in the morning, and the uniqueness is there for that rather than for any
technical reason — which is why the group relaxes it exactly as far as it
resolves it. `shop / api` beside `blog / api` names two different things
wherever either of them appears; two projects called `api` in no group at all are
the trap itself, and stay refused. A project is identified by an identity that
survives every rename, and that identity is what entries, tokens and queries are
attached to — never the name.

Projects are **created explicitly by the operator**. There is no implicit
creation on first delivery, so a token that names nothing admits nothing, and an
installation's project list is exactly what the operator put there. The first
project usually comes from the first-run guide after the claim
([Setup](./setup.md)).

There is no cap on how many an installation holds. `VISION.md` expects on the
order of 10 to 30, and that is a statement about the shape of the product rather
than a limit to enforce.

## The ingest token

A project holds **one token, and two while it is being rotated**. That is the
whole model: the token *is* the project as far as a sending application is
concerned, and there is nothing to name, list or manage beyond it.

The token is **write-only**, admitting delivery and granting no read access of
any kind. The operator can **read it back at any time** — it is stored encrypted
rather than hashed, so mislaying it means looking it up rather than rotating and
redeploying
([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)).
It carries a recognizable prefix — `logaffe_ingest`, against the agent token's
`logaffe_agent` and the host token's `logaffe_host`
([Metrics](./metrics.md#the-host-token)) — which costs nothing and means an
accidental
appearance in a repository or a log is something a scanner can find. That second case is not
hypothetical: applications log the configuration they started with, and a token
that ends up in a log entry ends up here. The prefix is written down here rather
than only in the code, because a prefix nobody outside the product knows is a
prefix no scanner is looking for.

Between the prefix and the secret sits a **non-secret identifier naming the row
that holds the token**, so that a delivery is authenticated by one indexed lookup
and one comparison rather than by trying the installation's tokens in turn
([ADR 0031](./adr/0031-a-token-names-its-own-row.md)). It is the price of storing
a token encrypted instead of hashed: a randomized ciphertext cannot be looked up
by the value presented. The identifier admits nothing on its own — the secret is
the part after it.

### Rotation, and knowing when it is done

Every token records **when it was last used**. Without that, rotation is
guesswork: the operator issues a new token, rolls the deployments over, and then
has to decide whether the old one is still feeding something they forgot. With
it, rotation is finished when the old token's last-used stops moving.

The sequence is therefore: issue the second token, move the applications over,
watch the old one go quiet, revoke it. Revocation takes effect immediately.

Issuing a third is **refused** rather than queued or rotating the oldest out: two
tokens is what moving deployments over one at a time needs, and a third means the
operator has lost track of which one they are retiring. They revoke one first,
which costs nothing because revocation is immediate.

**Revoking removes the row.** A revoked token is not kept as a revoked one: the
`401` a sender gets is the same whether the token was revoked this morning or
never existed, so a marked row would be a history answering no question the
product asks — and it would leave the encrypted secret of a dead credential in
the database for as long as the installation lives. A project may also be left
holding none at all, which is how an operator closes a project's door without
deleting the project.

The timestamp is kept to within **five minutes**, not to the second: a use writes
it only when the stored value is absent or older than that, because writing it on
every delivery is a database write per batch on the hottest path in the product,
bought for a precision this reading does not need
([ADR 0033](./adr/0033-the-last-use-of-a-token-is-written-coarsely.md)). The
first use always writes, so a token that was issued and never deployed stays
distinguishable from one that has gone quiet.

A sender presenting a revoked or unknown token is answered `401`, and by
`VISION.md`'s design it neither retries nor notices — it keeps writing its own
local file, which is where its logs were before logaffe existed. A rotation done
carelessly costs a gap in the central copy and nothing else, and that is the
whole reason delivery was made fire-and-forget.

### Why there is no token per sender

Three applications delivering into one project share its token, and revoking it
affects all three. The alternative — named tokens, issued and revoked
individually — buys a finer blast radius and costs a management surface with
names, a list and a lifecycle, in a product whose entire case is being small. An
operator who needs three applications separated has two better answers already:
give them separate projects, or leave them in one and tell them apart by their
`instance` ([Ingestion](./ingestion.md)).

## The retention window

A project keeps its entries for a number of days, counted from **receipt time**,
and time is the only limit there is — no size cap, no row quota, no interaction
between limits.

The number is the operator's, up to a **maximum of 90 days**
([ADR 0020](./adr/0020-retention-has-a-maximum.md)). Without a ceiling, a setting
box quietly turns logaffe into the multi-year archive `VISION.md` says it is
not, and the assumptions the rest of the product rests on — index sizes, the
volume the storage is tuned for, the self-repairing window in
[ADR 0005](./adr/0005-the-rendered-message-is-stored-not-recomputed.md) — stop
being true without anyone deciding that they should.

**Lowering it removes entries.** Before the change takes effect the operator is
told how many entries it will put outside the new window, because a settings
field that silently destroys data is a bad settings field. Raising it again
brings nothing back — what was swept is gone.

## The group

A project sits in **at most one group**, and a group is a name and the projects
that name it — one product's staging and production, one customer's
applications, one operator's idea of what belongs beside what. It exists so that
an installation holding twenty projects is a list an operator can read, and it
exists for nothing else.

**A group is for finding, never for asking.** It has no retention window, no
token, and no query of its own: a search still names one project exactly as it
did before there were groups ([Querying](./querying.md)), and the separation
`VISION.md` builds in is untouched by two projects being listed under the same
word. A group that could be queried would be a second kind of scope in a product
that has one.

It is nevertheless **a row with its own identity**, surviving every rename, and
not a word written on each project
([ADR 0039](./adr/0039-a-group-has-an-identity-and-holds-nothing.md)). Nothing a
group does today needs an identity. The identity is what makes adding something
to a group later an addition rather than a migration that has to invent one for
every string an installation in the field happens to hold.

Groups are **created by the operator**, like projects and for the same reason,
and they are managed where they are true of every project at once — the
installation's settings ([The web UI](./ui.md)). A project's own settings name
the group it is in and nothing more.

**A project is created into a group or into none**, and moved between them
afterwards. Creating a project and putting it where it belongs is one errand,
and a project that could only be grouped after the fact would make the operator
open the settings of something they had just made. Which of the two it is
decides the name it has to be free of: the group's projects, or the ones in no
group.

**A group may be empty**, both before its first project and after its last one
leaves. That follows from the identity: a group is something the operator made
rather than a side effect of what the projects say, so it stays until it is
removed.

**Deleting a group deletes nothing else.** Its projects are left in no group, the
act says how many that will be before it happens, and there is no name to type,
because there is nothing here that cannot be done again in a minute — the guard
on deleting a project is proportionate to entries that do not come back, and
wearing it here would say the two acts weigh the same.

**A group holds projects and never another group.** One level is what an
installation of ten to thirty projects has a use for, and a second one is a tree
in the interface, a path in every place a project is named, and a question about
what a nested group would inherit from the one above it.

## The host

A project sits on **at most one host** — the machine it runs on — which is the
same shape the group has and, deliberately, the same sentence
([Metrics](./metrics.md)). It exists so that the errors on this project's screen
can be read next to what the machine was doing at the time, and it exists for
nothing else.

**It is not a scope.** No query takes a host, no filter narrows by one, and two
projects named onto one machine are as separate as they were before — the rule
the group already carries, for the reason the group already carries it.

**A project on no host is the ordinary case** until the operator says otherwise.
It costs nothing except that there is no band to draw over its entries.

A project sitting on one host while running on two machines is a limitation this
accepts rather than solves, and [Metrics](./metrics.md#the-project-sits-on-at-most-one-host)
says what was traded for it.

The host is set in the project's own settings, beside the group, and the host
itself — its name, its token, its samples — is managed where it is true of every
project at once, in the installation's settings ([The web UI](./ui.md)).

## Deleting a project

Deleting is **immediate and irreversible**, and it is confirmed by typing the
project's name, which is the guard that fits an act that destroys data and cannot
be undone.

The confirmation is **the interface's, and the server sees nothing of it**. The
endpoint takes an identity and no typed name: repeating the name back would
protect nobody who issued the request deliberately, and it would make one route
answer to a rule none of the others do.

The project, its tokens and its visibility are gone at once; the entries are
removed afterwards, in the background
([ADR 0019](./adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)).
Senders holding its token get `401` from their next delivery and carry on writing
locally, exactly as they would through a botched rotation.

## Projects belong to the operator alone

Creating, renaming, deleting a project, issuing, rotating or revoking a token,
and everything there is to do to a group, are **operator acts that are not
reachable over MCP at all**
([ADR 0018](./adr/0018-projects-and-tokens-are-never-reachable-over-mcp.md)). The
agent reads entries and counts them; it cannot bring a project into existence,
end one, mint a credential, or move a project from one group to another — and
the same is true of every act on a host. It is told which group a project is in
and which host it sits on, because both are facts about the project it is reading
([MCP](./mcp.md)).

## What is deliberately not here

- **No implicit project creation.** Settled in `VISION.md`.
- **No per-sender tokens.** Covered above.
- **No ingest token that reads.** An ingest token writes to its project and does
  nothing else. Reading is a person with a session or an agent with an agent
  token ([MCP](./mcp.md)), and neither of those is issued per project.
- **No undelete, and no archive.** A deleted project is gone, and the product has
  no notion of a project that is kept but inactive.
- **No size or row quota on a project.** Time is the only limit.
- **No export or import of a project.** Backing an installation up is a database
  matter and `VISION.md` documents it as one.
- **No group that carries anything.** A group is a name: no retention window its
  projects inherit, no token, no colour, no icon, no description
  ([ADR 0039](./adr/0039-a-group-has-an-identity-and-holds-nothing.md)).
- **No group to query.** A search names one project, and putting two projects
  under one word does not make them one ([Querying](./querying.md)).
- **No nested groups, and no project in two of them.** Covered above.
- **No host to query, and no project on two of them.** A host is where a
  project's samples come from, never a way of asking about its entries
  ([Metrics](./metrics.md)).

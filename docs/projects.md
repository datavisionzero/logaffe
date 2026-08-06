# Projects and Tokens

The project is the unit everything else hangs off: an entry belongs to one,
retention is configured on one, a query runs inside one, and a token admits a
delivery to one. `VISION.md` builds multi-project capability in from the start
rather than retrofitting it, and this is what a project actually is.

## What a project is

A **name**, a **retention window**, and its **ingest token**. Nothing else.

The name is unique within an installation and can be changed at any time. Two
projects called `api` is a trap for the operator who reaches for one of them at
three in the morning, and the uniqueness is there for that rather than for any
technical reason. A project is identified by an identity that survives every
rename, and that identity is what entries, tokens and queries are attached to —
never the name.

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
It carries a recognizable prefix, which costs nothing and means an accidental
appearance in a repository or a log is something a scanner can find. That second case is not
hypothetical: applications log the configuration they started with, and a token
that ends up in a log entry ends up here.

### Rotation, and knowing when it is done

Every token records **when it was last used**. Without that, rotation is
guesswork: the operator issues a new token, rolls the deployments over, and then
has to decide whether the old one is still feeding something they forgot. With
it, rotation is finished when the old token's last-used stops moving.

The sequence is therefore: issue the second token, move the applications over,
watch the old one go quiet, revoke it. Revocation takes effect immediately.

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

## Deleting a project

Deleting is **immediate and irreversible**, and it is confirmed by typing the
project's name, which is the guard that fits an act that destroys data and cannot
be undone.

The project, its tokens and its visibility are gone at once; the entries are
removed afterwards, in the background
([ADR 0019](./adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)).
Senders holding its token get `401` from their next delivery and carry on writing
locally, exactly as they would through a botched rotation.

## Projects belong to the operator alone

Creating, renaming, deleting a project, and issuing, rotating or revoking a
token, are **operator acts that are not reachable over MCP at all**
([ADR 0018](./adr/0018-projects-and-tokens-are-never-reachable-over-mcp.md)). The
agent reads entries and counts them; it cannot bring a project into existence,
end one, or mint a credential.

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

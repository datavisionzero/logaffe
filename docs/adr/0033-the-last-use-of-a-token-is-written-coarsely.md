# The Last Use of a Token Is Written Coarsely

Authenticating a token records that it was used, and that record is written back
only when the stored value is **absent or more than five minutes old**. Writing
it on every use is an `UPDATE` per batch, taken on the hottest path in the
product and immediately in front of the `COPY` that is the actual work
([ADR 0003](./0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)) — a
row-level lock, a WAL record and a dead tuple for every delivery, on a row whose
purpose is to be read by one human, occasionally. The alternative that keeps full
precision without the writes is an in-memory buffer flushed in the background,
and it costs a background component, a flush on shutdown, and a timestamp that
loses its last few minutes whenever the container is replaced — which is exactly
the moment an operator is most likely to be watching one.

Five minutes is chosen against what the timestamp is *for*: `docs/projects.md`
makes it the thing that says a rotation is finished — the old token has gone
quiet — and [ADR 0021](./0021-an-agent-token-is-a-copied-secret.md) makes it the
field that turns "which of these agent tokens can I revoke" into a reading. Both
are questions answered in hours and days. Nothing in the product asks when a
token was used to the second, and nothing should be built that does without
reopening this.

## Consequences

**The first use always writes.** A stored value of null is what tells a token
that was issued and never deployed apart from one that has gone quiet, so it is
never the case that is skipped, and rolling a new token out is visible in the
product as soon as the first batch lands.

**The stored value is a lower bound, stale by at most five minutes.** It is
accurate enough to say "this token is live" and "this token stopped", which is
all that is asked of it. What shows it must not render it as though it were
precise to the second — a relative form is the honest one, and it is what
`docs/ui.md` shows anyway.

**A token used continuously writes at most twelve times an hour**, whether it is
carrying one delivery a minute or a thousand a second. The write stops scaling
with traffic, which is the whole point: an installation under load pays for its
entries and not for its credentials.

**Nothing is buffered, so nothing is lost on restart.** The write is part of the
request that earned it and is committed with it. A container replaced between two
deliveries costs the five minutes the interval already allows and never more.

**Two deliveries at once may both write, and the later value wins.** The domain
only ever moves the timestamp forward, so an out-of-order pair cannot make a
token look quieter than it is, and two threads agreeing to write within the same
millisecond is a cost paid by the two requests that were going to write anyway.

**The interval is a product value, not a setting.** It sits with the batch limits
of `docs/ingestion.md`: the same in every installation, documented, and not
something an operator is asked to have an opinion about.

**A failed authentication writes nothing at all.** Only a token that matched
records a use, so the timestamp stays a statement about the credential rather
than about who has been guessing at it.

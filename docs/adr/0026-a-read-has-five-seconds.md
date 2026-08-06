# A Read Has Five Seconds

Every query on the read surface is cut off after five seconds, in the web UI and
over MCP alike. It is one value for every kind of read, it is the same in every
installation, and there is no setting that raises it — the same standing the four
limits on the ingestion path already have, which `docs/ingestion.md` calls
product values rather than something the operator tunes. The write path carried
documented limits from the start and the read path carried none, and both of them
are reachable from the open internet.

**Five comes from the live tail rather than from caution.** `VISION.md` refreshes
a following view every five seconds or so, and a read that takes longer than the
interval which refreshes it has already stopped being an interface. A number
derived from the product is one the operator can be told the reason for, which is
the same standard [ADR 0025](./0025-a-search-text-is-at-least-three-characters.md)
was held to.

**It is a bound on what was not measured.** ADR 0025 closed the one unbounded
read that a measurement found. Everything else measured at ten million entries
came back in under a third of a second, so five seconds is fifteen times the
slowest known query and will never be met by an ordinary one. Two candidates are
already known and neither has been measured: a three-character search whose
single trigram is a common one, and a count over the retention ceiling with no
time range narrowing it.

## Consequences

**The count is the operation most likely to meet it**, because it is the only one
that cannot stop early — a page stops at its limit, a count has to visit every
match. Giving counts a longer limit of their own was considered and rejected: a
count already has the time range to narrow itself with, and two limits are two
rules where the product can have one.

**An expiring read has to say what to narrow.** Reporting that a statement timed
out names the mechanism and not the remedy. The agent meets the same limit and
the same explanation, as structured data rather than prose
([ADR 0012](./0012-log-content-reaches-an-agent-as-data-never-as-prose.md)).

**This is not a fairness mechanism.** With one operator there is no contention to
arbitrate; the limit bounds a single expensive query, and its purpose is that a
publicly reachable installation cannot be occupied by one request.

**It binds the read surface and nothing else.** The retention sweep, which
legitimately runs for minutes, is not a read and is not held to it.

**A legitimate query that cannot finish in five seconds is a finding about the
schema, not a reason to raise the number.** That is the same posture
[ADR 0020](./0020-retention-has-a-maximum.md) takes toward its ceiling: the limit
is what keeps the assumptions underneath the product true, so the thing that has
to give is the design, not the limit.

# Retention Deletes Rows Rather Than Dropping Partitions

Expired entries are removed by a background job deleting rows in bounded
portions, not by partitioning the entry table on time and dropping the oldest
partition. Partitioning is the textbook answer for a log store and it is the
better one when every row in a partition expires together — dropping a partition
is near-constant work, returns the space to the operating system, and takes the
indexes with it instead of churning them. It does not fit here because
**retention is configured per project**: a partition can only be dropped once
everything inside it has expired, so a project keeping entries for seven days
would keep them for up to ninety while sharing partitions with a project that
does. That is a broken promise rather than a tuning detail, and the repair —
partitioning by project and then by time — is on the order of a partition per
project per day, which is a great deal of machinery for a product whose case is
being small enough to reason about.

## Consequences

**Autovacuum has to be configured for this table rather than left on defaults.**
Its default trigger waits until a fifth of a table is dead, which is the wrong
shape for one where a predictable fraction expires daily. Reclaimed space is
reused by incoming entries rather than handed back to the operating system, and
in steady state that is correct: the table settles at roughly what the retention
window implies and stays there.

**Index maintenance is the real cost, not heap space.** The trigram index of
[ADR 0010](./0010-search-is-a-substring-match-not-a-full-text-query.md) is the
largest thing in the database after the entries, and a GIN index under continuous
insert-and-delete churn is the part of this design most likely to need attention
in production. A partitioned design would have avoided it entirely by dropping
indexes with their partition; that advantage is knowingly given up.

**A one-off shrink leaves the table file oversized.** Lowering a project's window
or deleting a large project frees far more space at once than ordinary traffic
will refill, and only a deliberate `pg_repack` or `VACUUM FULL` returns it. This
is documented as an operator's occasional act rather than something the product
attempts on its own, because both of them trade availability for disk and only
the operator knows when that trade is acceptable.

If this is reopened, the thing that has to change first is per-project retention.
As long as two projects in one installation can keep entries for different
lengths of time, a partition has no single moment at which it is expired.

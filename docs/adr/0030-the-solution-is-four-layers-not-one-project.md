# The Solution Is Four Layers, Not One Project

The backend is `Logaffe.Domain`, `Logaffe.Application`, `Logaffe.Infrastructure`
and `Logaffe.Api`, with dependencies pointing inward and the compiler holding
them there. The obvious alternative is one project with folders named after
features, and for a product whose entire case is being small it is a serious
one: four projects for a single-operator log store is a great deal of structure,
and every feature touches three or four of them.

What decided it is not convention but a promise already made elsewhere.
[Querying](../querying.md) gives the operator and the agent **one** read surface,
on the grounds that two surfaces over the same data drift apart and the
difference is discovered by whoever is debugging at the time. Layering turns that
from a matter of discipline into a compile unit: `SearchEntries` is one type in
Application, the HTTP endpoint and the MCP tool are two adapters over it in Api,
and neither can reach the database on its own to grow a capability the other
lacks. The CLI verbs of [Setup](../setup.md) and
[Operations](../operations.md) are a third adapter over the same use cases, which
is what keeps Host Recovery and a backup from becoming their own routes into the
data.

## Consequences

**The risk taken on is an anemic domain** — four projects in which `Logaffe.Domain`
holds nothing but data and every rule lives a layer up. The guard is a rule that
can be checked by reading: **anything the documents already state as a rule
belongs in Domain.** The three-character minimum of
[ADR 0025](./0025-a-search-text-is-at-least-three-characters.md), the ninety-day
ceiling of [ADR 0020](./0020-retention-has-a-maximum.md), the substitution rule
that is deliberately narrower than Serilog's
([ADR 0004](./0004-the-ingestion-format-is-clef-and-the-server-renders.md)), the
truncation of [ADR 0008](./0008-an-over-long-message-is-truncated-not-refused.md),
and the well-formedness a trace id has to have before it is promoted
([Storage](../storage.md)) are all rules of that kind. If they end up in
Application, the innermost project is ballast and this decision was not worth
its cost.

**Both idioms of [ADR 0003](./0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)
live in Infrastructure**, and the boundary between them stays where that ADR drew
it — at the table, not at a feature. Application knows a writer port and a reader
port; it does not know that one is Npgsql's binary `COPY` and the other
hand-written SQL fitted to the indexes of [Storage](../storage.md). That buys the
use cases a database they can be tested without, and it costs one indirection
between an index changing and the SQL that has to be re-read. ADR 0003 names that
re-reading as a standing obligation, so the SQL is kept together in one folder
rather than distributed over repository classes: an index change has to be one
place to look.

**A new filter touches every layer** — a value object in Domain, a field on the
filter in Application, a clause and an index in Infrastructure, a parameter in
two adapters in Api. That is the recurring price, it is paid on exactly the kind
of change [ADR 0011](./0011-filters-only-narrow-and-only-with-and.md) says is the
one worth reopening, and it is the strongest argument the single-project
alternative had.

If this is reopened, the split to reopen is Domain against Application.
Infrastructure earns its separation from the two data-access idioms and Api earns
its own from the three adapters over one set of use cases; the innermost boundary
is the one whose keep is a judgement rather than a mechanism.

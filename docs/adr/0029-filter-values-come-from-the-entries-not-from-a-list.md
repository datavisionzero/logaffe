# Filter Values Come From the Entries, Not From a List

The instance, the logger name and the trace are narrowed by clicking the value on
an entry that is on the screen, or by typing one. Nothing in the product answers
*which logger names does this project have*, so there is no dropdown beside the
filter bar and no tool for it over MCP.

The alternative is what every log viewer offers: a facet list of distinct values,
usually with the number of entries behind each. It was rejected because of what
answering it costs on the largest table in the database. `select distinct
logger_name where project_id = …` is an index-only scan over every key in the
logger name index — 1.39 GiB per ten million entries
([Storage](../storage.md)) — and Postgres does not turn that into one probe per
distinct value; the loose index scan that would is not something it performs for
this shape. That would be a second scan-shaped operation in a product that has
exactly one ([ADR 0028](./0028-the-exception-is-its-own-filter.md)), and unlike
that one it would run whenever a view opens rather than when somebody asked a
question with it. The cost has not been measured, and by the standard
[ADR 0027](./0027-repeated-text-is-stored-not-interned.md) sets, an unmeasured
number is not a reason to build the thing — it is a reason not to.

The maintained variant — a table of the distinct values per project, kept current
as entries arrive — is worse for a reason already decided. It puts a lookup and a
conditional insert back on the ingestion path, which is the stateful write step
ADR 0027 refused, arriving through another door for a smaller prize.

It is also not a UI convenience but an operation on the shared query surface.
[Querying](../querying.md) gives the operator and the agent the same one, so a
value list is a new tool for the agent as well, added on the strength of a
control the operator wanted.

What replaces it is that the value is already on the screen. An operator narrows
to a logger because they are looking at an entry from it, and that narrowing sits
on the line in front of them. A logger name no entry in front of them came from
is one they would not have picked out of a list either.

## Consequences

**A project cannot be explored by opening a menu.** What it contains is learned
by looking at its entries, which is the reading the product is built around
anyway.

**A typed filter value can match nothing**, and the view cannot distinguish a
misspelling from a value whose entries are outside the time range. It says the
filters matched nothing and names them, which is the same answer either way.

**The agent works the same way**: it reads entries and narrows by what it found
in them, rather than enumerating a project first.

Reopening this means measuring the distinct scan before building anything, and it
means adding an operation to the query surface rather than a control to the web
UI.

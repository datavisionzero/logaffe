# Filters Only Narrow, and Only With AND

A query is a set of filters that each remove entries and that all apply together.
There is no `OR`, no negation, no grouping, and no query language. The
alternative was an expression syntax of the kind log tools usually grow, and it
was rejected for what it costs beyond the parser: a grammar to document, errors
to render, a surface that never stops growing, and — because the agent is a
first-class consumer here — an endless supply of queries that parse and ask
something other than what was meant.

## Consequences

Two questions needing an `OR` between them are two queries. On a store that is
bounded by design that is a cheap answer, and it is the right trade for a product
whose whole case is being small enough to reason about.

The filters map onto MCP tool parameters without translation, which is what keeps
the agent and the operator on one surface rather than two that drift. It is also
what makes an agent's query legible to the operator afterwards: a set of named
narrowings reads the same to both of them, where an expression would have to be
understood before it could be checked.

If this is ever reopened, the thing to reopen is the filter list, not the
combinator. Another narrowing is an addition; `OR` is a grammar.

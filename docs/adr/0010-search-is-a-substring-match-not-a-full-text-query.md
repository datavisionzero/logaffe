# Search Is a Substring Match, Not a Full-Text Query

The search text is matched as a case-insensitive substring of the rendered
message, served by a trigram index, rather than as a word query against
PostgreSQL's full-text search. Full-text search is the obvious choice on this
database and it is built for prose, which logs are not: it tokenizes on
punctuation, so an address, a URL path and a container name come apart into
pieces the operator did not search for, and it matches whole stemmed words, so
`nullreference` never finds `NullReferenceException`. The searches people
actually type into a log tool are fragments of identifiers, and a substring match
is what finds them.

## Consequences

A trigram index is substantially larger than a full-text index on the same
column, and it is the second-largest thing this product stores after the entries
themselves. That is affordable only because volume is capped and retention is
short, and it is another place where the product leans on being deliberately
bounded.

A search under three characters cannot use the index and falls back to a scan.
This was originally allowed rather than refused, on the grounds that a minimum
length is a rule the operator has to learn at the moment they are busy;
[ADR 0025](./0025-a-search-text-is-at-least-three-characters.md) reverses that
after measuring what the scan costs.

**Properties are searchable only through the rendered message.** A value that a
placeholder collected is in the sentence and is found; a property attached by an
enricher and never rendered is stored, displayed, and not searchable. Adding an
equality filter over the properties was considered and rejected: it means a
further index on the largest table and a ruling on whether `42` and `"42"` are
one filter value, for a question the rendered text usually already answers.

There is no ranking. A substring either occurs or it does not, results come back
newest first like every other query, and the product has no notion of a better
match.

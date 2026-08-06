# The Ingestion Format Is CLEF, and the Server Renders

The ingestion endpoint takes newline-delimited [CLEF](https://clef-json.org/),
the format `Serilog.Formatting.Compact` already writes, rather than a schema of
logaffe's own. Every decision this product had made about an entry independently
described CLEF — a message template that is also the plain-text case, an absent
level meaning `Information`, the exception as its own string, properties as bare
keys — so inventing a near-identical schema would have bought a set of field
names and cost the sink a mapping layer it does not otherwise need. Adopting it
makes the Serilog sink a formatter configuration, and it gives an application
already shipping to Seq a change of URL rather than a change of logging.

## Consequences

**`@m` is refused rather than ignored.** CLEF permits a pre-rendered message
alongside the template, and accepting one would put rendering in two places and
raise the question of which wins when they disagree. Refusing the entry keeps a
single answer: the sender supplies `@mt`, the server renders.

**A plain line is a template without holes**, which is what lets the `curl` case
stay one field and no syntax. And because a plain line arrives as a template,
**rendering has to be narrower than Serilog's**: logaffe substitutes only
placeholders whose property was actually delivered, and leaves every other brace —
including the `{{` that Serilog reads as an escape — exactly as it arrived. Log
content is untrusted and routinely contains text an application never wrote; the
ordinary escaping rule would silently rewrite it, and this product stores lines
as they were delivered.

The cost is a third-party convention in the product's most public contract. If
CLEF changes in a direction logaffe does not want, the format is logaffe's to
freeze, because what is adopted here is the shape of a document and not a
dependency.

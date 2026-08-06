# The Rendered Message Is Stored, Not Recomputed

The message is rendered once when the entry is ingested and written to its own
column, where the full-text index sits, in addition to the template and the
properties it came from. Rendering on read was the alternative and it stores
less, but the sentence a person searches for is the sentence they saw on screen,
and that sentence exists nowhere in a database holding only
`User {UserId} failed login from {Ip}` and `203.0.113.7` in separate places.
Searching a template and a set of property values separately answers a different
question than the operator asked, and this is a product whose main verb is
search.

## Consequences

The message is stored twice in different forms, which is the largest single
contributor to the size of the log table and is accepted knowingly on a product
that caps volume and keeps retention short.

**A change to the rendering rules never reaches rows that already exist.** The
stored text is a fact about how the entry was rendered on the day it arrived, so
a correction applies from that day forward and the table briefly holds two
generations of rendered text that a search will mix.

That window is bounded by something this product decided already: **retention is
short**, so the old generation ages out on its own and the inconsistency repairs
itself without anyone rewriting a table. The same decision in a product holding
years of history would be considerably worse, and it is worth knowing that this
one leans on the retention window to make it cheap.

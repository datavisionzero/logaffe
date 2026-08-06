# Retention Has a Maximum

A project's retention window is the operator's to set, up to a ceiling of 90 days
that no installation can raise. `VISION.md` states that logaffe is not designed
for multi-year archives or billions of rows and calls limited retention a
deliberate constraint that keeps the system simple to run — and a settings field
without a ceiling turns that from a property into a hope. The product's own
decisions rest on the window being short: the storage is tuned for a bounded
data set, the trigram index in
[ADR 0010](./0010-search-is-a-substring-match-not-a-full-text-query.md) is
affordable only because of it, and the rendering inconsistency in
[ADR 0005](./0005-the-rendered-message-is-stored-not-recomputed.md) repairs
itself only because old rows age out.

## Consequences

An operator wanting a year of logs cannot have one, and will not be given a
reason beyond this decision. That is the point of writing it down: the answer is
that they want a different kind of product, and logaffe declining to become it
badly is better than becoming it badly.

Because the ceiling is what several other decisions lean on, raising it is not a
number change. Anything that moves it has to revisit the index sizing, the volume
the storage is tuned for, and the assumption that a rendering change washes out
of the store on its own.

**Lowering a project's window removes entries that fall outside the new one**,
and the operator is shown how many before the change takes effect. A field that
destroys data on a keystroke without saying how much is one the operator learns
to fear, and the count is cheap here because counting is already a capability the
product has.

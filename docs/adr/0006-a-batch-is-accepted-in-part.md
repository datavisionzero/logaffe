# A Batch Is Accepted in Part

A delivery containing invalid entries stores the valid ones and counts the rest,
instead of refusing the batch. The usual argument for all-or-nothing is a clean
contract, and it assumes a client that reads the answer and tries again — but
delivery here is fire-and-forget by design, so the sender neither retries nor
looks. Refusing a thousand-entry batch over one malformed line would therefore
discard 999 good entries permanently, and silently, for a defect that is
typically one field in one code path of the sending application.

## Consequences

The response is `200` with the accepted and rejected counts and the first few
reasons against their line numbers. Nothing in a sender depends on it; it exists
for a person wiring up a new integration with `curl`, which is also the only
moment anyone reads it. A whole batch is still refused where no part of it can be
trusted or afforded — a bad token, a size limit, an exhausted quota, or a store
that cannot be reached.

Malformed entries are counted rather than kept. Storing them as raw lines was
considered and rejected: it would put a second kind of row into the table that
the UI, the search and MCP would each have to handle, for data whose sender is
under the operator's own control and can be fixed.

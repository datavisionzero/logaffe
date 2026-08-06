# The Tail Follows the Receipt, the View Keeps the Order of Events

The live view polls for what has arrived since its last poll — a cursor on
**receipt time** — while continuing to order what it holds by **event time**. The
obvious implementation runs the cursor on the same clock the view sorts by, and
it silently loses data: a sender that was disconnected delivers entries whose
event times are older than what the tail has already displayed, so a cursor on
event time never returns them. The entries are stored, and a later search finds
them, but the operator watching the outage happen sees nothing — which is the one
moment the live view exists for.

## Consequences

Entries can appear below the newest line rather than at the top, and that is the
correct behaviour rather than a glitch: a late delivery belongs where it
happened, not where it arrived. A view that inserted them at the top would be
claiming a sender's two-minute-old entry is newer than one from a second ago.

The cursor needs the entry's identity as a tiebreaker, because a batch is
received in one act and its entries can share a receipt time, and a cursor that
is not total either repeats a row or skips one.

This is [ADR 0007](./0007-the-sender-orders-the-receipt-expires.md) applied a
second time. Retention counts from the receipt because a sender cannot be trusted
with a clock; the tail follows the receipt for the same reason, and the display
keeps the sender's order in both cases because that is the order things happened
in.

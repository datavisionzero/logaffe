# Architecture Decisions

This directory contains decisions that are difficult to reverse, would be
surprising without context, and resulted from a genuine trade-off. Product scope
and promises belong in the [product vision](../../VISION.md) instead, which also
carries the roster of the technical direction — an entry there names what was
chosen, an ADR here explains why the obvious alternative was not.

## Naming

ADRs are numbered sequentially as `NNNN-short-slug.md`. The next number follows
the highest existing number.

## Short form

```md
# Short decision title

One to three sentences describe the context, decision, and rationale.
```

Status, considered options, and consequences are included only when they add
material value to understanding the decision.

## Decisions

- [0001 – The frontend is React, not Blazor](./0001-the-frontend-is-react-not-blazor.md)
- [0002 – logaffe logs to files, not into itself](./0002-logaffe-logs-to-files-not-into-itself.md)
- [0003 – EF Core owns the schema, the log path goes around it](./0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)
- [0004 – The ingestion format is CLEF, and the server renders](./0004-the-ingestion-format-is-clef-and-the-server-renders.md)
- [0005 – The rendered message is stored, not recomputed](./0005-the-rendered-message-is-stored-not-recomputed.md)
- [0006 – A batch is accepted in part](./0006-a-batch-is-accepted-in-part.md)
- [0007 – The sender orders, the receipt expires](./0007-the-sender-orders-the-receipt-expires.md)
- [0008 – An over-long message is truncated, not refused](./0008-an-over-long-message-is-truncated-not-refused.md)
- [0009 – The tail follows the receipt, the view keeps the order of events](./0009-the-tail-follows-the-receipt-the-view-keeps-the-order-of-events.md)
- [0010 – Search is a substring match, not a full-text query](./0010-search-is-a-substring-match-not-a-full-text-query.md)
- [0011 – Filters only narrow, and only with AND](./0011-filters-only-narrow-and-only-with-and.md)
- [0012 – Log content reaches an agent as data, never as prose](./0012-log-content-reaches-an-agent-as-data-never-as-prose.md)
- [0013 – Host Recovery returns the installation to unclaimed](./0013-host-recovery-returns-the-installation-to-unclaimed.md)
- [0014 – The claim is atomic and holds nothing](./0014-the-claim-is-atomic-and-holds-nothing.md)
- [0015 – The operator has no username and no email](./0015-the-operator-has-no-username-and-no-email.md)

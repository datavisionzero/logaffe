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

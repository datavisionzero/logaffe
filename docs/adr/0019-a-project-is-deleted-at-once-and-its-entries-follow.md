# A Project Is Deleted at Once and Its Entries Follow

Deleting a project removes the project, its tokens and its visibility
immediately, and its entries are swept afterwards in the background. Doing it in
one transaction was the alternative and it means a request that stands for
minutes while millions of rows are removed, holding the database busy at whatever
moment the operator happened to pick — and other projects are still receiving
deliveries throughout. Splitting it keeps the act instant where the operator is
standing and puts the expensive part where nothing is waiting on it.

## Consequences

There is a window in which entries exist for a project that does not. Nothing can
reach them: queries run inside a project, the token that admitted them is gone,
and the agent has no path to a project it cannot name. The rows are unreferenced
data on their way out rather than a state the product exposes, and no part of the
system has to describe a half-deleted project because none is ever shown one.

Deletion is **irreversible**, confirmed by typing the project's name. A grace
period was considered and rejected: it keeps data the operator believes is gone,
and it forces an answer to what a sender delivering against a deleted-but-
restorable project should experience. Logs are expendable by design here —
`VISION.md` makes them additive to the applications' own local files — so the
cost of a mistaken deletion is real but bounded, and the confirmation is
proportionate to it.

A sender holding the token of a deleted project is answered `401` and, being
fire-and-forget, keeps writing locally without noticing. That is the same
experience as a rotation done carelessly, which means there is one behaviour to
understand rather than two.

# An Over-Long Message Is Truncated, Not Refused

A message or exception exceeding its size cap is cut at the cap and the entry is
flagged as truncated, rather than the entry being rejected. This is the single
modification logaffe makes to stored data, and `VISION.md` names it as the one
exception to "log lines are stored as delivered". It is recorded here because the
alternative is the obvious reading of that sentence and would select precisely
the wrong victims: the entries that overrun a cap are the four-megabyte stack
traces and the dumped payloads, which is to say the entries an operator is most
likely to have gone looking for.

## Consequences

The truncation is visible rather than silent. The entry carries a flag, and the
UI and MCP both say that the text is cut, so nobody reads a shortened stack trace
as a complete one — a truncation an operator cannot see would be worse than the
rejection this decision avoids.

The caps stay generous enough that truncation is rare in normal operation, and
`docs/ingestion.md` fixes them as product values rather than operator settings.
The vision's sentence keeps its force everywhere else on this path: nothing is
scrubbed, classified, reformatted or dropped for its content, and this remains
the only place where what is stored differs from what arrived.

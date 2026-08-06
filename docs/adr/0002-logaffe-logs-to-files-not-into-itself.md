# logaffe Logs to Files, Not Into Itself

logaffe writes its own logs with Serilog to rolling files on the mounted host
volume, next to the configuration and secrets that `VISION.md` already keeps
outside the container. Self-ingestion is the obvious move for a logging product
and is rejected on the merits: the failures worth diagnosing are the ones where
logaffe is the thing that broke — the database unreachable, a migration failing
at startup, the ingestion endpoint refusing batches — and in exactly those
moments a self-ingesting installation records nothing, so the log of the outage is
the log that is missing. A file needs no database, no claimed installation, no
project and no ingest token, which is also why it is the only sink available
during startup and during the guided claim. Serilog rather than a bare
`ILogger` file sink because logaffe ships a Serilog sink as its own ingestion
package: the product uses the library it asks its users to use.

## Consequences

A **second** logaffe installation may later be added as an additional sink, and that
is the additive model the product sells — an application keeps its local file
logging and delivers to logaffe on top. It is an addition, never a replacement:
the file log stays, because it is what survives the failure that takes the
network sink with it. Nothing is owed to the operator's backup plan either, since
logaffe's own log is expendable in the same way user logs are, and `VISION.md`
already says a backup covering only the account and the configuration is a
legitimate choice.

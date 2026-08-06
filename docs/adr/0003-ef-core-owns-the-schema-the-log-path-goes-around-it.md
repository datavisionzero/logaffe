# EF Core Owns the Schema, the Log Path Goes Around It

One table dominates this product and the rest of the database is small. The
operator account, the projects, the ingest tokens and the settings are
low-traffic relational rows that EF Core handles well, and its migrations are
what let `VISION.md` promise that a schema upgrade applies itself on startup with
no step for the operator to run. The log entry table is the opposite in every
respect: rows arrive as batches from the ingestion endpoint, are never updated,
and are read back as filtered, time-ordered, paginated scans that the web UI
polls every few seconds. Change tracking is pure overhead for a row that will
never change, row-by-row `INSERT` loses to `COPY` by a wide margin, and the read
shape has to be hand-fitted to the indexes the vision already calls for. So EF
Core owns the schema — including the log table's — and everything except the log
path, while the log path uses Npgsql's binary `COPY` for ingestion and Dapper for
the queries.

## Consequences

There are two data-access idioms in one codebase, and the boundary between them
is drawn at the table rather than at a feature, so it stays easy to state:
anything touching log entries in volume is SQL, everything else is EF Core. The
log table is nevertheless declared in an EF Core migration like every other
table, so there remains exactly one place that creates schema and exactly one
mechanism that upgrades it. The hand-written SQL is the part that pays for the
storage tuning the vision asks for, and it is also the part that has to be
re-read whenever an index changes.

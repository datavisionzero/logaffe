# The Installation Dumps Its Own Database

[ADR 0024](./0024-a-backup-is-one-artifact-holding-both-halves.md) settles that a
backup is one artifact holding the database and the key material together. It
does not settle how the database gets into it, and the shape of the container the
command runs in makes that a real question: the runtime image is
`dotnet/aspnet:10.0-alpine`, it carries no Postgres tooling, the database is a
separate container reachable only over the network, and the process runs as
non-root.

The obvious answer is to put `postgresql-client` in the image and shell out to
`pg_dump`. It is the well-trodden path, the format is one every operator already
knows, and `pg_restore` means the replay is somebody else's problem. logaffe does
not do that. It dumps its own database through Npgsql with binary `COPY TO`, one
table at a time — the mirror of the `COPY` writer the ingest path already has,
so this is not a new kind of thing in this codebase
([ADR 0003](./0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)).

What the obvious answer costs is a version coupling in the wrong place. `pg_dump`
has to be at least as new as the server it reads, so the runtime image would
carry a client whose version has to track the database's, and upgrading Postgres
would become a question about the logaffe image. The two are chosen and moved
independently — the operator's Compose file names the database image — and
binding them through a tool used once per backup buys convenience with a
constraint that shows up years later and is unpleasant to unpick.

## Consequences

**The format is ours, so the replay is ours.** There is no `pg_restore` to lean
on, and a restore has to reproduce the table order, the foreign keys and the
schema the dump was taken against. That is a standing obligation on the one path
where being wrong loses everything, and it is why the round trip — take an
artifact, restore it, find the installation the operator had — is the test that
matters rather than one that checks a file was written.

**Every schema change now has two places to land.** A column added to a table is
also a column the dump and the replay have to carry, and nothing in the compiler
will say so. This is the cost that was accepted; the alternative was a
constraint on the image, and this one is at least visible in the repository
rather than in a base image nobody rereads.

The artifact carries the migration id it was taken at, which is what makes
ADR 0024's refusal — a restore into an older logaffe is refused rather than
attempted — something a command can actually check. It is the same comparison the
installation makes at startup against a database migrated by a newer build, and
it is worth being one piece of code used twice.

Entries are the bulk of any installation and are excludable, which stays true
here: a dump that omits `log_entry` is the small, slow-changing half an operator
may legitimately choose, and it is the half whose loss cannot be shrugged off.

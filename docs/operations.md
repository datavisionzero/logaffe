# Operating an Installation

Self-hosted software is only as good as its operational story, so upgrading,
backing up and knowing whether the thing is alive are part of the product.
`VISION.md` fixes the shape: Docker Compose is the standard way to run it,
migrations apply themselves, and backup is the operator's responsibility while
being simple to do and clearly documented.

## What lives where

Two stores, and both are needed.

- **The database** holds projects, tokens, entries and the operator's account.
- **The host volume** holds the configuration and the secrets — including the
  **encryption key** that makes the stored tokens readable
  ([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)).

Nothing lives inside the container image. A container can be destroyed and
recreated without losing anything, which is what makes an upgrade a `pull` and an
`up`.

**Neither store is useful without the other.** A database restored without its
key is an installation whose every ingest and agent token is undecryptable, and
the operator discovers it at the moment they go looking for one. This is the
trap the backup command below exists to remove.

## Backup

**logaffe does not run backups, schedule them, or ship them anywhere.** That is
`VISION.md`'s position and it stands. What it does provide is a way to take one
that cannot be taken half-right:

```
docker compose exec logaffe logaffe backup > logaffe-backup.tar
```

The artifact contains **both halves** — the database and the key material — and
the operator decides when to run it, where to put it, and how long to keep it. A
scheduler, a destination and a retention policy for backups are all still theirs;
what is not theirs is the chance to back up one half and believe they are covered.

**Not everything is equally worth saving.** Entries are expendable: they are
short-lived by design, they are additive to the applications' own local files,
and losing them costs little. The operator's account, the configuration and the
tokens are not — losing those means losing the installation. An operator who
backs up only the small, slow-changing part is making a legitimate choice, and
the command supports it.

**Restoring** puts both halves back and starts a version **no older than** the one
that produced the artifact. Restoring into an older logaffe is refused rather
than attempted, for the same reason a downgrade is: the schema has moved and the
code behind it has not.

## Upgrades

`docker compose pull`, then `docker compose up`. Schema migrations run on
startup, and there is no separate step and no sequence to follow between
versions — an installation two years behind catches up in one start.

Three things make that safe to promise:

- **Migrations take a lock.** Two containers starting at once — during an
  upgrade, or because something restarted them — do not migrate against each
  other; the second waits and then finds nothing to do.
- **A newer schema than the code is refused.** Starting an old image against a
  database a later version already migrated stops with a clear message instead of
  running queries against a shape it does not know. There is no downgrade path:
  going back a version means restoring a backup.
- **A failed migration stops the installation.** It does not start half-migrated
  and it does not serve requests. The container exits with the failure in its own
  log, which is where every other failure of this kind is already written
  ([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md)).

## Retention as a running job

Retention deletes rows. A background job removes entries whose **receipt time**
has fallen outside their project's window, in bounded portions rather than one
statement, so it never holds a long transaction across a table other projects are
still being written to.

Deleting rows rather than dropping time partitions is the deliberate choice, and
the reason is that **retention is per project**. A partition can only be dropped
once everything inside it has expired, so a project keeping entries for seven days
would hold them for up to ninety while sharing partitions with a project that
keeps them that long — a broken promise rather than a tuning detail. Fixing that
means partitioning by project *and* time, which for thirty projects across ninety
days is a great deal of machinery for a product whose case is being small.

Two consequences follow, and both are operational rather than theoretical.

**Autovacuum is configured for the entry table specifically.** Its defaults wait
until a fifth of a table is dead, which is the wrong shape for one where a steady
fraction expires every day. Space reclaimed this way is reused by incoming
entries rather than returned to the operating system, which is exactly right in
steady state: the table settles at roughly the volume the retention window
implies and stays there.

**A one-off shrink is the exception.** Lowering a project's window from ninety
days to seven, or deleting a large project, frees a great deal of space at once
that ordinary operation will only refill slowly. The table file stays as large as
it was. That is the situation — and the only one — where `pg_repack` or a
maintenance window with `VACUUM FULL` is worth the trouble, and it is a thing an
operator does deliberately rather than something the product does on its own.

## Health

One unauthenticated endpoint answering `200` or `503`, and nothing else. No
version, no migration state, no database detail, no uptime.

It is public because a Compose healthcheck and a reverse proxy both need to reach
it without credentials, and it says nothing because it sits on the open internet.
A stranger learns from it exactly what they would learn by loading the sign-in
page, which is that a logaffe is here.

It reports ready when the database is reachable and migrations are complete —
during a long migration on a large installation the answer is `503`, which is the
honest one, since nothing can be served yet.

## Sizing the disk

The store is bounded by the retention window and the delivery rate, so its size
is predictable and the operator should be given the arithmetic rather than a
shrug: entries per day, times the retention window, times the size of an entry —
which the documentation states with the indexes counted, because the trigram
index of [ADR 0010](./adr/0010-search-is-a-substring-match-not-a-full-text-query.md)
is the second-largest thing in the database and leaving it out of the estimate
would understate it badly.

logaffe does not enforce a disk limit and does not stop ingesting to protect one.
There are no size quotas anywhere in this product, and adding one here would be
the "drop oldest when full" interaction that `VISION.md` refuses.

## logaffe's own log

Written to files on the host volume ([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md)),
and **bounded**: it rotates by size and keeps a fixed number of files. An
unbounded log on the same volume as the secrets is the most embarrassing possible
way for a logging product to take its own installation down.

## What is deliberately not here

- **No scheduled or automated backups**, no destinations, no retention policy for
  backup artifacts. Settled in `VISION.md`.
- **No downgrade.** Going back a version is restoring a backup.
- **No maintenance mode.** There is no state in which the installation is up but
  refusing service on purpose; it is either serving or it is not running.
- **No metrics endpoint, and no OpenTelemetry export.** `VISION.md` refuses OTLP
  as an ingestion path and this product does not turn around and require it of
  its own operator. The file log and the health endpoint are the whole of what
  logaffe says about itself.
- **No self-update.** logaffe does not check for versions, announce them, or
  update itself. `docker compose pull` is the operator's to run.

# Operating an Installation

Self-hosted software is only as good as its operational story, so upgrading,
backing up and knowing whether the thing is alive are part of the product.
`VISION.md` fixes the shape: Docker Compose is the standard way to run it,
migrations apply themselves, and backup is the operator's responsibility while
being simple to do and clearly documented.

## What lives where

Two stores, and both are needed.

- **The database** holds projects, hosts, tokens, entries, samples and the
  operator's account.
- **The host volume** holds the configuration and the secrets — including the
  **encryption key** at `keys/token.key` that makes the stored tokens readable
  ([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)),
  and with them the operator's TOTP secret, which is encrypted under the same key
  ([ADR 0032](./adr/0032-each-operator-secret-is-stored-for-what-it-is.md)). On an
  installation nobody has claimed yet it also holds `claim-secret.txt`, if the
  installation drew one ([Setup](./setup.md#the-claim-secret)); that file is
  removed by the claim.

**The key is written on first start and never again.** There is no step for the
operator here, in the same spirit as the claim secret being drawn rather than
fetched from anywhere: an installation that came up is one that has a key. It is base64 in a file readable
by its owner alone, and a start that finds one uses it rather than replacing
it — including two containers starting at once, where only the first creates and
the second reads what the first wrote.

**A start whose key does not open what the database holds is refused.** The
installation takes a handful of its stored secrets and tries them; if none of
them opens, the two stores are not halves of one installation and the container
exits with that in its log rather than serving. This is the check that catches a
lost volume, a database restored without its key, and a volume swapped for
somebody else's — and it catches them at the start rather than at the moment the
operator goes looking for a token. A single unreadable secret is not enough to
refuse on: that is a corrupt row, and only a whole unreadable sample is a wrong
key.

Nothing lives inside the container image. A container can be destroyed and
recreated without losing anything, which is what makes an upgrade a `pull` and an
`up`.

**Neither store is useful without the other.** A database restored without its
key is an installation whose every ingest and agent token is undecryptable and
whose operator has only their backup codes left to get in with, and both are
discovered at the moment they go looking. This is the trap the backup command
below exists to remove.

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

It is safe to run beside a serving installation — it reads and takes no lock —
and it writes the tar to standard output, so it has to be redirected somewhere.
**The artifact holds the key material, which makes it as sensitive as the
installation itself.**

What it does not hold is logaffe's own log, which shares the volume with the
key. It is diagnostic output rather than state, it is allowed to reach several
hundred megabytes, and a restore writes the volume back — so carrying it would
mean landing an old log on top of the one describing the failure that led to the
restore.

**Not everything is equally worth saving.** Entries are expendable: they are
short-lived by design, they are additive to the applications' own local files,
and losing them costs little. **Samples are expendable for the same reason** —
they age out inside a year at the very longest and inside a month on an
installation that never changed the window, they describe a machine that is
still there to be asked, and a gap in a band costs an operator nothing they cannot get by
looking now. The operator's account, the configuration, the projects, the hosts
and the tokens are not — losing those means losing the installation. An operator
who backs up only the small, slow-changing part is making a legitimate choice,
and the command supports it:

```
docker compose exec logaffe logaffe backup --without-entries > logaffe-backup.tar
```

**It leaves out the samples too**, and keeps the name it has. The flag names the
distinction it makes — the bulk that ages out against the small part that does
not — rather than listing what falls on each side, and a second flag would offer
a choice between two expendable things that nobody has a reason to make
differently. A host comes back from such an artifact with its name and its token
and no history, which is the same shape a project comes back in.

The artifact says which of the two it is, so a restore does not have to guess
whether an installation's log is missing or was never taken.

**Restoring** puts both halves back:

```
docker compose down
docker compose run --rm logaffe restore --yes < logaffe-backup.tar
docker compose up -d
```

`run`, not `exec`. `backup` is safe beside a serving installation; a restore is
not, and a one-off container while the serving one is down makes the dangerous
case impossible rather than merely unlikely.

**The two are not typed the same way, and the difference is easy to miss.**
`exec` runs the command it is given, so the binary is named — `exec logaffe
logaffe backup`, the service and then the command. `run` passes what follows to
the image's entrypoint, which is the binary already, so `run --rm logaffe
restore --yes` names it once. Naming it twice there does not fail: the extra
word is read as the verb, is not one, and starts a server that restores nothing. **It replaces what is there** — the
database is dropped and rebuilt from the artifact, and the artifact's key
material is written over the volume's — so it says as much before it starts.
Standard input is the artifact, which leaves no terminal to answer a question
from, and `--yes` is what answers it.

It starts a version **no older than** the one that produced the artifact:
the schema is rebuilt to the migration the artifact was taken at, and the start
afterwards migrates the rest of the way by the ordinary upgrade path. Restoring
into an older logaffe is refused rather than attempted, for the same reason a
downgrade is: the schema has moved and the code behind it has not. So is an
artifact that holds no key material, before anything is written — half an
artifact is worse than none, because it looks like one.

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

### What a version is

**A tag is the release, and the tag is the version.** Pushing `v1.4.0` publishes
`ghcr.io/datavisionzero/logaffe:1.4.0` and the three client packages under the
same number, from the same commit, and writes the release entry that names them.
The collector image joins them at `logaffe-collector:1.4.0` under the same
number, from the same commit and in the same job — so a release cannot half
happen, with an entry naming a version whose collector never reached the
registry. One version means one thing everywhere: it is the number a backup
manifest records and the one a restore reads.

The entry is written from the tag with generated notes, and it is written only
where there is none — so notes edited afterwards survive a release that has to be
re-run. What the release *says* is still somebody's to write; what the workflow
guarantees is that the page exists and names the right version the minute the
image does.

`deploy/docker-compose.yml` pulls `:latest`, which is what makes an upgrade a
pull and an up. A prerelease tag does not move it — an installation that never
asked for one is not upgraded into it. An operator who would rather decide when
to move names a version instead:

```yaml
image: ghcr.io/datavisionzero/logaffe:1.4.0
```

### Cutting one

Only a maintainer can, and the whole of it is a tag:

```sh
git tag v1.4.0 && git push origin v1.4.0
```

**The commit being tagged has to be green first.** An image can be rebuilt and a
registry tag moved, but a package pushed to nuget.org cannot be taken back — and
a number that means one thing everywhere has no way to be made to mean nothing.

The workflow then publishes the image, the three packages and the entry under
that number, and **the entry it writes is not the release notes yet.** Generated
notes are a list of what was committed, which is not what somebody deciding
whether to pull came to find out: whether this is worth taking, whether there is
anything to decide before taking it, and what does not change. Writing that over
the draft is the last step of the release rather than a courtesy after it — the
version is on the registry within two minutes, and the page beside it is what an
operator reads.

**A release is done when it can be seen from outside**, which a green workflow
does not show. The latest release names the new tag, `:latest` and `:1.4.0` are
the same digest — on a stable release, since a prerelease moves neither — and
nuget.org lists the packages, which it does some minutes after the run ends.

## Housekeeping that runs on a timer

Some of what the product does is a job on an interval rather than an answer to a
request. **Sessions that went thirty days untouched are removed once a day.**
They admit nothing from the moment they expire — that is what refuses them, and
it needs no job — so this is housekeeping: it keeps the table, and the list the
operator reads for a browser that is not theirs, from filling with rows that
cannot act ([Signing in](./sign-in.md#sessions)).

**A pass that fails does not end the job or the installation.** It is logged as
an error into the file log ([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md))
and the next interval is the retry; what a failed pass leaves behind is rows that
live a day longer than they had to.

Each job runs on its own timer rather than all of them on one. The two this
product has are not the same shape — a session sweep is one statement a day, and
retention below is bounded portions paced against the largest table in the
database — and one timer for both would make the interval of one the interval of
the other.

## Retention as a running job

Retention deletes rows. A background job removes entries whose **receipt time**
has fallen outside their project's window, in bounded portions rather than one
statement, so it never holds a long transaction across a table other projects are
still being written to.

It runs **hourly**, though a window is measured in days. A daily pass would take
a whole day of a project in one burst on the largest table in the database, and
index churn under continuous insert-and-delete is the part of this design most
likely to need attention
([ADR 0023](./adr/0023-retention-deletes-rows-rather-than-dropping-partitions.md));
an hourly pass takes a twenty-fourth of that, and a pass with nothing to do —
which is most of them — costs one index probe per project.

**The same job sweeps the samples**, on the same hourly pass and after the
entries, rather than on a timer of its own. It is the same concern on the same
clock, and what it costs is one statement per host against a table three orders
of magnitude smaller than the entries ([Storage](./storage.md#the-sample-tables))
— a third timer would be a third thing to reason about for a pass that is over
before the hour's entry work has warmed up. The window it counts against is the
installation's single one rather than a project's
([Metrics](./metrics.md#retention)), which is also why this part of the pass asks
nothing about projects at all.

**The same job evaluates the alert conditions**, before any of the sweeping
rather than after it ([Alerts](./alerts.md)). It is the one duty on this pass
that cannot be caught up: a sweep that runs an hour late deletes the same rows an
hour later, while the hour that has just closed is evaluated once or never — so
it goes first, and it costs a few hundred small rows against a table of millions.
An installation with all four conditions switched off does nothing here at all.

**The same job takes what a deleted project left behind.** A project goes at
once and its entries follow in the background
([ADR 0019](./adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)),
and this is that background: there is no window left to read, so they are removed
whole. Nothing can reach them in the meantime — every query runs inside a
project, and that project is gone. **A deleted host's samples go the same way**,
for the same reason: the host is gone from the moment the act completes, and
nothing on either surface can name one that no longer exists.

Deleting rows rather than dropping time partitions is the deliberate choice, and
the reason is that **retention is per project**. A partition can only be dropped
once everything inside it has expired, so a project keeping entries for seven days
would hold them for as long as the longest window in the installation while
sharing partitions with the project that has it — a broken promise rather than a
tuning detail, and a worse one now that the longest window may be a year
([ADR 0048](./adr/0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)).
Fixing it means partitioning by project *and* time, which for thirty projects
across a year is a great deal of machinery for a product whose case is being
small.

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

## Behind a reverse proxy

Two things in the product act on **where a request came from**: the throttle in
front of the sign-in ([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md))
and the address beside each session in the operator's list
([Signing in](./sign-in.md)), which is the only way they can ever notice a
session that is not theirs.

An installation reached directly gets that from the connection and needs no
configuration. One reached through a reverse proxy sees the proxy on every
connection instead, and has to be told which addresses to believe an
`X-Forwarded-For` from:

```
Logaffe__TrustedProxies=10.0.0.0/8,203.0.113.4
```

**Unset means nothing is trusted and the header is ignored**, which is the right
default rather than a cautious one: `X-Forwarded-For` is written by whoever sent
the request, so an installation that honours it without naming a proxy hands
both of the things above to the caller — a throttle partitioned by a value the
attacker picks, and a session list showing whatever an intruder wanted it to
show. Loopback is the one exception and stays trusted, which covers a proxy
sharing the container's network namespace; a proxy in its own container on the
Compose network arrives from that network's address range and has to be named
like any other.

Which range that is depends on how the two are deployed rather than on anything
the product decides, and the arrangement in which the answer is a range the
operator chose rather than one Docker did is
[Deploying](./deployment.md) — including the arrangement that looks tighter and
is not.

## Sizing the disk

The store is bounded by the retention window and the delivery rate, so its size
is predictable and the operator should be given the arithmetic rather than a
shrug: entries per day, times the retention window, times the size of an entry —
which the documentation states with the indexes counted, because the trigram
index of [ADR 0010](./adr/0010-search-is-a-substring-match-not-a-full-text-query.md)
is the second-largest thing in the database and leaving it out of the estimate
would understate it badly.

**The product does that arithmetic where a window is set**, from the project's
own rate rather than from a number the operator has to know
([Projects](./projects.md#the-field-says-what-the-window-will-cost)), and shows
it beside what the installation holds today and what the disk has left. Sizing a
disk before there is an installation is still this arithmetic done by hand; after
there is one, the settings field has already done it.

**Samples do not enter that arithmetic in any meaningful way.** A handful of hosts
at ninety days is a couple of hundred megabytes against a log store measured in
gigabytes ([Storage](./storage.md#what-the-samples-cost)), so an operator sizing a disk
counts entries and adds nothing for the bands.

logaffe does not enforce a disk limit and does not stop ingesting to protect one.
There are no size quotas anywhere in this product, and adding one here would be
the "drop oldest when full" interaction that `VISION.md` refuses.

## logaffe's own log

Written to files on the host volume ([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md)),
and **bounded**: it rotates by size and keeps a fixed number of files. An
unbounded log on the same volume as the secrets is the most embarrassing possible
way for a logging product to take its own installation down.

**A volume it cannot be written to is said out loud at every start**, on the
container's own output:

```
logaffe's own log cannot be written under /var/lib/logaffe: … The installation
is starting anyway.
```

A read-only mount and a full disk both end here, and neither announces itself any
other way — a file that cannot be opened costs the log and nothing else, so the
installation would go on serving while everything it has to say about itself went
nowhere. It starts anyway, because an installation that cannot write its diary
can still take deliveries, still answer its operator and still be repaired from
the screen the complaint is on; what it must not do is keep quiet about it. The
same is true of the command line: a backup or a Host Recovery that fails tells
the operator the whole of it is in that log, and the sentence has to be true.

## What is deliberately not here

- **No scheduled or automated backups**, no destinations, no retention policy for
  backup artifacts. Settled in `VISION.md`. The schedule is the operator's, as
  [Backup](#backup) says — a timer of theirs running the command is them taking
  one. What logaffe does not do is take a backup nobody asked for, or decide
  where it goes.
- **No downgrade.** Going back a version is restoring a backup.
- **No maintenance mode.** There is no state in which the installation is up but
  refusing service on purpose; it is either serving or it is not running.
- **No metrics endpoint, and no OpenTelemetry export.** `VISION.md` refuses OTLP
  as an ingestion path and this product does not turn around and require it of
  its own operator. The file log and the health endpoint are the whole of what
  logaffe says about itself — and that is unchanged by
  [Metrics](./metrics.md), which is what the operator's *machines* report to this
  installation, pushed by their collectors, and never anything this installation
  exposes about itself for somebody else to scrape.
- **No sweep of a machine that stopped reporting, and no alert about one.** A
  host with no recent samples is left exactly as it is: nothing removes it,
  renames it, marks it stale or says anything about it.
  This used to be a sentence about alerting, which the product refused
  altogether. It is now a sentence about hosts. A **project** going quiet is one
  of the four conditions ([Alerts](./alerts.md#a-project-has-gone-quiet)) and a
  host going quiet is not, and the difference is the whole reason the set is
  named rather than general: a project that stops delivering means an application
  stopped, which is what a self-hoster most wants to be told, while a collector
  that stops reporting usually means a collector — a container that was not
  restarted, a machine deliberately switched off, an upgrade in progress. The
  same sentence about the two would be right about one of them
  ([ADR 0050](./adr/0050-the-alert-conditions-are-a-closed-set.md)).
- **No self-update.** logaffe does not check for versions, announce them, or
  update itself. `docker compose pull` is the operator's to run — on a schedule
  if they want one, since a timer they wrote is still them running it. What is
  refused is the installation deciding for itself that it is time.

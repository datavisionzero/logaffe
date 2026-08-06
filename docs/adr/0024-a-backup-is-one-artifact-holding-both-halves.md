# A Backup Is One Artifact Holding Both Halves

logaffe ships a command that writes the database and the key material into a
single artifact on the operator's terminal. This sits close to a line `VISION.md`
draws — logaffe does not run backups, schedule them, or ship snapshots anywhere —
and it stays on the right side of it: nothing is scheduled, nothing is sent
anywhere, and the operator decides when it runs, where the artifact goes and how
long it is kept. What the command removes is not the operator's responsibility
but their opportunity to discharge it incorrectly.

The opportunity is created by
[ADR 0022](./0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md).
Once the encryption key lives on the host volume and never in the database, a
`pg_dump` is no longer a backup of this product — restoring one without its key
produces an installation whose every ingest and agent token is undecryptable, and
that is discovered at the moment the operator most needs it not to be. Two
documented steps would have been the alternative, and the failure mode of two
documented steps is that people perform one of them.

## Consequences

The artifact is a format, which means it is a compatibility surface: it has to
carry what produced it, and a restore into an older logaffe is refused rather
than attempted. That is the same rule as the upgrade path, where a schema newer
than the code stops the installation instead of being served.

**It holds the key material, so the artifact is as sensitive as the installation
itself.** The documentation says so plainly rather than leaving the operator to
infer it from what is inside. Splitting it to keep backups less sensitive would
reintroduce exactly the failure this decision exists to remove.

Verifying an artifact without restoring it is deliberately not offered. It is the
question that matters most about any backup, and answering it honestly means
performing most of a restore — a second mechanism carrying most of the risk of
the first, in a product that would rather document a test restore than pretend to
substitute for one.

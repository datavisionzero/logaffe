# The Claim Window Is a Row in the Database

The window is one of the two ways a claim is guarded and no longer the only one
([ADR 0040](./0040-the-claim-is-guarded-by-a-secret-or-by-a-window.md)). Where it
lives is decided here, and the hash of a drawn claim secret lives in the same
row for the same reasons.

The thirty-minute window of [Setup](../setup.md) hangs off one instant — when the
installation first ran — which is read on every claim attempt, written once, and
written again by Host Recovery. It lives in Postgres, as a table holding one row,
rather than in a file on the host volume beside the key. Putting it in the
database makes "first run" mean exactly the run that created the schema: the
startup that migrates is the startup that writes the row, a restart does not
extend the window because the insert only happens when the table is empty, and
two containers coming up at once are decided by the same single-row unique index
the account table already carries.

The volume was the defensible alternative — the key lives there
([ADR 0022](./0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)),
and so the window would have sat beside it. It was not taken because it buys a
second store for one fact: Host Recovery would write to both, the two can
disagree, and a volume replaced without its database would arm a fresh window on
an installation that already has an operator.

## Consequences

**Restoring a backup taken before the claim brings the old window with it**, and
that window has almost certainly lapsed. The restored installation is not
claimable over the network and the operator runs `logaffe recover`, which is the
command the screen names anyway. That is the honest behaviour:
[ADR 0024](./0024-a-backup-is-one-artifact-holding-both-halves.md) makes a backup
one artifact, and this is one instant travelling inside the database it belongs
to rather than a third thing to remember. In secret mode the same restore behaves
better and for the same reason: the hash travels in the database, the drawn secret
travels on the volume, and a backup carrying both halves comes back claimable
with the secret it always had.

**The instant is not a secret and is not sealed.** It says when the installation
first ran, which is what the claim screen counts down from and therefore already
tells anyone who can reach it. Nothing about this reopens ADR 0022's rule that
the key never goes into the database — the key is what makes secrets readable,
and this is not one. **A claim secret is one**, and so what the row holds is its
hash: it is verified and never read back, which is the same reasoning that
separates a password from a token
([ADR 0032](./0032-each-operator-secret-is-stored-for-what-it-is.md)). The value
itself is either on the volume or in configuration, never here.

**Host Recovery opens the door before it removes the account.** The two are two
statements rather than one, so the order is what decides what a failure between
them leaves behind: a fresh window or a fresh secret on a still-claimed
installation admits nothing, because there is no re-claim while claimed, while an
unclaimed installation whose window has lapsed is a locked door that needs the
command run a second time.

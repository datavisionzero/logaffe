# The Claim Hands Its Enrolment Back Sealed

[ADR 0014](./0014-the-claim-is-atomic-and-holds-nothing.md) says nothing is
stored until the last step, and [Setup](../setup.md) says the operator sees their
authenticator secret and their ten backup codes before that step and confirms one
of the codes by typing it back. Both cannot be true unless the material survives
between two requests somewhere that is not the database, so it survives in the
browser: the installation draws it, seals it under the key on the host volume
with the cipher a token is sealed with, and hands the sealed **ticket** back
alongside the values themselves. The final request returns the ticket, and
opening it is how the installation knows those were the values it drew.

The alternative was to let the browser hand the plain values back and verify them
by proof instead of by provenance — a code that verifies against the secret, and
one code out of the set typed back. That stores nothing either, and it was not
taken because it quietly stops being true that the installation drew them at full
entropy: a claimant could submit ten codes of their own choosing, and the product
would have no way to tell.

## Consequences

**The ticket is bound to the window it was drawn in**, by carrying that window's
instant and being refused against any other. A ticket therefore cannot be carried
across a Host Recovery, which is the one moment at which the installation's
notion of who may claim it changes.

**It carries the codes as their hashes, not as themselves.** The rows cannot be
built at the moment they are drawn — a backup code hangs off an operator that
does not exist until the last step — so what the ticket has to carry is what the
row will hold, and that is the hash
([ADR 0032](./0032-each-operator-secret-is-stored-for-what-it-is.md)). The
operator holds the only copy of the codes from the moment they are shown, which
is what the sheet of paper is for.

**Opening it needs the key**, and an installation whose key does not open what it
holds does not start at all. So there is no state in which a ticket outlives the
key that sealed it while the installation is serving — a ticket that cannot be
opened is a ticket from another installation, and it is refused as one.

**An abandoned claim leaves a ticket in a browser and nothing anywhere else**,
which is ADR 0014's "no half-claimed state to clean up" holding with no sweep and
no expiry job. The window closing is what makes the ticket worthless.

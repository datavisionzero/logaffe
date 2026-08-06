# The Claim Is Atomic and Holds Nothing

An installation is unclaimed until the last step of the claim completes, and a
claim that is started and abandoned leaves nothing behind. The obvious
alternative is to treat starting the claim as taking it — a reservation, a lock,
a half-claimed state — and that is worse in both directions: it hands anyone who
can reach an unclaimed installation a way to lock its operator out without
completing anything, and it adds a state that has to expire, be cleaned up, and
be reasoned about on every path that asks whether the installation has an owner.

## Consequences

Two claimants racing both walk the whole flow, and the one who confirms their
backup codes first has the installation while the other's final step fails. The
loser learns this at the end rather than at the beginning, which is the price of
not holding anything, and it is a screen shown to a person who at that moment
either owns the host — and can take it back — or was never going to.

Every step before the last is therefore a form with no effect: the password is
not stored, the second factor is not enrolled, and nothing exists to abandon.
Whether the installation is claimed is a single fact with no in-between value,
which is what lets the claim window, the read paths and the recovery command all
ask the same question and get an unambiguous answer.

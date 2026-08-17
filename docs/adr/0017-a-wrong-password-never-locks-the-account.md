# A Wrong Password Never Locks the Account

Failed sign-ins are throttled by their source with a delay that grows as they
accumulate, and the account is never locked. Lockout after some number of wrong
attempts is the conventional answer and it is the wrong one here: with exactly
one account, locking it is a denial of service aimed at the only person who has
it, available to anyone who can reach the installation and willing to guess
wrong on purpose. The way back from a locked account would be the host command
that deletes it, so the protection would cost more than the attack it prevents.

## Consequences

Online password guessing is left to the throttle and to the second factor, and
where one is enrolled the second is the part that actually holds: a correct
password on its own opens nothing, so the attempt an unlimited guesser is making
cannot succeed even when it lands.

**Where none is enrolled, the throttle is the whole of it**, and a correct
password opens everything
([ADR 0041](./0041-the-second-factor-is-offered-not-required.md) made that state
reachable). The decision itself survives that unchanged, because what it refuses
is a lockout, and a lockout is worse in exactly the same way it was before — with
one account it is a weapon pointed at its owner, and it becomes a better weapon,
not a worse one, on an installation where the password is the only credential.
What does not survive is the comfort: this ADR used to be able to say that
guessing cannot succeed, and now it can only say that guessing is slow.

Throttling by source is weaker than throttling by account, since a distributed
attempt spreads across many origins. That is accepted: the alternative protects
nothing extra behind the second factor while handing over a way to shut the
operator out, and the throttle's real job is to keep the sign-in path from being
a free oracle rather than to be the last line.

# A Wrong Password Never Locks the Account

Failed sign-ins are throttled by their source with a delay that grows as they
accumulate, and the account is never locked. Lockout after some number of wrong
attempts is the conventional answer and it is the wrong one here: with exactly
one account, locking it is a denial of service aimed at the only person who has
it, available to anyone who can reach the installation and willing to guess
wrong on purpose. The way back from a locked account would be the host command
that deletes it, so the protection would cost more than the attack it prevents.

## Consequences

Online password guessing is left to the throttle and to the second factor, which
is the part that actually holds: a correct password on its own opens nothing, so
the attempt an unlimited guesser is making cannot succeed even when it lands.
This is a decision that only works because the second factor is mandatory and
cannot be turned off, and reopening either of those reopens this one.

Throttling by source is weaker than throttling by account, since a distributed
attempt spreads across many origins. That is accepted: the alternative protects
nothing extra behind the second factor while handing over a way to shut the
operator out, and the throttle's real job is to keep the sign-in path from being
a free oracle rather than to be the last line.

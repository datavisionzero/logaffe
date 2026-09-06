# Sign-In Is Throttled Per Account and Per Source

A failed sign-in counts against two windows: five attempts in a rolling fifteen
minutes per normalized email address, and twenty in the same window per source
address. Either one exhausted refuses further attempts until it drains. This
supersedes [ADR 0017](./0017-a-wrong-password-never-locks-the-account.md).

**0017 is not being overruled, its premise is gone.** It refused a per-account
limit because with exactly one account, locking it is a denial of service aimed
at the only person who has it, and the way back would be the host command that
deletes the account — the protection costing more than the attack. With several
accounts none of that holds: a limit on one address does not touch anybody else's
sign-in, it drains on its own in minutes without anybody intervening, and the
person it inconveniences is the person whose password is being guessed.

What 0017 was right about survives in the shape of the limit. This is a
**throttle that expires**, not a lockout that has to be lifted: nothing latches,
no administrator has to unlock anything, and there is no state a stranger can put
somebody's account into that outlasts a coffee. The counter-argument to lockouts
— that they hand an attacker a way to shut a user out — is answered by the clock
rather than by refusing to count.

**A successful sign-in clears the account's window and not the source's.** The
person who mistyped their password four times and then got it right is back to
zero. The source that has been trying twenty addresses has not proven anything by
guessing one of them correctly, and clearing its window on a success is precisely
what a spraying attempt would use.

## Consequences

**The counters live in a bounded in-process store and are not product data.** A
restart forgets the attempts, and that is the safer end of the trade: the
alternative makes signing in depend on a table that has to be swept, or on a
second service, and an installation whose sign-in path can be taken down by its
own bookkeeping is worse than one that occasionally forgives four wrong guesses.
The store is bounded, so a flood of distinct addresses evicts rather than grows.

**Throttling per source is still weak on its own** and is kept for what 0017
kept it for: it stops the sign-in path from being a free oracle. A distributed
attempt spreads across origins and the per-account window is what actually holds
there, which is the half 0017 could not have.

**Unknown address, wrong password and deactivated account remain one answer in
one time class.** Counting per address must not become a way to ask whether an
address exists, so the refusal, the timing, and the effect on the counters are
identical in all three cases — an attempt against an address nobody holds counts
against that address exactly as one against an address somebody does.

**The second factor is unchanged and remains offered rather than required**
([ADR 0041](./0041-the-second-factor-is-offered-not-required.md)). The throttle
is not a substitute for it and does not make a password-only account safe; what
it does is make the account-level guess expensive, which is the thing 0017 had to
say it could not do.

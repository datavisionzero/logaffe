# The Claim Is Atomic and Holds Nothing

An installation is unclaimed until the last step of the claim completes, and a
claim that is started and abandoned leaves nothing behind. The obvious
alternative is to treat starting the claim as taking it — a reservation, a lock,
a half-claimed state — and that is worse in both directions: it hands anyone who
can reach an unclaimed installation a way to lock its operator out without
completing anything, and it adds a state that has to expire, be cleaned up, and
be reasoned about on every path that asks whether the installation has an owner.

## Consequences

**The claim became one request and this decision got cheaper to hold rather than
weaker.** With the second factor out of the flow
([ADR 0041](./0041-the-second-factor-is-offered-not-required.md)) there is one
step, and one step is atomic for nothing: the row is written or it is not. What
the decision now says is what it always meant — that reaching the claim screen,
filling it in, and abandoning it takes nothing and reserves nothing.

Two claimants racing are therefore decided by the request itself, and the loser
is refused against an installation that already has an operator. In secret mode
there are no strangers in that race
([ADR 0040](./0040-the-claim-is-guarded-by-a-secret-or-by-a-window.md)); in
window mode there can be, and the answer is unchanged — it is a screen shown to
a person who at that moment either owns the host, and can take it back, or was
never going to.

Whether the installation is claimed stays a single fact with no in-between value,
which is what lets the claim guard, the read paths and the recovery command all
ask the same question and get an unambiguous answer.

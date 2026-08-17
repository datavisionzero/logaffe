# The Claim Is Guarded by a Secret or by a Window, and Whoever Installs Chooses

An unclaimed installation is guarded either by a **claim secret**, which is
presented to claim and has no deadline, or by the **open window** of
[ADR 0034](./0034-the-claim-window-is-a-row-in-the-database.md), which has no
secret and lasts thirty minutes. The mode is configuration, read before the first
start, and the secret is the default.

The window was never a security mechanism on its own — it is what you are left
with when there is no secret, and the only thing that can bound an open door is
time. That has two costs. It aims a deadline at the operator rather than at the
attacker: whoever installs has half an hour to also be the person at the browser,
and an installation handed over — set up by one party, claimed by another,
whether that party is an agent or a colleague — cannot be handed over at all.
And it leaves the ground state of every fresh installation reachable by whoever
finds it first. A secret removes both, and it removes them by closing the door
rather than by watching the clock, which is why it is the default now and the
window is the fallback for the installation whose operator cannot read a file or
a container log.

**This reverses `VISION.md`'s previous refusal of a setup secret**, and it is
worth saying so plainly rather than reading the old sentence charitably. What was
being protected there was that nobody must have to *fetch* something before they
can install — and that survives exactly, as the window mode, chosen by the person
who needs it.

## Considered alternatives

**A longer or configurable window.** The cheapest change and the worst one: it
makes a security property into a knob whose most useful value is the exposure the
product refuses everywhere else, and it does not solve the handover — a window is
a race no matter how long it runs.

**A host-issued invitation on top of the open window**, drawn by a command after
the fact. It solves the handover and leaves the ground state where it was: every
installation is still claimable by whoever reaches it first during the minutes
after it starts, and the invitation is a second mechanism layered on the first
rather than a replacement for it. Closing the door outright is both simpler and
strictly stronger, and it costs the same command.

## Consequences

**In secret mode there is no clock at all**, and `VISION.md`'s worry about the
installation that is spun up and then forgotten is answered better than the
window answered it: a forgotten installation is not an open door. The
Certificate Transparency argument in [Setup](../setup.md) — a fresh hostname is
discoverable within seconds of its certificate being issued — is what the window
has to be measured against, and it stops applying to the mode that has no window.

**A drawn secret and a supplied one are stored differently, and deliberately.** A
secret the installation draws on its first start is written to the host volume in
plain, readable by its owner alone, beside the key
([ADR 0022](./0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)),
and logged once at the moment it is drawn; the installation keeps its hash in the
same single row that holds the window
([ADR 0034](./0034-the-claim-window-is-a-row-in-the-database.md)). A secret
supplied as configuration is not stored anywhere: it is compared against what
configuration says on the request, so that changing it is editing the compose
file, and the two stores cannot come to disagree about it. Every start while the
installation is unclaimed names the file in the log without repeating the secret,
because an operator who restarted the container has not lost it.

**The plaintext file is removed when the claim completes.** It exists to be read
once and handed over; leaving it is leaving a credential for a door that no
longer opens.

**A supplied secret has to clear a minimum length, or the installation refuses to
start.** This is the one public door that a guess opens, so a weak one is not the
operator's business to get wrong quietly — and it is a rule about a value, which
puts it in the domain rather than in an endpoint.

**The mode is read on every start while the installation is unclaimed.** A
compose file written wrong is fixed by editing it and restarting, not by Host
Recovery, and an installation that is already claimed is unaffected by either
setting. Whether it is claimed remains the single fact
[ADR 0014](./0014-the-claim-is-atomic-and-holds-nothing.md) makes it.

**Host Recovery opens whichever door the installation is configured for**, so it
draws and prints a fresh secret in secret mode and arms a fresh window in window
mode ([ADR 0013](./0013-host-recovery-returns-the-installation-to-unclaimed.md)).
A drawn secret is never reused: recovery is exactly the moment at which the
installation's notion of who may claim it changes.

**Presenting the secret is a request like any other on this surface**, compared in
constant time and behind the same rate limits the claim already carries. The
secret is the whole of the guard, and it is not a factor alongside a password —
what it protects is the act of claiming, not the account, which does not exist
yet.

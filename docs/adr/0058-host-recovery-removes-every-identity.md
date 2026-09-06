# Host Recovery Removes Every Identity

The host command does one thing and it is the same one thing
[ADR 0013](./0013-host-recovery-returns-the-installation-to-unclaimed.md) chose:
**one operation, one state.** What that state contains has changed. The
installation goes back to holding no identities at all — every user and every
agent token is removed — while projects, groups, ingest tokens, host tokens,
settings and entries stay exactly where they were. The way back in afterwards is
the bootstrap from configuration
([ADR 0054](./0054-the-first-administrator-comes-from-the-environment.md)) rather
than a claim screen.

0013's argument survives intact: the two cases `VISION.md` asks the escape hatch
to cover — somebody locked out of their own account, and an installation nobody
can get into — are the same event with the same answer, and making them one
operation means one state to reason about and one code path that can grant access.
What is superseded is everything 0013 said about *the* operator, the claim
secret, and the window it armed.

## Consequences

**It costs more than it used to, and the command says so before it runs.** It
used to remove one account; it now removes every account on the installation,
including the accounts of people who were not locked out and did not ask. There
is no version of this that removes one account, because a command on the host that
can pick an account is a command that can pick any account, and the recovery path
is unauthenticated by construction — its only guard is that whoever runs it has
the machine.

**Recovery is simpler than it was**, which is the one pleasant consequence: there
is no claim secret to draw, no file to write on the volume, and no window to arm
and expire. The command deletes and stops. Everything that used to make the way
back in safe is now a compose file the operator already controls.

**Agent tokens go, ingest and host tokens stay.** This is 0013's asymmetry and
its reasoning is unchanged — an application shipping logs through this
installation must not notice, and an agent token reads every entry it can reach
and runs past the password and the second factor. It now has a second reason:
an agent token belongs to a user
([ADR 0052](./0052-a-user-and-an-agent-are-one-identity.md)), and a credential
belonging to an identity that no longer exists is exactly the thing that must not
survive. The removal is a step in the command rather than a cascade, ordered
before the identities, so that a failure between the two leaves an installation
that still has its users and has lost its agent configurations — recoverable by
running the command again — rather than live read-everything credentials on an
installation anybody can bootstrap.

**Project assignments go with the users.** They point at identities, so there is
nothing left to point at; the projects themselves are untouched and the
cartesian-product rule of
[ADR 0055](./0055-project-access-is-one-filter.md) does not apply here — that
rule is about not losing access during an upgrade, and this command is about
losing it on purpose.

**There is still deliberately no gentler mechanism on the host.** No password
reset, no re-enrolment of a second factor, no "just make this one person an
administrator again". A second unauthenticated path into an account is a second
thing to secure, and this one is only as safe as it is because it can do nothing
the bootstrap does not already do.

# A User and an Agent Are One Identity

logaffe has **identities**. An identity is a `user` or an `agent`, carries a
name, may be an administrator, and — when it is an agent — belongs to the user
who owns it. A user additionally carries an email address, a state, and a
password. This supersedes
[ADR 0015](./0015-the-operator-has-no-username-and-no-email.md), which refused
both the username and the address, and it is the decision the whole of the user
model hangs from.

**0015 was right for the product it was written for and its reasoning is what
changed.** It refused a username because it would select from a set of one, and
it refused an address for the stronger reason that logaffe sends no mail and an
address stored against a promise never to write to it is an invitation to the
feature that eventually does. There are now several accounts, so a name selects;
and the feature 0015 predicted is exactly the one being added beside it
([ADR 0053](./0053-transactional-email-is-an-optional-capability-of-the-installation.md)),
deliberately and in the open, which is the reopening 0015 asked for by name — *as
a question about mail rather than answered quietly by adding an address field to
the operator.*

## One table, because a user and an agent are the same kind of thing

Both are things that authenticate, that own something, and that a record of a
change can point at. Modelling them as two tables means every one of those
relations exists twice, and the history that
records who changed a setting would have to carry two nullable columns saying
which of them acted. planaffe's `identity` is the shape being copied rather than
reinvented, and its check constraint comes with it: **an agent has an owner and
is never an administrator.** An agent that could be an administrator would be a
credential that grants the powers of the person who issued it plus the power to
change who else has them.

**An agent's authority is its owner's authority and never more.** It inherits the
owner's project set exactly ([ADR 0055](./0055-project-access-is-one-filter.md)),
and the administrative acts of the installation are reachable through it only
while its owner is an administrator. Deactivating a user silences their agents in
the same act, because an agent is a way for a person to act and not a second
person.

**A token is issued by a human, never by an agent.** An agent that can issue an
agent token can grant itself what its owner withheld, which is the load-bearing
exclusion [ADR 0046](./0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)
already names. That exclusion now has a second reason: an agent issuing a token
would be an identity escaping the one it was given.

## Consequences

**The address is what a user signs in with, and it is normalized.** It is
trimmed and lowercased under Unicode normalization before the transaction, the
original spelling is kept for display, and a unique index over the normalized
form is the last line rather than the first. Two people cannot hold the same
address in different capitalization, and the check that says so is in the
database rather than in a service that could be bypassed.

**Identities are never deleted, only deactivated.** A user in `deactivated`
cannot sign in and their agents cannot authenticate, and everything that points
at them — a project assignment, a record of who changed something — keeps
pointing at something that exists. Deletion would mean either orphaned history or
a cascade that erases the record of what somebody did, and both are worse than a
row that says the account is closed. The three states are `invited`, `active`
and `deactivated`; the first exists because a user who has been invited has an
address and no password yet
([ADR 0053](./0053-transactional-email-is-an-optional-capability-of-the-installation.md)).

**A session belongs to a user.** The `Session` row that carried an `OperatorId`
carries an identity, and everything it already did stays true: the secret is
hashed in the database, seven days of inactivity and thirty days absolute end it,
and it is revocable one at a time. Two rules come with several accounts — a
password change ends every other session of that user, and a deactivation ends
all of them — because a credential that has been replaced must not leave a live
session behind it.

**Nothing about the operator survives as a concept.** There is no god-mode
account with a count of one; there is an administrator role, and an administrator
manages the installation without thereby seeing what is in its projects
([ADR 0055](./0055-project-access-is-one-filter.md)). The product no longer has a
word for *the* person it belongs to, which is the point.

**There is always at least one active administrator.** The last one can be
neither deactivated nor stripped of the role. Without that rule an installation
can be talked into a state whose only exit is
[Host Recovery](./0058-host-recovery-removes-every-identity.md), and an
administrator removing their own role by accident is the likeliest way in.

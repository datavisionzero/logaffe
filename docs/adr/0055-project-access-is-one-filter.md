# Project Access Is One Filter, Not Eight

A user sees the projects they have been assigned and no others, and an agent sees
exactly what its owner sees. The assignment is a row — project, user, who
assigned it, when — and the whole of the product asks **one** authorization
component whether a project is in reach.

## Why it is built now rather than when somebody asks

It is the most expensive item of the user model, because the filter has to run
through every entry query, the search, the tail, the filter values, the volume
figures, the alerts and the retention screen. It is built now anyway. For a
logging product, *this person may see that project and not this one* is the most
likely second wish after *there is more than one person*, and retrofitting it
means opening every one of those call sites a second time — at which point the
cheap thing is not the delay but the double handling.

**One filter, not eight.** A call site that forgets it is a leak, and the way to
make forgetting impossible is to have one place that can be forgotten rather than
eight. The reachable set of projects is resolved once per authenticated request
from the identity, and every read narrows to it; a query that does not go through
it does not compile against a store that only offers the narrowed form.

## An administrator manages access and does not thereby have it

An administrator invites people, sets roles, and hands out project assignments —
including to themselves. What the role does **not** do is grant sight of a
project's entries. The two are kept apart because they are different questions:
*who runs this installation* and *whose logs are these*. An administrator who
needs to read a project assigns it to themselves, which is an act the
history records ([`docs/storage.md`](../storage.md)), rather than a capability
that is invisible because it was never used.

This is the one place where the model is stricter than convenience wants, and it
is worth the friction: the alternative is that the administrator role silently
means *reads everything*, and an installation deliberately exposed on the public
internet should not have a role that quietly does.

## Consequences

**Whoever creates a project gets access to it in the same transaction.** The
alternative is a project its creator cannot open, which is a bug report waiting
to happen.

**A project out of reach does not exist.** It is absent from lists rather than
present and refused, and asking for it by id answers the way asking for a project
that was never created answers. A refusal that distinguishes the two is a probe
for what other projects the installation holds.

**The migration seeds the cartesian product.** Every user that exists when the
table is created is assigned every project that exists, before the first
authorization run. An upgrade that silently took access away from somebody who
had it would be the worst possible first impression of this feature.

**A new user starts with nothing.** An invitation grants an account, not a view;
the assignments are a separate, deliberate act. The two directions are asymmetric
on purpose — an upgrade must lose nobody, and a new account must gain nothing by
default.

**Host samples stay outside the filter**
([ADR 0045](./0045-a-sample-is-not-an-entry-and-may-be-read-across-projects.md)
is unchanged). A sample is what a machine said about itself, it belongs to no
project, and one host carries several projects belonging to several people —
there is nothing coherent to narrow it to. Every signed-in user sees every host.
That is a deliberate exception and the reason it is affordable is 0044: a sample
has a closed schema and carries no free text, so what is exposed is a machine
name and four numbers, and never anything anybody logged.

**Ingest tokens keep belonging to the project.** A sending application knows
nothing about users, and giving an ingest token an owner would make delivery
depend on somebody's account still being active.

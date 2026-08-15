# Both Installations Pull on a Timer, and the Tag Is the Deliberate Act

The project runs two installations of its own. Staging exists to be lived with,
so it tracks the trunk: it follows `:main`, which
[`ci.yml`](../../.github/workflows/ci.yml) publishes for every trunk commit that
passed. Production follows `:latest`, which only a release tag moves. Both are
pulled by a timer on the host, and what differs between them is which tag —
and therefore what has to happen before the timer finds anything new.

The alternative is one environment, upgraded when it feels safe, which is what
the project has today. It answers neither of the questions staging exists to
answer: whether the migration chain survives contact with data that has been
sitting there for weeks, and whether the documented upgrade path is still the
one that works.

## The installation pulls; CI does not push

No runner is the host the installations run on. A deploy job would therefore
mean an SSH key as a repository secret and an inbound path from CI into that
host — on a public repository, that is a credential worth stealing. A timer on
the host that pulls needs no secret in GitHub, no inbound connection, and no
change when the repository's visibility changes.

This was written while CI ran on two self-hosted runners on the same home
network as those installations, and the argument then was partly that those
runners were not ephemeral. #69 moved CI to GitHub-hosted runners, which does
not weaken it: the inbound path a deploy job would need now crosses the
internet rather than a LAN, and the secret would sit in a repository anyone can
read the workflows of.

What that accepts is roughly a minute between a green trunk and a running
staging, and a deployment whose outcome is not visible in the Actions run that
caused it. Both are affordable for one operator. Neither would be for a team
waiting on a deploy button, which is the condition under which this record
should be reopened.

## The tag is where the deliberation sits

Production on a timer is not continuous deployment. `:latest` moves when a
release tag is pushed, which is a hand's act, and `release.yml` already refuses
to move it for a prerelease — so no timer can drag an installation into a
version nobody asked for. The deliberate step is not gone; it moved from running
a command on the host to pushing the tag, which is where the decision actually
is.

Nor is it the product updating itself. [`docs/operations.md`](../operations.md)
says that logaffe does not check for versions, announce them, or update
itself, and that `docker compose pull` is the operator's to run — statements
about what the software does, not about what its operator may automate. A
timer on the host running the documented commands is the operator running
them. The same reading already governs backup: that document refuses to
schedule one, and the operator schedules it anyway, because that is whose job
it was said to be.

## A backup goes before the up

There is no downgrade — [Upgrades](../operations.md#upgrades) is explicit that
going back a version means restoring a backup. An unattended upgrade therefore
takes an artifact before it starts anything, because that artifact is the
rollback. It is the one ordering the timer may not get wrong, and it is why this
follows the backup timer rather than shipping beside it.

**Nobody walks the documented upgrade path by hand any more.** That is a smaller
loss than it sounds: the timer runs those commands literally, so a documented
path that has stopped working stops the timer. What goes untested is a person
reading the prose and following it.

**A failed migration now happens with nobody watching.** The installation stops
rather than serving half-migrated, which is what makes this survivable at all,
but stopped is stopped — the health check after the `up` is the whole of what
notices, and its silence has to reach a person. A timer whose failure is visible
only in the host's journal is a deployment nobody is monitoring.

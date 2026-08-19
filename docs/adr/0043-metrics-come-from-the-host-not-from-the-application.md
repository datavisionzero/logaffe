# Metrics Come From the Host, Not From the Application

Samples are delivered by a collector the operator runs on each machine, and not
by the client packages that already sit inside the applications and already have
a delivery to piggyback on. The cheaper option was tempting for exactly the
reason `VISION.md` cares about — it would have kept setup at "add a sink", added
no second artifact, and needed no new entity — and it was rejected because an
application cannot see the machine it runs on. In a container it reads the
container's processor and memory rather than the host's, it cannot see a
filesystem it has not mounted, and five applications sharing one box would
report that box five times, each from inside its own slice of it, with no way for
the installation to know it was being told about one machine.

## Consequences

**A logaffe installation is no longer the only thing an operator deploys.** There
is a second image, on every machine that reports, with its own release cadence
and its own upgrade — and `docker compose pull` on the installation does not
cover it. This is the largest cost in the decision and it falls on exactly the
person `VISION.md` is written for, so the collector is kept small enough to be
boring: it reads, it posts, it holds no state, and it has nothing to configure
beyond an address, a token and the mounts to watch.

**The host has to become an entity**, because samples arrive belonging to a
machine and every other thing in the product belongs to a project. That brings a
name, an identity, a token of a third kind, a screen to manage it on and a
relation from the project ([Metrics](../metrics.md)). None of it would have
existed had the application been the source.

**Process-level numbers are given up, and they were worth something.** Host memory
says the box is full; process memory says which application filled it, and that is
often the more useful sentence. It is given up because collecting both means two
sources with two trust stories and two shapes for one screen, and because the
instance a process would report under is a property a sender writes rather than
something the installation manages. An operator who needs it has the answer one
`docker stats` away on a machine they already have access to.

**The collector is not a place to put anything else.** It reads a machine and
posts numbers. It does not read log files, does not watch containers, and does not
grow a second job — the moment it does, it is an agent on the operator's machines
rather than a thermometer, and that is a different product with a different threat
model.

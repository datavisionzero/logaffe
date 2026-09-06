# Transactional Email Is an Optional Capability of the Installation

logaffe may send mail through an SMTP account the operator configures, and it
sends it for **identity transactions only**: an invitation, a password
recovery, and the confirmation of a changed address. Nothing else in the product
sends mail, and in particular an alert does not
([ADR 0049](./0049-a-notification-carries-numbers-and-names-never-log-content.md)).

This weakens a promise the product made. `VISION.md` said an installation is a
compose file and a database and nothing else, and
[ADR 0015](./0015-the-operator-has-no-username-and-no-email.md) refused an email
address partly to keep that true. The moment there is more than one person, an
administrator has to be able to invite somebody and to give them a forgotten
password back, and there is no way to do either without a channel to a human.
The promise therefore moves from *no external dependencies* to *as few as
possible*, which is a real change and is stated rather than glossed: transactional
mail is ordinary in self-hosting, there are many providers, and an ordinary
mailbox with SMTP does the job.

**SMTP is configuration, not a second service.** The operator supplies a host, a
port, a transport-security mode, credentials, a sender, and a public base URL.
The application has a narrow mail port and owns its text and HTML templates; the
domain knows nothing of SMTP. There is no queue, no retry engine, and no third
production container.

## Consequences

**An installation without SMTP is healthy.** It starts, it signs people in, it
ingests, it serves the API and MCP, and it runs its alerts. Only the acts that
necessarily send a message — inviting somebody, recovering a password, changing
an address — refuse, and they say that no mail is configured rather than failing
obscurely. This is what makes SMTP optional in fact and not just in name, and it
is only affordable because the first administrator does not arrive by mail
([ADR 0054](./0054-the-first-administrator-comes-from-the-environment.md)).

**Links are built from one named base URL and never from the `Host` header.** An
invitation or recovery link assembled from an inbound header is a link an
attacker chooses, on a surface that is deliberately reachable by anyone. The
operator names the externally reachable origin, and an installation that has not
named one cannot send.

**A delivery failure is returned, not queued.** The administrator who pressed
invite is told that the message did not go out, and it is logged. A durable
queue would be a second store, a background worker, and a class of failure that
happens where nobody is looking — for three kinds of message that a human is
waiting on anyway, retrying by hand is the better trade. Retrying issues a fresh
secret rather than resending the old one.

**Mailpit is development infrastructure and never production.** It joins
`deploy/docker-compose.dev.yml`, the integration tests read the delivered message
through its API, and it appears nowhere in the production topology.

**The mail path is a new way for the installation to reach outward**, and it is
the second one after ntfy. Both carry names and never log content, and this one
carries less: an address, a link, and the name of the installation.

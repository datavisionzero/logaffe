# The First Administrator Comes From the Environment, and the Claim Is Gone

An installation with no identity creates its first administrator on startup, out
of configuration the operator wrote before that startup: a name, an email
address, and a bootstrap token. There is no claim, no claim secret, no claim
window, and no first-run screen. This supersedes
[ADR 0014](./0014-the-claim-is-atomic-and-holds-nothing.md),
[ADR 0034](./0034-the-claim-window-is-a-row-in-the-database.md),
[ADR 0035](./0035-the-claim-hands-its-enrolment-back-sealed.md),
[ADR 0036](./0036-an-enrolment-carries-its-own-sealed-ticket.md) and
[ADR 0040](./0040-the-claim-is-guarded-by-a-secret-or-by-a-window.md), all five
of which exist to answer questions the claim asked and nothing else does.

**This is a demolition rather than a rebuild.** The claim was a well-argued
mechanism, and every one of its parts was there for a reason: the window and the
secret because an unclaimed installation is reachable by strangers, the sealed
ticket because a two-step claim had to carry values it had not stored yet, the
atomicity because a half-claimed state is a way to lock an operator out. What
removes all of it at once is that the question changes. **The operator already
writes a compose file before the first start**, and anything they can put in it
is a thing that does not have to be defended on a public endpoint. A door that is
never opened needs neither a lock nor a clock.

## Why a bootstrap token and not a password

The obvious alternative is a password in the environment. It is refused: a
password in a compose file is a password in the operator's shell history, in
their git, and in `docker inspect` for as long as the container runs, and unlike
a token it is the credential a human reuses. So the environment names a
**bootstrap token**, and the browser exchanges it once for a password and a
session. This is planaffe's arrangement and it is taken as it stands. Two things
fall out of it and both are wanted: **the first sign-in does not depend on mail**,
which is what lets SMTP be genuinely optional
([ADR 0053](./0053-transactional-email-is-an-optional-capability-of-the-installation.md)),
and the token is a value the installation can require to be long, because nobody
has to remember it.

The token is a user token for the administrator it creates, so it does not stop
working at the exchange: it is the credential that administrator's own tooling
holds afterwards. What it does not do is grow a second one — the variables are
read on the one start where no identity exists, and **from the second start they
are ignored, whatever they say**. Changing the token in the environment changes
nothing, and an installation that already has an administrator cannot be
bootstrapped a second time by editing a file.

## Consequences

**An installation that starts with nothing and is told nothing starts anyway**,
and says in its log that nothing can authenticate and which variables would fix
it. It does not refuse to run: the ingestion path needs no identity, and an
installation that is receiving logs while its administrator is still writing the
compose file is doing something useful. A bootstrap secret the installation will
*not accept* — too short, or an address that is not one — is the other case and
stops the start, the way a failed migration does, because nothing was written and
starting anyway would offer a credential that cannot work.

**The upgrade is not silent for an existing installation.** The operator that
exists today has a password and no address, and the migration makes it the first
administrator by reading the address out of the same bootstrap variable. An
installation upgraded without setting it does not start, and says so. That is
deliberate: the alternative is a placeholder address in the database that
somebody signs in with once and never changes.

**Nothing is publicly reachable before there is an administrator.** The claim was
the one surface a stranger could act on, and it is gone; the sign-in of an
installation with no users refuses every attempt in the same way it refuses a
wrong password. The bootstrap exchange is the one exception and it is guarded by a
secret that never travelled over the network to get there.

**`docs/setup.md` stops being a document about a flow and becomes one about
configuration**, and the frontend loses `src/web/src/claim` entirely. The
OpenAPI contract loses `/claim` and gains the bootstrap exchange.

**Both products are now brought up the same way**, which is worth something on
its own: an operator who runs planaffe and logaffe writes the same three
variables twice and does not have to remember that one of them is claimed in a
browser.

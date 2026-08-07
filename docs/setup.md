# Setup and the Claim

An installation starts life belonging to nobody, and the **claim** is the act
that gives it an operator. This is the only surface in the product a stranger can
act on, and everything below is written from that angle.

Two things are settled in `VISION.md` and are the premises here rather than
decisions of this document. **Anyone who can reach an unclaimed installation may
claim it** — there is no setup secret to fetch first. And **there is always a way
back in from the host**, which is what makes the rest affordable.

## An unclaimed installation

An unclaimed installation exposes the claim and nothing else. There is no
ingestion, because ingestion needs a token and a token needs a project and a
project needs an operator; there is no MCP; there is nothing to read and nothing
to configure. The whole reachable surface is one flow.

Claiming is open, and the cost of that is a race the operator can lose: an
installation that is reachable before its operator gets to it can be claimed by
whoever finds it first. The damage is bounded — an empty installation, no data to
take, and the host command below takes it back — but it is a real window and the
next section is what keeps it narrow.

## The claim window

The window opens when the installation **first runs**, lasts **30 minutes**, and
**a restart does not extend it**. The deadline belongs to the installation rather
than to the process, so nobody gains anything by forcing a restart, and an
installation that is brought up and forgotten stops being an open door half an
hour later rather than indefinitely.

Thirty minutes is deliberately short, and it is short because the way back is
cheap. The alternative reading — make the window generous so nobody is locked
out — buys convenience with exactly the exposure this product refuses elsewhere.
It would also be aimed at the wrong threat: a fresh installation is not found by
somebody scanning the whole internet on the off chance, it is found because a new
hostname with a fresh certificate appears in the public **Certificate
Transparency** logs within seconds of being issued, and those are watched. An
installation reachable under its own name is discoverable almost immediately, so
the window has to be measured against a person walking back to their desk, not
against a scanner's patience.

When the window lapses, claiming over the network is over. The installation says
so plainly and names the host command that re-opens it, because an operator
meeting this screen is already having a bad minute and does not need to search
for the answer. The container log says the same thing on every start, since an
operator bringing an installation up for the first time is watching it.

The instant the window hangs off lives **in the database**, which makes the first
run the run that created the schema
([ADR 0034](./adr/0034-the-claim-window-is-a-row-in-the-database.md)). One
consequence is worth knowing in advance: an installation restored from a backup
taken *before* it was claimed comes back with that old window, which has long
since lapsed, and is opened again with the host command below.

## The claim is one act

The flow establishes, in order:

1. a **password**,
2. a **second factor** — a TOTP authenticator, enrolled during setup rather than
   offered afterwards,
3. **backup codes**, shown once and confirmed by typing one back.

**It is atomic.** The installation stays unclaimed until the last step completes,
and a claim that is started and abandoned holds nothing. There is no reservation,
no lock, and no half-claimed state to clean up: two people racing both get to
walk the flow, and whoever confirms their backup codes first has the
installation, while the other's final step fails against an installation that is
no longer unclaimed
([ADR 0014](./adr/0014-the-claim-is-atomic-and-holds-nothing.md)).

Because nothing is stored before the last step, the secret and the codes have to
survive between the screen that shows them and the request that completes the
claim — and they survive **in the browser**, alongside a sealed copy the
installation drew and only the installation can read
([ADR 0035](./adr/0035-the-claim-hands-its-enrolment-back-sealed.md)). That is
what keeps "the installation drew these at full entropy" a fact rather than a
hope, without a half-claimed row anywhere.

The second factor cannot be turned off later. `VISION.md` puts it in the guided
setup precisely so it is not an optional extra, and a single god-mode account on
the public internet is not a place where that is negotiable afterwards. It can be
re-enrolled by a signed-in operator, which is how a replaced phone stays an
ordinary event. Backup codes are single-use, and a fresh set can be generated at
any time, which replaces the old set entirely.

How the operator gets back in on an ordinary day, from whatever machine they are
at, is [Signing in and sessions](./sign-in.md).

## The operator has no name and no address

Sign-in is a password and the second factor. There is **no username**, because an
installation has exactly one account and a name that identifies which of one is
decoration. There is **no email address**, because the product sends no mail at
all — no verification, no notification, no password reset — and storing an
address that is never written to would be inviting the feature that reads it
([ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md)).

Consequently there is **no password reset over the network**. Forgetting the
password is the same event as losing the second factor and the backup codes, and
it has the same answer: the host.

## After the claim

What follows the claim is a **guide, not a stage**: it offers the first project
and hands over a copy-paste snippet for a Serilog sink pointed at this
installation, with the ingest token already in it. It can be skipped, it holds no state, and
nothing is half-configured if it is abandoned — the installation is fully claimed
the moment the claim completed.

It exists because `VISION.md` makes ingestion friction the adoption barrier, and
the shortest path from a running installation to a log arriving is a snippet the
operator does not have to assemble from documentation.

## Host Recovery

**Host Recovery** is a command run inside the running container, reached the way
anything is reached on a Docker host:

```
docker compose exec logaffe logaffe recover
```

**It says what it does and waits to be told to do it.** Somebody reading the
command name will expect the smaller thing — a password reset — so it prints
what it removes and asks for the word `recover` before touching anything. A
caller with no terminal passes `--yes`.

It **returns the installation to unclaimed** and arms a fresh claim window
([ADR 0013](./adr/0013-host-recovery-returns-the-installation-to-unclaimed.md)).
That single operation covers both cases `VISION.md` asks it to: an operator who
lost their second factor and their backup codes, and an installation whose window
lapsed before anyone claimed it.

**Projects, ingest tokens and log entries are untouched.** Recovery replaces who
the installation belongs to, not what it holds, and an application shipping logs
through it does not notice. Existing sessions end, since the account they belong
to no longer exists.

**It is not a security boundary, and it is not treated as one.** Whoever can run
a command in the container already owns the database and could do this and more
by hand. The command exists so that the operator does not have to, and its whole
security property is that it is reachable from the host and never over the
network.

Every use is written to logaffe's own file log
([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md)), which is the
one place a record of it can survive the reset it performs.

## Abuse protection on this surface

The claim and the sign-in are public, pre-authentication and reachable by anyone,
so they carry the rate limits `VISION.md` requires of every exposed endpoint.
Failed sign-ins are throttled by their source and **never lock the account**,
because with one account a lockout is a weapon pointed at its owner
([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md)). A rate
limit on the claim does not stop somebody who wins the race honestly — it stops
the automated attempt at the password afterwards.

## What is deliberately not here

- **No setup token or install secret.** Settled in `VISION.md`: the claim is open
  to whoever reaches it, and the short window plus Host Recovery is the answer to
  what that costs.
- **No email, anywhere.** No verification, no reset link, no notification that a
  claim happened. There is nothing to notify: the account that would be told is
  the one being created.
- **No second account, no invitation, no delegation.** Settled in `VISION.md`:
  one operator, no user model.
- **No disabling the second factor** once the installation is claimed.
- **No account recovery over the network**, by any mechanism, for any reason.
- **No re-claim while claimed.** An installation with an operator is not
  claimable, and the only route back to unclaimed is the host.

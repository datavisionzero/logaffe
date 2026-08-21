# Setup and the Claim

An installation starts life belonging to nobody, and the **claim** is the act
that gives it an operator. This is the only surface in the product a stranger can
act on, and everything below is written from that angle.

Two things are settled in `VISION.md` and are the premises here rather than
decisions of this document. **Whoever installs decides how the claim is
guarded** — by a secret, which is the default, or by an open window. And **there
is always a way back in from the host**, which is what makes the rest affordable.

## An unclaimed installation

An unclaimed installation exposes the claim and nothing else. There is no
ingestion, because ingestion needs a token and a token needs a project and a
project needs an operator; no samples, for the same reason one step over — a
host is something an operator creates ([Metrics](./metrics.md)); there is no
MCP; there is nothing to read and nothing to configure. The whole reachable
surface is one flow.

## The claim secret

```yaml
Logaffe__Claim__Mode: secret     # the default
Logaffe__Claim__Secret: ""       # empty: the installation draws one
```

In this mode the installation is **not claimable by anyone who cannot present the
secret**, and there is no deadline of any kind. A door that is locked does not
need a clock: an installation that is brought up and forgotten is not an open
door, and the operator can claim it a week later from wherever they are
([ADR 0040](./adr/0040-the-claim-is-guarded-by-a-secret-or-by-a-window.md)).

**Either the installation draws the secret or the operator sets it.** A drawn one
is thirty-two symbols of the same transcription-safe alphabet a token is written
in — no `l`, no `o`, no `0`, no `1` — because this is a value that gets read off
a terminal and typed into a browser on another machine more often than a token
ever is. It lands in two places on the start that draws it:

```
/var/lib/logaffe/claim-secret.txt      on the host volume, readable by its owner alone
```

and once in the container log, which is where somebody watching a first start is
already looking. Every later start while the installation is still unclaimed
names the file without repeating the secret, because an operator who restarted a
container has not lost anything.

A secret set as configuration is **not stored at all** — it is compared against
what configuration says, so changing it is editing the compose file and there is
no second copy to disagree with. It has to be at least sixteen characters or the
installation refuses to start, which is the one rule this value has: it is
pasted, not recited, and anything shorter is somebody typing a word.

**The secret guards the act of claiming and nothing else.** It is not a factor
alongside the password, it grants nothing on its own, and it stops working the
moment the installation is claimed — at which point the file is removed, because
what is left otherwise is a credential for a door that no longer opens. Losing it
before that is what Host Recovery is for.

**This is the mode an unattended installation uses.** Whoever performs it — a
person, a script, an agent — writes the compose file, brings the installation up,
reads the secret and hands it over. The person who claims never has to be the
person who installed, and never has to be at a browser within minutes of the
container coming up.

## The claim window

```yaml
Logaffe__Claim__Mode: window
```

In this mode there is no secret and **anyone who can reach the installation may
claim it**, for a window that opens when the installation **first runs**, lasts
**30 minutes**, and that **a restart does not extend**. The deadline belongs to
the installation rather than to the process, so nobody gains anything by forcing
a restart.

Claiming is open here, and the cost of that is a race the operator can lose: an
installation that is reachable before its operator gets to it can be claimed by
whoever finds it first. The damage is bounded — an empty installation, no data to
take, and the host command below takes it back — but it is a real window, and
thirty minutes is what keeps it narrow.

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
for the answer. The container log says the same thing on every start.

**This mode exists for the installation that cannot read a file or a container
log** — a one-click host, a hosting panel, somebody else's Docker. It is the
older of the two rather than the better one, and it is chosen deliberately.

The instant the window hangs off lives **in the database**, which makes the first
run the run that created the schema
([ADR 0034](./adr/0034-the-claim-window-is-a-row-in-the-database.md)), and so does
the hash of a drawn secret. One consequence is worth knowing in advance: an
installation restored from a backup taken *before* it was claimed comes back with
that old window, which has long since lapsed, and is opened again with the host
command below. In secret mode the same restore behaves better — the hash travels
in the database and the secret on the volume, so a backup holding both halves
comes back claimable with the secret it always had.

## The claim is one act

The claim establishes a **password** and nothing else. It is a single request:
the installation is unclaimed until it succeeds, a claim that is abandoned holds
nothing, and there is no reservation, no lock and no half-claimed state to clean
up ([ADR 0014](./adr/0014-the-claim-is-atomic-and-holds-nothing.md)). In window
mode, two people racing both get to fill the screen in, and whoever sends it
first has the installation while the other is refused against an installation
that is no longer unclaimed.

**The second factor is not part of it**
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)). It used to
be — a TOTP enrolment and a sheet of backup codes, shown and confirmed before the
claim completed — and requiring it there meant the first act of a new
installation depended on the claimant having an authenticator to hand at that
minute. An operator enrols one afterwards, from the settings, whenever they
decide to, and the installation says the second factor is off for as long as it
is. How that works is [Signing in and sessions](./sign-in.md).

The password is at least sixteen characters
([ADR 0042](./adr/0042-the-password-carries-more-so-it-gets-longer.md)), which is
what it is worth on an installation where it may be the only credential.

## The operator has no name and no address

Sign-in is a password, and a second factor if one is enrolled. There is **no
username**, because an installation has exactly one account and a name that
identifies which of one is decoration. There is **no email address**, because the
product sends no mail at all — no verification, no notification, no password
reset — and storing an address that is never written to would be inviting the
feature that reads it
([ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md)). The
notifier an operator may configure for alerts is not a way round this: it is a
topic on a push service, it belongs to the installation rather than to the
account, and nothing about the account is ever sent to it.

Consequently there is **no password reset over the network**. Forgetting the
password has the same answer as losing the second factor and the backup codes
with it: the host.

## After the claim

What follows the claim is a **guide, not a stage**: it offers the second factor,
then the first project with a copy-paste delivery pointed at this installation
and the ingest token already in it. It can be skipped, it holds no state, and
nothing is half-configured if it is abandoned — the installation is fully claimed
the moment the claim completed.

The second factor comes first in it because that is the one thing on the list the
operator cannot be reminded of by anything else later except the banner, and
because it costs a phone that is already in their hand. Skipping it is a
decision, not an oversight, and the interface keeps saying so.

The rest exists because `VISION.md` makes ingestion friction the adoption
barrier, and the shortest path from a running installation to a log arriving is a
snippet the operator does not have to assemble from documentation.

**The guide is the interface's, and the backend knows nothing about it.** It is
the act that enrols a second factor, the act that creates a project and the act
that issues an ingest token, walked in order by the single-page application.
There is no endpoint that reports how far along it is: a guide that holds no
state has no progress to report, and one that reported it would be the stage this
is not.

**What it hands over is the plain path** — an address, a header and one CLEF
line, which needs nothing installed and works from any language
([Ingestion](./ingestion.md)):

```
curl -X POST https://logs.example.com/ingest \
  -H "Authorization: Bearer logaffe_ingest_…" \
  -H "Content-Type: application/x-ndjson" \
  --data-binary "{\"@t\":\"$(date -u +%FT%TZ)\",\"@mt\":\"Hello from {Sender}\",\"Sender\":\"curl\"}"
```

The token and the address are already in it, and the timestamp is generated when
the line is sent rather than when the token was issued — the UI orders by `@t`,
and a snippet carrying a fixed one would deliver an entry dated whenever the
operator happened to open the page. The cost is that this is a POSIX shell line.

**The Serilog form is the same handover with the sink in place of `curl`, and it
arrives with the package it needs.** The .NET packages are not published yet
([Codebase](./codebase.md)), and a snippet whose first line is a package nobody
can install is worse than one that is honestly the plain path.

**The guide does not offer a host**, and that is a decision rather than an
omission. Its whole job is the shortest path from a claimed installation to a log
arriving, which is the barrier `VISION.md` names; a step that asks the operator to
name a machine and go paste a second command on it lengthens exactly the flow that
exists to be short, in service of a screen that has nothing to draw until logs are
coming in anyway. A host is created from the settings when the operator wants the
numbers ([The web UI](./ui.md)), which is the moment they have a reason to.

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

It **returns the installation to unclaimed** and opens the way in again
([ADR 0013](./adr/0013-host-recovery-returns-the-installation-to-unclaimed.md)),
in whichever form that installation is configured for: it draws and prints a
fresh claim secret, or it arms a fresh window. The secret is drawn rather than
reused, because this is exactly the moment at which the installation's notion of
who may claim it changes. That single operation covers every case `VISION.md`
asks it to — an operator who forgot their password, one who lost their second
factor and their backup codes with it, and an installation whose door closed
before anyone came through it.

**Projects, hosts, ingest tokens, host tokens, log entries and samples are
untouched.** Recovery replaces who the installation belongs to, not what it
holds, and neither an application shipping logs through it nor a collector
reporting to it notices. Existing sessions end, since the account they belong to
no longer exists.

A host token survives for the reason an ingest token does: it writes and reads
nothing ([Metrics](./metrics.md#the-host-token)), so it is not a credential that
carries anything out of the installation it no longer belongs to. That is the
whole of the distinction being drawn in the paragraph below.

**The agent tokens end with it — both kinds** — and the command says how many
went, because each one is a client configuration somewhere that has just stopped
working. A reading token reads every entry in every project, and an administering
token works the settings and may have been issued to destroy
([MCP](./mcp.md), [ADR 0046](./adr/0046-administration-is-reachable-on-a-token-that-reads-no-entries.md)),
so they are the one thing the installation holds that must not survive changing
hands — and issuing a new one is a paste per agent.

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
so they carry the rate limits `VISION.md` requires of every exposed endpoint. A
presented claim secret is compared in constant time and behind those limits, like
any other credential on a public surface. Failed sign-ins are throttled by their
source and **never lock the account**, because with one account a lockout is a
weapon pointed at its owner
([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md)) — and that
holds all the more on an installation whose operator enrolled no second factor,
where the throttle is the whole of what stands in front of a guess.

## What is deliberately not here

- **No setup secret that has to be fetched from somewhere.** The claim secret is
  produced by the installation being installed, or set by the person installing
  it; window mode needs none at all. Neither involves an account anywhere, a
  licence, or a service to ask.
- **No email, anywhere.** No verification, no reset link, no notification that a
  claim happened. There is nothing to notify: the account that would be told is
  the one being created. The installation can be given somewhere to send an alert
  ([Alerts](./alerts.md)), and that changes nothing here — it is not mail, it is
  never about the account, and nothing that happens to the claim, the password or
  the second factor produces one.
- **No second account, no invitation, no delegation.** Settled in `VISION.md`:
  one operator, no user model.
- **No account recovery over the network**, by any mechanism, for any reason.
- **No re-claim while claimed.** An installation with an operator is not
  claimable, and the only route back to unclaimed is the host. Neither claim
  setting does anything on an installation that already has an operator.

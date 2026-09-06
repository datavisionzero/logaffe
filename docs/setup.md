# Setup and the First Administrator

An installation creates its **first administrator** on the one start where it
holds no identity, out of configuration whoever installs it wrote before that
start. There is no claim, no claim secret, no claim window and no first-run
screen: the whole of what used to be the only surface a stranger could act on is
gone ([ADR 0054](./adr/0054-the-first-administrator-comes-from-the-environment.md)).

Two things are settled in `VISION.md` and are the premises here rather than
decisions of this document. **Whoever installs names the first administrator**,
before the first start, because they are the one holding the compose file. And
**there is always a way back in from the host**, which is what makes the rest
affordable.

## The three keys

```yaml
Logaffe__Bootstrap__Administrator: "Alex"                   # the name they are shown under
Logaffe__Bootstrap__Email: "alex@example.com"               # the address they sign in with
Logaffe__Bootstrap__Token: "…at least 32 characters…"       # exchanged once, in a browser
```

All three or none. An installation given all three and holding no identity
creates that administrator on that start and says so in its log.

**A token rather than a password**, and that is the whole design. A password in a
compose file is a password in a shell history, in a git repository and in
`docker inspect` for as long as the container runs — and unlike a token it is the
credential a human reuses somewhere else. So the configuration names a value
nobody has to remember, the browser exchanges it once for a password, and the
password never travels through a file. Draw it rather than think of it:

```
openssl rand -base64 32
```

**The exchange is reached from the sign-in screen**, by the one link on it that
is not a sign-in. Nothing announces that an installation is waiting to be set up:
whether anybody has ever signed in here is not something a stranger learns by
loading the page, and the person who has the token does not need to be told.

**It happens once.** What makes it single-use is not a flag somebody has to
remember to set: the bootstrap writes the administrator *invited* and without a
password, and the exchange is the act that gives them one — so the moment it
succeeds there is no passwordless administrator left to exchange against, and a
second attempt is refused against an installation that already has its person.

**From the second start the three keys are ignored**, whatever they say. Changing
the token in the compose file changes nothing, and an installation that already
has an administrator cannot be bootstrapped again by editing a file. The way back
into one nobody can sign into is Host Recovery, below.

## What happens on a start that was told nothing

**It starts anyway, and says so.** An installation with no identity and no
configuration naming one writes a line naming the three keys and goes on serving:
ingestion needs no identity, and an installation receiving logs while somebody is
still writing its compose file is doing something useful. Nothing can sign in
until the keys are set and it is started again.

**A value it will not accept stops the start**, the way a failed migration does —
a token shorter than 32 characters, or an address that is not one. Nothing was
written, and starting anyway would be an installation nobody can sign into
carrying a variable that looks as though they could.

## Upgrading an installation that had an operator

An installation from before this version had one **operator**: a password, an
optional second factor, and no address of any kind
([ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md)). The schema
migration carries that account over to be the first administrator, keeping its id,
its password, its second factor, its backup codes and its sessions. **Nobody is
signed out and nobody resets anything.**

The one thing it cannot bring is the address, because it never had one. So
`Logaffe__Bootstrap__Email` is what supplies it, on the first start after the
upgrade, and that start **refuses to run without it**. The alternative was a
placeholder address in the database that somebody signs in with once and never
changes, and this is the version that does not leave one behind.
`Logaffe__Bootstrap__Administrator` is taken as their name if it is set, and the
token is not read at all — that account already has a password.

## The password is at least sixteen characters

([ADR 0042](./adr/0042-the-password-carries-more-so-it-gets-longer.md)) — which
is what it is worth on an account where it may be the only credential. It is
hashed with Argon2id
([ADR 0057](./adr/0057-a-users-secrets-are-stored-for-what-they-are-and-the-password-is-argon2id.md)),
and an installation carried over from an older version rewrites each hash at the
new algorithm on that account's next successful sign-in.

**The second factor is not part of the exchange**
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)). Requiring
it there would make the first act of a new installation depend on the person
having an authenticator to hand at that minute. Every user enrols one afterwards,
from their own settings, whenever they decide to, and the interface says the
second factor is off for as long as it is. How that works is
[Signing in and sessions](./sign-in.md).

## Everybody after the first arrives by invitation

An administrator invites an address, the person sets their own password from a
one-time link, and they begin with an account and **no project access** until
somebody gives them some
([ADR 0055](./adr/0055-project-access-is-one-filter.md)). Recovering a forgotten
password and changing an address work the same way. All three need mail, which is
the one external dependency this product takes and is optional
([ADR 0053](./adr/0053-transactional-email-is-an-optional-capability-of-the-installation.md)):
an installation with no SMTP configured is healthy and complete in every other
respect, and only those three acts refuse, saying why.

## After the exchange

What follows it is a **guide, not a stage**: it offers the second factor, then
the first project with a copy-paste delivery pointed at this installation and the
ingest token already in it. It can be skipped, it holds no state, and nothing is
half-configured if it is abandoned — the account was complete the moment the
exchange finished.

The second factor comes first in it because that is the one thing on the list
nothing else reminds anybody of later except the banner, and because it costs a
phone that is already in their hand. Skipping it is a decision, not an oversight,
and the interface keeps saying so.

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
omission. Its whole job is the shortest path from a running installation to a log
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

**It says what it does and waits to be told to do it, and it now does more than
its name suggests.** Somebody reading the command name will expect the smaller
thing — a password reset — and this removes not one account but **every identity
on the installation**: every user, and every agent token
([ADR 0058](./adr/0058-host-recovery-removes-every-identity.md)). It prints what
it removes and asks for the word `recover` before touching anything. A caller
with no terminal passes `--yes`.

There is deliberately no version that picks an account. A command on the host
that can pick one is a command that can pick any one, and this path is
unauthenticated by construction — its only guard is that whoever runs it has the
machine.

**The way back in afterwards is the bootstrap**: set the three keys and start the
installation again. That makes recovery simpler than it used to be, because there
is no secret to draw, no file to write and no window to arm and expire. The one
operation covers every case `VISION.md` asks it to — somebody who forgot their
password, somebody who lost their second factor and their backup codes with it,
and an installation whose administrators are all gone.

**Projects, groups, hosts, ingest tokens, host tokens, settings, log entries and
samples are untouched.** Recovery removes the installation's people, not what it
holds, and neither an application shipping logs through it nor a collector
reporting to it notices. Sessions, backup codes and project assignments go with
the accounts they belong to.

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

The bootstrap exchange and the sign-in are public, pre-authentication and
reachable by anyone, so they carry the rate limits `VISION.md` requires of every
exposed endpoint. A presented bootstrap token is compared in constant time and
behind those limits, like any other credential on a public surface.

Failed sign-ins are throttled **per account and per source** — five attempts in a
rolling fifteen minutes against one address, twenty from one source
([ADR 0056](./adr/0056-sign-in-is-throttled-per-account-and-per-source.md)). It is
a throttle that drains on its own and never a lockout somebody has to lift: with
several accounts the argument that a per-account limit is a weapon pointed at its
owner no longer holds, and what replaces it is a clock rather than a refusal to
count. An address nobody holds, a wrong password and a deactivated account are one
refusal, in one wording and one time class.

## What is deliberately not here

- **No setup secret that has to be fetched from somewhere.** The bootstrap token
  is set by whoever installs, in the file they are already editing. It involves
  no account anywhere, no licence, and no service to ask.
- **No first-run screen, and no endpoint that says whether one would be
  needed.** The bootstrap is a start, not a flow, and an installation does not
  report how far along its setup is.
- **No second bootstrap.** An installation that holds any identity ignores the
  three keys, and the only route back to none is the host.
- **No self-registration.** Nobody creates their own account: an administrator
  invites an address, and that is the only way a second person exists
  ([ADR 0052](./adr/0052-a-user-and-an-agent-are-one-identity.md)).
- **No account recovery over the network without mail.** Password recovery is a
  one-time link to an address, so an installation with no SMTP configured has the
  host and nothing else — which is what it always had.

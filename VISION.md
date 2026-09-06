# logaffe — Product Vision

## In one sentence

logaffe is a self-hostable, central logging tool for a small team and their AI
agents: it collects logs from many applications, keeps them separated by
project, and makes them accessible through a web UI and through MCP — safe
enough to expose directly to the public internet.

## Problem

Backend applications — especially .NET services — commonly write logs to local
files. Those logs are scattered across machines and containers, hard to search,
and effectively invisible unless someone goes looking for the file. Existing
central logging stacks solve this, but they demand a level of instrumentation
maturity (full OpenTelemetry adoption, structured events everywhere) and
operational effort that is out of proportion for small and mid-sized setups.

logaffe targets the gap: teams that want central, searchable logging without
first rebuilding how their applications log.

## Target users and scenario

- A handful of people running self-hosted backend services between them — plus
  the AI agents working on their behalf. One person on their own is the ordinary
  case and stays a first-class one.
- Primarily .NET backend applications that today log to local files.
- A single deployment hosting on the order of 10–30 projects, each with a
  deliberately limited retention window.

logaffe is not designed for multi-year log archives or billions of rows. Log
volume per project is intentionally capped, and short retention is the expected
mode of operation.

## The user model

logaffe has **users**, and a user sees the projects they were given and no
others. There is one role, **administrator**, and it is about running the
installation — inviting people, deactivating them, handing out project access —
rather than about seeing what is in it. An administrator who wants to read a
project assigns it to themselves, and that assignment is a recorded act. Nothing
in the product is visible by virtue of a role
([ADR 0055](./docs/adr/0055-project-access-is-one-filter.md)).

**A user and an agent are the same kind of thing**, and the product calls both an
identity ([ADR 0052](./docs/adr/0052-a-user-and-an-agent-are-one-identity.md)). An
agent belongs to the person who issued its token, sees exactly the projects that
person sees, is never an administrator, and goes quiet when its owner is
deactivated. An agent is a way for somebody to act, not a second somebody.

This replaces the single god-mode operator the product started with. That was a
deliberate simplification and it was worth having for as long as the answer to
*who else uses this* was *nobody*; project-scoped access is the likeliest second
wish of anyone running a log store, and retrofitting it would mean opening every
read path a second time. What the model still refuses is everything above it:
there are no teams, no per-entry permissions, no roles an operator defines, no
sharing links, and no directory to federate with.

**Nobody is deleted.** An account that should not be used is deactivated, so that
every project assignment and every record of who changed something keeps pointing
at somebody. There is always at least one active administrator, and the last one
can be neither deactivated nor stripped of the role.

**What anybody changed is written down.** Everything that alters an
installation's configuration — a project, a group, a token, a retention window,
an alert switch, somebody's access, somebody's role — is recorded with who did
it, and an act an agent performed says so rather than naming the person whose
authority it borrowed. With one operator there was nothing to ask; with several,
*who deleted that project* and *who rotated that token* are questions a product
that cannot answer them has no business being trusted with. The entries
themselves are never in it: they are written once and never altered.

## Publicly reachable by design

A logaffe installation is meant to be put on the open internet and be safe there.
Four surfaces are publicly exposed:

- the **web UI**,
- the **MCP endpoint** for AI-agent access,
- the **ingestion endpoint** for applications shipping logs,
- the **sample endpoint** for the collectors reporting on their machines.

Requiring a VPN, Tailscale, an SSH tunnel, or a reverse-proxy auth layer in front
of logaffe is explicitly *not* an acceptable answer to security questions. The
system has to be hardened enough that hosting it on a public cloud host, reached
over plain HTTPS, is a sound default. Security is part of the product, not an
exercise left to the operator's network setup.

## Trust boundaries

### Senders are trusted, and they are the operator's own applications

logaffe is a central log store for applications the operator runs themselves. It
is not a drop-off point for arbitrary third-party tools of unclear provenance,
and it is not a hosted logging service for other people's software. Every sender
holds an ingest token the operator issued for a project the operator created.

Abuse protection on the ingestion endpoint — rate limits, payload size caps,
per-project quotas — therefore exists to keep an unauthenticated flood or a
misbehaving deployment from filling the store, not to defend against the sending
applications themselves.

### Log content is untrusted data

The sender being trusted says nothing about what a log entry contains.
Applications routinely log text that originated outside them: usernames from
failed logins, requested paths, headers, user agents, malformed request bodies.
An outsider needs no access to the operator's systems for their text to end up
verbatim in the log store — an HTTP request to any exposed application is
enough.

Because that stored text is later read by an AI agent operating with its owner's
access, log content is a prompt-injection surface, and for this product it is
the normal case rather than an edge case. Two consequences follow:

- Agent access over MCP is **read-only by default**.
- Log data is presented to agents as **untrusted data, never as instructions**,
  and the agent interface is designed so that content cannot be mistaken for
  direction from the operator.

## Setup and the first administrator

**The first administrator comes out of the configuration.** Whoever installs
names them — a name, an email address and a bootstrap token — before the first
start, and the installation creates them on the one start where it holds no
identity. There is no claim, no first-run screen, and no publicly reachable act
that establishes an account
([ADR 0054](./docs/adr/0054-the-first-administrator-comes-from-the-environment.md)).

The token rather than a password, because a password in a compose file is a
password in a shell history and in `docker inspect`, and because it is the
credential a human reuses. It is exchanged once in the browser for a password and
a session, and it stays valid afterwards as that administrator's own user token.
**From the second start the variables are ignored**, whatever they say, so an
installation that already has an administrator cannot be bootstrapped again by
editing a file.

An installation that starts with nothing and was told nothing starts anyway and
says in its log that nothing can authenticate. An installation given a bootstrap
secret it will not accept — too short, or an address that is not one — does not
start, the way a failed migration does not.

**Everybody after the first arrives by invitation.** An administrator invites an
address, the person sets their own password from a one-time link, and they begin
with an account and no project access until somebody gives them some. Recovering
a forgotten password and changing an address work the same way, and all three
need mail — which is the one external dependency this product takes
([ADR 0053](./docs/adr/0053-transactional-email-is-an-optional-capability-of-the-installation.md)).
An installation with no SMTP configured is healthy and complete in every other
respect; only those three acts refuse, and they say why.

**The second factor is offered, not required.** A user enrols a TOTP
authenticator, and takes the sheet of backup codes that comes with it, whenever
they decide to — from their own settings, behind their own password — and can
turn it off again. It is deliberately not forced. Requiring it buys account
strength at the price of an enrolment that cannot be completed by somebody
without an authenticator to hand, and a forced enrolment is the one most likely to
be done badly. This is a real concession: an account on the public internet behind
a password alone is weaker than the same account behind two factors. What the
product owes in return is that the choice is never made by accident — a user with
no second factor is told so for as long as that is true — and that the sign-in
throttle stands either way, per account and per source
([ADR 0056](./docs/adr/0056-sign-in-is-throttled-per-account-and-per-source.md)).

**There is always a way back in from the host.** An installation whose
administrators are all locked out — forgotten passwords, a lost authenticator
with the backup codes gone too — would otherwise be lost. Whoever has access to
the machine logaffe runs on can therefore run **Host Recovery**, which removes
every identity on the installation and leaves everything else — projects, groups,
ingest tokens, settings, entries — untouched; the way back in afterwards is the
bootstrap from configuration. It is one operation for every case, it now costs
every account rather than one, and it is deliberately host-local: reachable from
the Docker host, never over the network
([ADR 0058](./docs/adr/0058-host-recovery-removes-every-identity.md)). See
[`docs/setup.md`](./docs/setup.md).

## Core capabilities

### 1. Low-friction log ingestion

Getting logs into logaffe must be easy — this is a primary product goal, not an
implementation detail.

- **Structured logging is assumed** — but only as an envelope. An entry arrives
  as a discrete event carrying a timestamp, a level and a message, which is what
  every .NET logging framework already produces. logaffe does not read log files
  and does not parse text into fields.
- **Structured messages are not assumed.** A plain sentence with no named
  properties is a complete entry, not a degraded one, and no application has to
  rewrite its log statements into message templates to start delivering.
  Applications that already write templated messages get their properties stored
  and searchable; that is a reward, never a requirement.
- Applications are **not** expected to have adopted OpenTelemetry properly.
- The migration path from "we write log files locally" to "we ship logs to
  logaffe" should be short and low-risk — it adds a sink to the logging the
  application already does, and takes nothing away.

The first supported ingestion path is .NET backend applications, and **Serilog is
the best-supported one of those**. The wire format is Serilog's own compact
format, so a Serilog application is a configuration change rather than an
integration. Applications on `Microsoft.Extensions.Logging` are supported to the
same depth through an `ILoggerProvider`, and any other runtime can deliver with
`curl`.

**logaffe is additive, not a replacement.** An application keeps its local file
logging and delivers to logaffe in addition. Nothing has to be switched off to
try logaffe out, and an application that loses its connection to logaffe still
has its logs where it always had them. This is what keeps delivery simple:
shipping logs is fire-and-forget, must never block or slow down the sending
application, and does not require durable client-side buffering or delivery
guarantees. Central logging is a convenience layer on top of local logging, not
the system of record.

**Transport.** The primitive is a plain HTTP endpoint accepting a batch of log
entries as JSON. It is language-neutral and simple enough to drive with `curl`,
so any runtime can deliver logs without a dedicated client. Full OpenTelemetry /
OTLP is deliberately not the primary path — requiring it would contradict the
premise that applications have not adopted it.

On top of that primitive, .NET applications get convenience packages: a Serilog
sink and an `ILoggerProvider`, so that switching an existing file-logging
application over is a configuration change rather than a rewrite.

**Authentication.** Each project has its own ingest token. Tokens are
write-only — they permit delivering logs and grant no read access whatsoever —
and can be rotated by the operator. Projects are created explicitly by the
operator; there is no implicit project creation on first delivery. In practice
the token *is* the project as far as a sending application is concerned.

### 2. AI-agent access to logs

Making the log data accessible to AI agents is the second core capability, on
equal footing with the web UI. Access is provided over **MCP**, publicly
reachable and authenticated. Agents should be able to query and read project
logs so that log analysis, troubleshooting, and summarization can be delegated
rather than done by hand in a search box. The agent queries through the same
surface as the web UI — see [`docs/querying.md`](./docs/querying.md) for what it
can ask and [`docs/mcp.md`](./docs/mcp.md) for how it connects and what it
cannot.

An agent acts on the behalf of the person who owns it, sees exactly the projects
that person sees, and is, alongside them, a first-class consumer of the system.

**Agent access is initiated by its owner.** The agent looks into the logs because
they ask it to — while fixing a bug, or on a request such as "check
project *mysupertestapp* and tell me whether there were critical errors in the
last three days". The agent does not watch the log stream on its own and does
not act unprompted. Passive, continuously running agent monitoring is not part
of the product.

### 3. Multi-project separation

Multi-project capability is built in from the start, not retrofitted:

- Logs are assigned to a project at ingestion time.
- Projects are kept separate in storage, in the web UI, and in agent access.
- Retention is configured per project.

**Projects can be grouped.** Twenty projects is a list, and one product's staging
and production sitting under one heading is what makes it a readable one, so the
operator may put projects into named groups. A group exists so that a project is
found and for nothing else: it holds no retention window, no token and no query,
and two projects listed under one word are as separate as they were before. See
[`docs/projects.md`](./docs/projects.md).

**Retention is time-based.** A project keeps its logs for a configured period,
after which they are removed. Time is the only limit; there are no size or row
quotas, no "drop oldest when full", and no interaction between different limits.
Keeping this logic trivially simple is a deliberate choice — retention is a
detail the operator should be able to reason about in one sentence.

The period is the operator's to set **up to a ceiling of one year that the
installation cannot raise**, so that "not a multi-year archive" stays a property
of the product rather than a hope about how it is configured. Below that ceiling
the operator is not argued with, they are **told what it costs**: the field says
what the window will hold at this project's own measured rate, beside what the
installation holds today and what its disk has left. A number of days was never a
bound on disk — a week of a noisy project is more store than a year of a quiet
one — so the arithmetic does the work the ceiling used to do badly
([ADR 0048](./docs/adr/0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)).
See [`docs/projects.md`](./docs/projects.md).

### 4. Web UI

A single-page web application is the human entry point: browsing, searching, and
filtering logs, with project separation reflected throughout the interface. It
reads through the same query surface the agent uses, rather than a richer one of
its own — see [`docs/querying.md`](./docs/querying.md). What the operator
actually sees, and how it behaves, is [`docs/ui.md`](./docs/ui.md).

**Following logs live** is done by polling — refreshing the current view every
few seconds, on the order of five. Push-based streaming (SSE, WebSockets) is
deliberately not used: an installation of this size has a handful of open views
at a time, so polling is cheap and avoids a whole class of connection-lifecycle,
proxy, and reconnect problems on a publicly exposed deployment.

### 5. What the machine was doing

Logs say what an application did; they do not say that the machine it ran on had
been out of memory for twenty minutes. That is the question an operator asks
immediately after reading an error, and it is the one the log store cannot
answer.

logaffe therefore keeps **the numbers a machine reports about itself** — the
processor, the memory, the load, and how full its filesystems are — sampled once
a minute by a small **collector** the operator runs on each machine. A project
names the **host** it runs on, and that is the whole of the relation: the log
view draws a band over the entries showing what the machine was doing across
exactly the range the filters already state, and the agent can ask a host what it
reported over a range.

This is deliberately **not a metrics system**. The set of numbers is closed:
there is no metric to define, no label to choose, no query language, and no
dashboard to arrange. Custom counters, latency histograms and request rates are
the shape this was designed against rather than a later phase of it, because a
labelled series moves the limit on how much data exists out of the installation
and into the discipline of whoever writes the labels — and everything else here
is bounded by the installation. See [`docs/metrics.md`](./docs/metrics.md).

The collector is a second thing to deploy, on every machine that reports, and
that is the real cost of this capability. It is paid because an application
cannot see the machine it shares with four others, and a number that is wrong in
a way nobody notices is worse than no number.

### 6. Saying so when something is wrong

Everything above waits to be asked, and for reading what the logs say that is
right. But four things can be true of an installation that nobody will go
looking for, precisely because the whole point of them is that the operator does
not yet know: the store is filling up, an application has stopped delivering, a
project is suddenly writing far more than it does, and a project has started
failing far more than it does. The first ends in a database that stops accepting
writes, the second is usually how a self-hoster finds out that a service died,
the third is what fills the disk while nobody is watching, and the fourth is what
an operator would otherwise run a second piece of software to be told.

logaffe therefore **sends the operator a notification**, on **four conditions
and no others** — the store filling up, a project going quiet, a project
flooding, and a project failing. They are named in the product rather than written by the operator:
there is no rule language, no threshold to type in, and no alert on a saved
query, and every threshold is derived from the project's own recent history so
that nothing has to be guessed at
([ADR 0050](./docs/adr/0050-the-alert-conditions-are-a-closed-set.md)). Each is
off until it is switched on.

**A late true alarm beats a false one**, and the design spends real sensitivity
to buy that: no condition fires before a project has a fortnight of history, every
rate has an absolute floor beneath it, only closed hours are judged, a normal is a
median by hour of the day rather than an average over it, and one notification is
followed by six hours of silence. Nothing is sent when a condition clears, because
silence is not information.

**A notification carries numbers and names — never log content.** The project, the
condition, the numbers behind it, and a link into the log view with the filters
already set. No message, no exception, no property value. It is the one thing in
this product that travels outward on its own, to a service the operator does not
run, and log content is untrusted text that would arrive there rendered as prose
([ADR 0049](./docs/adr/0049-a-notification-carries-numbers-and-names-never-log-content.md)).
The reading happens behind the session, one click away, which is where the
operator wanted to be anyway.

**One notifier is supported, and it is ntfy** — it pushes, it needs no inbound
port, it is self-hostable, and it reaches a phone. A notification that is a name,
three numbers and a URL formats identically everywhere, so there is nothing a
second integration would render better. Email stays absent, and the reason has
been corrected: the product does have addresses now and can send mail
([ADR 0053](./docs/adr/0053-transactional-email-is-an-optional-capability-of-the-installation.md)),
but what this capability is for is a push to a phone, and mail is for identity
transactions only.

None of this reads an entry. The conditions run on a small tally the installation
keeps as deliveries arrive — how many entries a project received in an hour —
which is also what tells the operator what a retention window will cost
([ADR 0047](./docs/adr/0047-the-volume-history-is-tallied-as-it-arrives.md)).

## Non-goals

- **No content filtering or scrubbing before ingestion.** logaffe does not
  inspect log data for sensitive or otherwise problematic content and does not
  require callers to strip anything out beforehand. Log entries are stored as
  delivered, with exactly one exception: a message or exception that exceeds its
  size cap is cut at the cap and visibly flagged as truncated, because the
  entries that overrun a cap are the large stack traces an operator went looking
  for. Nothing is ever dropped, reformatted or altered on account of what it
  says. See [`docs/ingestion.md`](./docs/ingestion.md).
- **No large-scale log platform.** Massive retention windows, billions of
  entries, and horizontal scale-out are explicitly out of scope.
- **Not a logging service for third parties.** logaffe stores logs from the
  operator's own applications, not from arbitrary foreign tools or other
  people's software.
- **No requirement of full OpenTelemetry adoption** in the applications that
  send logs.
- **No user model beyond users, one role, and project access.** No teams, no
  organizations, no permissions an operator defines, no roles beyond
  administrator, no per-project role, no permission on an individual entry, no
  sharing link, and no SSO, LDAP or OIDC to federate with. Access is a list of
  projects against a person, and that is the whole of it.
- **No reliance on network-level protection.** logaffe does not assume it sits
  behind a VPN, Tailscale, or an authenticating reverse proxy, and it will not
  treat "run it on a private network" as a security answer.
- **No alert an operator defines, and no alerting beyond the four conditions.**
  Capability 6 is a closed set, and the shape refused with it is the usual one:
  no rule language, no threshold to type, no alert attached to a saved query or a
  filter, no severity model, no incident, no acknowledging, no escalation, no
  on-call rota, and no second notifier. Adding a fifth condition is a change to
  [ADR 0050](./docs/adr/0050-the-alert-conditions-are-a-closed-set.md) rather
  than a field in a form.
- **No notification carrying log content.** No message text, no exception, no
  property value, no digest of the day's errors, and nothing that groups alerts
  by what the entries say. A notification is a reason to open a screen, and the
  screen is behind the session
  ([ADR 0049](./docs/adr/0049-a-notification-carries-numbers-and-names-never-log-content.md)).
- **No agent that watches.** Nothing analyses entries in the background, no agent
  is handed a log stream to keep an eye on, and no notification is written by a
  model. A condition counts rows it was handed as they arrived. Every look into
  what the logs actually *say* still starts with the operator or their agent.
- **No metrics system, and no metric an operator defines.** The set of numbers a
  host reports is closed: no custom counters, gauges or histograms, no labels, no
  query language, and no dashboard. Wanting those is a reason to run a tool that
  does them well beside logaffe.
- **No application or runtime metrics.** Request rates, latency percentiles, GC
  pauses and heap sizes are not collected, and the client packages do not sample
  the process they live in. Metrics are about the machine.
- **No pull-based collection.** No OTLP endpoint, no Prometheus scrape, no
  `/metrics` for anyone to poll. Collectors push, for the reason senders push:
  an installation on the open internet that reaches back into the operator's
  machines is a different security posture than one that only ever receives.
- **No push-based live streaming.** Following logs live is polling, not SSE or
  WebSockets.
- **No OTLP as the primary ingestion path.** Applications are not expected to
  speak OpenTelemetry to talk to logaffe.

## Technical direction

Why the non-obvious ones were chosen over their alternatives is recorded as ADRs
in [`docs/adr/`](./docs/adr/), and how the repository is laid out around them is
[`docs/codebase.md`](./docs/codebase.md).

- **Backend:** .NET 10, in four layers. See
  [`docs/codebase.md`](./docs/codebase.md)
- **Frontend:** React, as a single-page application, drawn with Tailwind CSS v4
  as the token layer, Base UI as the primitive layer and shadcn components this
  repository owns — the same foundation and the same token values as planaffe,
  so that the two read as one family. See [`docs/ui.md`](./docs/ui.md)
- **Storage:** PostgreSQL, tuned for high log-row counts through appropriate
  indexing and schema design — sized for a moderate, bounded data set rather
  than unbounded growth. See [`docs/storage.md`](./docs/storage.md)
- **Data access:** EF Core owns the schema and the self-applying migrations, and
  serves everything except the log entries; the log path writes through Npgsql's
  binary `COPY` and reads through hand-written SQL with Dapper
- **Ingestion:** HTTP endpoint taking batches of newline-delimited CLEF,
  authenticated with per-project write-only tokens; Serilog sink and
  `ILoggerProvider` packages for .NET on top. See
  [`docs/ingestion.md`](./docs/ingestion.md)
- **logaffe's own logs:** Serilog to rolling files on the mounted host volume.
  logaffe does not log into itself — the failures worth diagnosing are the ones
  in which it could not record anything
- **Metrics:** a closed set of host readings, pushed once a minute by a separate
  containerized collector against a write-only host token. See
  [`docs/metrics.md`](./docs/metrics.md)
- **Alerting:** three named conditions evaluated on the closed hour against a
  tally the ingestion path keeps, delivered to ntfy and to nothing else, carrying
  no log content. See [`docs/alerts.md`](./docs/alerts.md)
- **Agent interface:** MCP, exposed publicly and authenticated
- **Live updates:** polling on the order of five seconds, no push streaming
- **Deployment:** containerized, runnable with Docker Compose as the standard
  way to operate it — including on a public cloud host
- **Authentication:** users signing in with an email address and a password
  hashed with Argon2id, on server-side browser sessions; an optional TOTP second
  factor and its backup codes are each user's own. The first administrator comes
  from the environment on the first start, everybody after them by invitation.
  See [`docs/sign-in.md`](./docs/sign-in.md)
- **Transactional email:** SMTP the operator configures, used for invitations,
  password recovery and address changes and for nothing else — optional, with no
  queue and no second container. See [`docs/setup.md`](./docs/setup.md)
- **Distribution:** the project is intended to be released as open source

## Operating an installation: upgrades and backup

Self-hosted software is only as good as its operational story, so upgrades and
backup are part of the product rather than an afterthought.

**Upgrades** are `docker compose pull` followed by `docker compose up`. Schema
migrations apply themselves on startup; there is no separate migration step for
the operator to run and no manual sequence to follow between versions.

**Backup is the operator's responsibility** — logaffe does not run backups,
schedule them, or ship snapshots anywhere. What logaffe owes the operator is
that backing up is *simple to do and clearly documented*:

- Any state that does not live in the database — configuration, secrets — is
  kept on the host in a mounted volume, never inside the container image. A
  container can be thrown away and recreated without losing anything.
- **Both stores are needed, and a database alone is not a backup**: the key that
  makes stored tokens readable lives on the volume. logaffe therefore provides a
  command that writes both halves into one artifact, which the operator runs,
  places and schedules themselves. See
  [`docs/operations.md`](./docs/operations.md).

**Not everything is equally worth saving.** Logs are expendable: they are
short-lived by design, they are additive to the applications' own local logs,
and losing them costs little. The accounts and the configuration are not —
losing those means losing access to the installation. A backup strategy
that covers only the small, slow-changing part is a legitimate choice, and the
documentation should say so.

## Guiding principles

1. **Ingestion friction is the adoption barrier.** Every decision about the
   ingestion path is judged by how easy it is for an existing file-logging
   application to switch.
2. **Meet applications where they are.** An application must log through a
   logging framework, and that is the whole of what is asked. Message text that
   was never structured stays a supported reality rather than a problem to be
   fixed first.
3. **Agents are first-class consumers.** The log data model and query surface
   are designed for machine consumption, not only for a human-facing UI.
4. **Bounded by design.** Limited retention and moderate volume are deliberate
   constraints that keep the system simple to run.
5. **Access is a list of projects, and there is one filter.** Every read narrows
   to the projects the identity behind the request can reach, through one
   component rather than at each call site, because a call site that forgets is a
   leak. Anything that would need a richer permission model than *this person may
   see that project* is out of scope by definition.
6. **Safe on the open internet.** Every publicly exposed surface — UI, MCP,
   ingestion, samples — is designed to withstand being reachable by anyone,
   without a network-level safety net in front of it.
7. **Additive, not authoritative.** logaffe sits on top of the applications'
   existing local logging instead of replacing it. That keeps delivery
   fire-and-forget and keeps the cost of losing log data low.
8. **Nothing reads unasked.** Every look into what the logs say — by the operator
   or by their agent — is initiated by the operator, and nothing watches, analyses
   or summarizes in the background. The four conditions of capability 6 are not
   an exception to this: they count rows as they arrive, they never read an entry,
   and the operator asked for them once, when they switched them on.

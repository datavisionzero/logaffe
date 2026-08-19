# logaffe — Product Vision

## In one sentence

logaffe is a self-hostable, central logging tool for a single operator and their
AI agent: it collects logs from many applications, keeps them separated by
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

- One operator running a handful of self-hosted backend services — plus their
  AI agent.
- Primarily .NET backend applications that today log to local files.
- A single deployment hosting on the order of 10–30 projects, each with a
  deliberately limited retention window.

logaffe is not designed for multi-year log archives or billions of rows. Log
volume per project is intentionally capped, and short retention is the expected
mode of operation.

## The operator model

logaffe is a **single-operator, god-mode** system. There is exactly one human
account, and it can see and do everything. There are no additional users, no
roles, no permissions, no teams, no sharing, no invitations. The audience is one
person and the AI agent working on their behalf.

This is a deliberate simplification, not a stage on the way to multi-tenancy. It
removes an entire dimension of complexity from the data model, the UI, and the
agent interface, and it is what makes the rest of the product small enough to
stay simple.

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

Because that stored text is later read by an AI agent operating with god-mode
access, log content is a prompt-injection surface, and for this product it is
the normal case rather than an edge case. Two consequences follow:

- Agent access over MCP is **read-only by default**.
- Log data is presented to agents as **untrusted data, never as instructions**,
  and the agent interface is designed so that content cannot be mistaken for
  direction from the operator.

## Setup and the installation claim

A fresh installation is **unclaimed**. The claim is a flow in the web UI through
which the operator takes the installation and establishes their account, and what
it establishes is a **password**. It is one act: until it completes the
installation belongs to nobody, and nothing about it is half-done.

**How the claim is guarded is decided by whoever installs**, before the first
start, because they are the one who knows which of the two they can actually
perform.

- **A claim secret.** The installation is not claimable by anyone who cannot
  present it. Whoever installs either sets it beforehand or leaves it to the
  installation, which draws one on its first start and writes it where the host
  can read it. There is no deadline, because a door that is locked does not need
  a clock: an installation that is spun up and then forgotten is not an open
  door, and the operator can claim it a week later.
- **An open window.** No secret, and anyone who can reach the installation may
  claim it — for a short, time-limited window after it first runs. There is no
  data to take yet, but the installation itself can be taken, so the exposure is
  real rather than nil; it is accepted because it is narrow and because it is
  recoverable. The time limit is the whole of what keeps it narrow, and when it
  lapses, claiming over the network is over until the operator intervenes on the
  host.

The claim secret is the default. The window is for the installation where reading
a file or a container log is not on offer — a one-click host, a panel — and it is
the older of the two rather than the better one. The secret is also what makes an
**unattended installation** work: whoever or whatever performs it writes the
configuration before the first start and hands the secret over, and the operator
claims when they get to it rather than within minutes of the container coming up.

**The second factor is offered, not required.** An operator enrols a TOTP
authenticator, and takes the sheet of backup codes that comes with it, whenever
they decide to — from the settings, behind their own password — and can turn it
off again. It is deliberately not part of the claim. Requiring it there buys
account strength at the price of a claim that cannot be finished by someone
without an authenticator to hand, and a forced enrolment is the one most likely
to be done badly. This is a real concession: one god-mode account on the public
internet behind a password alone is weaker than the same account behind two
factors, and nothing else in the product compensates for it. What the product
owes in return is that the choice is never made by accident — an installation
whose second factor is off says so in the UI for as long as it is off — and that
the sign-in rate limits stand either way.

**There is always a way back in from the host.** With a single account, no email
and no reset channel, a forgotten password — or a lost second factor with the
backup codes gone too — would otherwise mean losing the installation. Whoever has
access to the machine logaffe runs on can therefore run **Host Recovery**, which
returns the installation to unclaimed and opens the way back in the form that
installation is configured for, a fresh claim secret or a fresh window, while
keeping its projects, tokens and entries. It is one operation for every case, and
it is deliberately host-local: it is reachable from the Docker host, never over
the network. See [`docs/setup.md`](./docs/setup.md).

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

The agent acts on the operator's behalf and is, alongside the operator, the
second first-class consumer of the system.

**Agent access is operator-initiated.** The agent looks into the logs because
the operator asks it to — while fixing a bug, or on a request such as "check
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

The period is the operator's to set **up to a ceiling the installation cannot
raise**, so that "not a multi-year archive" stays a property of the product
rather than a hope about how it is configured. See
[`docs/projects.md`](./docs/projects.md).

### 4. Web UI

A single-page web application is the human entry point: browsing, searching, and
filtering logs, with project separation reflected throughout the interface. It
reads through the same query surface the agent uses, rather than a richer one of
its own — see [`docs/querying.md`](./docs/querying.md). What the operator
actually sees, and how it behaves, is [`docs/ui.md`](./docs/ui.md).

**Following logs live** is done by polling — refreshing the current view every
few seconds, on the order of five. Push-based streaming (SSE, WebSockets) is
deliberately not used: with a single operator there is at most one open view at
a time, so polling is cheap and avoids a whole class of connection-lifecycle,
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
- **No multi-user features.** No user management, roles, permissions, teams,
  sharing, or invitations. One operator, full access.
- **No reliance on network-level protection.** logaffe does not assume it sits
  behind a VPN, Tailscale, or an authenticating reverse proxy, and it will not
  treat "run it on a private network" as a security answer.
- **No alerting.** logaffe does not send notifications, evaluate alert rules, or
  page anyone, and no agent watches the logs in the background to do it either.
  Looking into the logs always starts with the operator. Alerting may be
  revisited later; it is not part of the initial product. The samples of
  capability 5 do not reopen this — they only mean that the day it is revisited,
  there is something to evaluate.
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
- **Frontend:** React, as a single-page application. See
  [`docs/ui.md`](./docs/ui.md)
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
- **Agent interface:** MCP, exposed publicly and authenticated
- **Live updates:** polling on the order of five seconds, no push streaming
- **Deployment:** containerized, runnable with Docker Compose as the standard
  way to operate it — including on a public cloud host
- **Authentication:** a single operator account with a password, established
  through the claim and guarded by a claim secret or a time-limited window, with
  no username and no email address; an optional TOTP second factor and its backup
  codes are enrolled afterwards. See [`docs/sign-in.md`](./docs/sign-in.md)
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
and losing them costs little. The operator account and the configuration are
not — losing those means losing access to the installation. A backup strategy
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
5. **One operator, no user model.** Every feature is designed for a single
   god-mode account; anything that would only make sense with multiple users is
   out of scope by definition.
6. **Safe on the open internet.** Every publicly exposed surface — UI, MCP,
   ingestion, samples — is designed to withstand being reachable by anyone,
   without a network-level safety net in front of it.
7. **Additive, not authoritative.** logaffe sits on top of the applications'
   existing local logging instead of replacing it. That keeps delivery
   fire-and-forget and keeps the cost of losing log data low.
8. **Nothing happens unasked.** Every look into the logs — by the operator or by
   their agent — is initiated by the operator.

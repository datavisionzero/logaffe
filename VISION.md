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
Three surfaces are publicly exposed:

- the **web UI**,
- the **MCP endpoint** for AI-agent access,
- the **ingestion endpoint** for applications shipping logs.

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

## Guided setup and the installation claim

A fresh installation is **unclaimed**. Setup is a guided flow in the web UI
through which the operator claims the installation and establishes their account:

- credentials,
- two-factor authentication as part of the guided setup, not an optional extra
  buried in settings,
- backup codes, presented and confirmed during setup.

While an installation is unclaimed, **anyone who can reach it may start the
claim**. There is no data to take yet, but the installation itself can be taken,
so the exposure is real rather than nil — it is accepted because it is narrow and
because it is recoverable, not because it is absent. The larger risk is an
installation that is spun up and then forgotten: it would sit unclaimed and
claimable indefinitely.

Therefore the claim window is **time-limited**. If nobody claims the installation
within that window, claiming over the network is no longer possible and the
operator has to intervene locally on the host to re-enable it. An abandoned
installation must not remain an open door.

**There is always a way back in from the host.** With a single account protected
by two-factor authentication and exposed to the public internet, losing the
second factor and the backup codes would otherwise mean losing the installation.
Whoever has access to the machine logaffe runs on can therefore run **Host
Recovery**, which returns the installation to unclaimed and arms a fresh claim
window while keeping its projects, tokens and entries. It is one operation for
both cases, and it is deliberately host-local: it is reachable from the Docker
host, never over the network. See [`docs/setup.md`](./docs/setup.md).

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
its own — see [`docs/querying.md`](./docs/querying.md).

**Following logs live** is done by polling — refreshing the current view every
few seconds, on the order of five. Push-based streaming (SSE, WebSockets) is
deliberately not used: with a single operator there is at most one open view at
a time, so polling is cheap and avoids a whole class of connection-lifecycle,
proxy, and reconnect problems on a publicly exposed deployment.

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
  revisited later; it is not part of the initial product.
- **No push-based live streaming.** Following logs live is polling, not SSE or
  WebSockets.
- **No OTLP as the primary ingestion path.** Applications are not expected to
  speak OpenTelemetry to talk to logaffe.

## Technical direction

Why the non-obvious ones were chosen over their alternatives is recorded as ADRs
in [`docs/adr/`](./docs/adr/).

- **Backend:** .NET 10
- **Frontend:** React, as a single-page application
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
- **Agent interface:** MCP, exposed publicly and authenticated
- **Live updates:** polling on the order of five seconds, no push streaming
- **Deployment:** containerized, runnable with Docker Compose as the standard
  way to operate it — including on a public cloud host
- **Authentication:** a single operator account with a password, a TOTP second
  factor and backup codes, established through the guided claim flow, with no
  username and no email address. See [`docs/sign-in.md`](./docs/sign-in.md)
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
   ingestion — is designed to withstand being reachable by anyone, without a
   network-level safety net in front of it.
7. **Additive, not authoritative.** logaffe sits on top of the applications'
   existing local logging instead of replacing it. That keeps delivery
   fire-and-forget and keeps the cost of losing log data low.
8. **Nothing happens unasked.** Every look into the logs — by the operator or by
   their agent — is initiated by the operator.

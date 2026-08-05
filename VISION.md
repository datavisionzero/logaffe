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

A logaffe instance is meant to be put on the open internet and be safe there.
Three surfaces are publicly exposed:

- the **web UI**,
- the **MCP endpoint** for AI-agent access,
- the **ingestion endpoint** for applications shipping logs.

Requiring a VPN, Tailscale, an SSH tunnel, or a reverse-proxy auth layer in front
of logaffe is explicitly *not* an acceptable answer to security questions. The
system has to be hardened enough that hosting it on a public cloud host, reached
over plain HTTPS, is a sound default. Security is part of the product, not an
exercise left to the operator's network setup.

## Guided setup and the installation claim

A fresh installation is **unclaimed**. Setup is a guided flow in the web UI
through which the operator claims the instance and establishes their account:

- credentials,
- two-factor authentication as part of the guided setup, not an optional extra
  buried in settings,
- backup codes, presented and confirmed during setup.

While an installation is unclaimed, **anyone who can reach it may start the
claim** — there is nothing to protect yet, so this is not a risk in itself. The
risk is an installation that is spun up and then forgotten: it would sit
unclaimed and claimable indefinitely.

Therefore the claim window is **time-limited**. If nobody claims the instance
within that window, claiming over the network is no longer possible and the
operator has to intervene locally on the host to re-enable it. An abandoned
installation must not remain an open door.

## Core capabilities

### 1. Low-friction log ingestion

Getting logs into logaffe must be easy — this is a primary product goal, not an
implementation detail.

- Applications are **not** expected to have adopted OpenTelemetry properly.
- Classic, mostly unstructured log output (a level, a timestamp, a text message)
  is a first-class input, not a degraded fallback.
- Richer structured input is supported where an application already provides it,
  but it is never a precondition for using logaffe.
- The migration path from "we write log files locally" to "we ship logs to
  logaffe" should be short and low-risk.

The first supported ingestion path is .NET backend applications.

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
rather than done by hand in a search box.

The agent acts on the operator's behalf and is, alongside the operator, the
second first-class consumer of the system.

### 3. Multi-project separation

Multi-project capability is built in from the start, not retrofitted:

- Logs are assigned to a project at ingestion time.
- Projects are kept separate in storage, in the web UI, and in agent access.
- Retention and volume limits are applied per project.

### 4. Web UI

A single-page web application is the human entry point: browsing, searching, and
filtering logs, with project separation reflected throughout the interface.

**Following logs live** is done by polling — refreshing the current view every
few seconds, on the order of five. Push-based streaming (SSE, WebSockets) is
deliberately not used: with a single operator there is at most one open view at
a time, so polling is cheap and avoids a whole class of connection-lifecycle,
proxy, and reconnect problems on a publicly exposed deployment.

## Non-goals

- **No content filtering or scrubbing before ingestion.** logaffe does not
  inspect log data for sensitive or otherwise problematic content and does not
  require callers to strip anything out beforehand. Log lines are stored as
  delivered.
- **No large-scale log platform.** Massive retention windows, billions of
  entries, and horizontal scale-out are explicitly out of scope.
- **No requirement of full OpenTelemetry adoption** in the applications that
  send logs.
- **No multi-user features.** No user management, roles, permissions, teams,
  sharing, or invitations. One operator, full access.
- **No reliance on network-level protection.** logaffe does not assume it sits
  behind a VPN, Tailscale, or an authenticating reverse proxy, and it will not
  treat "run it on a private network" as a security answer.
- **No push-based live streaming.** Following logs live is polling, not SSE or
  WebSockets.
- **No OTLP as the primary ingestion path.** Applications are not expected to
  speak OpenTelemetry to talk to logaffe.

## Technical direction

- **Backend:** .NET 10
- **Frontend:** React, as a single-page application
- **Storage:** PostgreSQL, tuned for high log-row counts through appropriate
  indexing and schema design — sized for a moderate, bounded data set rather
  than unbounded growth
- **Ingestion:** HTTP endpoint taking JSON batches, authenticated with
  per-project write-only tokens; Serilog sink and `ILoggerProvider` packages for
  .NET on top
- **Agent interface:** MCP, exposed publicly and authenticated
- **Live updates:** polling on the order of five seconds, no push streaming
- **Deployment:** containerized, runnable with Docker Compose as the standard
  way to operate it — including on a public cloud host
- **Authentication:** a single operator account with two-factor authentication
  and backup codes, established through the guided claim flow
- **Distribution:** the project is intended to be released as open source

## Guiding principles

1. **Ingestion friction is the adoption barrier.** Every decision about the
   ingestion path is judged by how easy it is for an existing file-logging
   application to switch.
2. **Meet applications where they are.** Unstructured text logs are a supported
   reality, not a problem to be fixed first.
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

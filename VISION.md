# logaffe — Product Vision

## In one sentence

logaffe is a self-hostable, central logging tool that collects logs from many
applications, keeps them separated by project, and makes them accessible both to
humans through a web UI and to AI agents through a machine-facing interface.

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

- Operators and developers of a handful of self-hosted backend services.
- Primarily .NET backend applications that today log to local files.
- A single deployment hosting on the order of 10–30 projects, each with a
  deliberately limited retention window.

logaffe is not designed for multi-year log archives or billions of rows. Log
volume per project is intentionally capped, and short retention is the expected
mode of operation.

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

### 2. AI-agent access to logs

Making the log data accessible to AI agents is the second core capability, on
equal footing with the web UI. Agents should be able to query and read project
logs so that log analysis, troubleshooting, and summarization can be delegated
rather than done by hand in a search box.

### 3. Multi-project separation

Multi-project capability is built in from the start, not retrofitted:

- Logs are assigned to a project at ingestion time.
- Projects are kept separate in storage, in the web UI, and in agent access.
- Retention and volume limits are applied per project.

### 4. Web UI

A single-page web application is the human entry point: browsing, searching, and
filtering logs, with project separation reflected throughout the interface.

## Non-goals

- **No content filtering or scrubbing before ingestion.** logaffe does not
  inspect log data for sensitive or otherwise problematic content and does not
  require callers to strip anything out beforehand. Log lines are stored as
  delivered.
- **No large-scale log platform.** Massive retention windows, billions of
  entries, and horizontal scale-out are explicitly out of scope.
- **No requirement of full OpenTelemetry adoption** in the applications that
  send logs.

## Technical direction

- **Backend:** .NET 10
- **Frontend:** React, as a single-page application
- **Storage:** PostgreSQL, tuned for high log-row counts through appropriate
  indexing and schema design — sized for a moderate, bounded data set rather
  than unbounded growth
- **Deployment:** containerized, runnable with Docker Compose as the standard
  way to operate it
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

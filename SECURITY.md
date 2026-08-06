# Security Policy

logaffe is designed to be exposed directly to the public internet — its web UI,
its MCP endpoint, and its ingestion endpoint are all meant to be reachable by
anyone. Security is therefore treated as part of the product rather than as
something the operator is expected to solve with a VPN or an authenticating
reverse proxy in front of it.

## Reporting a vulnerability

Please report security issues privately through GitHub's private vulnerability
reporting: open the **Security** tab of `datavisionzero/logaffe` and choose
**Report a vulnerability**.

Do not open a public issue for a suspected vulnerability, and do not disclose it
publicly before a fix is available.

Please include:

- what the issue is and which surface it affects (web UI, MCP endpoint,
  ingestion endpoint, setup/claim flow, container image),
- the steps needed to reproduce it,
- the version or commit you tested against,
- the impact you believe it has.

## What to expect

This is a small project maintained by a single person. Reports are acknowledged
as soon as they are seen, and fixes are prioritized over other work. There is no
bug bounty.

## Supported versions

logaffe is pre-release. Only the current `main` branch receives security fixes;
there are no maintained release branches yet. This section will be updated once
versioned releases exist.

## Scope

In scope:

- authentication and the guided claim flow, including two-factor authentication
  and backup codes,
- the host-local recovery path,
- ingest token handling and project separation,
- anything that lets one project's log data be read through another project's
  credentials,
- prompt-injection paths through log content into agent access over MCP,
- the default configuration of the published container image and Compose setup.

Out of scope:

- weaknesses that require the attacker to already have host access to the
  machine logaffe runs on — the host-local recovery path is a documented and
  intentional escape hatch,
- sensitive data appearing in stored log lines. logaffe stores log content as
  delivered and performs no filtering or scrubbing; this is a documented
  non-goal,
- denial of service through a sender that legitimately holds an ingest token.
  Ingest tokens are issued by the operator to their own applications, which are
  trusted by design.

## No warranty

logaffe is provided under the MIT License, without warranty of any kind. See
[LICENSE](LICENSE).

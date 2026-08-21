# The Codebase

Every other document under `docs/` describes what logaffe does. This one
describes where that lives: how the repository is laid out, which project holds
what, which way the dependencies point, and what is built by which toolchain.

Two things are settled elsewhere and shape all of it. The backend is **four
layers** ([ADR 0030](./adr/0030-the-solution-is-four-layers-not-one-project.md)),
and the frontend is **React on its own toolchain**
([ADR 0001](./adr/0001-the-frontend-is-react-not-blazor.md)) — so the repository
carries two languages, and the one artifact an operator runs carries both.

## The shape of the repository

```
logaffe/
├─ .github/workflows/         ci on every push, release on every tag
├─ docs/                      the product, the decisions, and this
│  ├─ adr/
│  ├─ agents/
│  └─ api/openapi.json        the HTTP contract, checked in
├─ deploy/                    the two Dockerfiles, Compose, and nothing else
├─ src/
│  ├─ Logaffe.Domain/         the rules
│  ├─ Logaffe.Application/    the use cases and their ports
│  ├─ Logaffe.Infrastructure/ Postgres, secrets, the file log
│  ├─ Logaffe.Api/            HTTP, MCP, CLI, and the composition root
│  ├─ Logaffe.Collector/      the host collector, its own deployable
│  ├─ clients/                the three NuGet packages
│  └─ web/                    the single-page application
├─ tests/
│  ├─ Logaffe.UnitTests/
│  └─ Logaffe.IntegrationTests/
└─ Logaffe.slnx               plus global.json and the Directory.* properties
```

`src/` and `tests/` is the convention a .NET contributor arrives expecting, and
the open-source intent of `VISION.md` is reason enough to meet it rather than
invent something more descriptive.

## The four layers

Dependencies point inward and only inward: Domain depends on nothing, Application
on Domain, Infrastructure on Application, and Api on both of the outer two as the
composition root. Domain carries no package references at all, which is the
cheapest possible check that nothing has leaked into it.

**`Logaffe.Domain` holds the rules.** The entry and its level, the two clocks, the
message template and the rendered form, the trace and span as the byte lengths
they actually are, the caps and the truncation, the project with its retention
window and the group it is listed under, the two tokens with the identifier and
the alphabet they are written in,
the operator with the session, the backup code, the claim window and the claim
secret, the
filters with the cursor, and what a retention window costs — the per-entry and
per-row figures [Storage](./storage.md) measured, and the arithmetic that turns a
rate into bytes
([ADR 0048](./adr/0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)).
The test of whether something belongs here is stated
in ADR 0030: **anything the documents already state as a rule.** A retention
window that can be constructed above a year, or a search text that can be
constructed with two characters, is a rule that escaped.

**`Logaffe.Application` holds the use cases and the ports.** Authenticating a
presented token, ingesting a batch, searching, counting, fetching one entry, the
project, group and token acts, the claim and the sign-in, the retention sweep,
the backup and the recovery. Every one of them is reachable from more than one
adapter or is a candidate to become so, and none of them knows what it is being
called by — the first is called by both public endpoints and is the plainest
case. Beside them sit the ports — a writer and a reader for entries, stores for
the small relational rows, the cipher for whatever is sealed under the key on the
host volume, the id source, the password hasher, the TOTP, and what the store
says it occupies — which is the whole of what this layer asks the world for. The
clock is not among them:
`TimeProvider` is in the base class libraries, and a port over it would be an
abstraction over an abstraction.

**`Logaffe.Infrastructure` answers those ports.** EF Core declares every table
and owns the self-applying migrations, including the entry table's, so that there
stays exactly one place that creates schema
([ADR 0003](./adr/0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md)).
Beside it the log path: the binary `COPY` writer and the hand-written queries,
whose SQL is kept together in one folder because
[Storage](./storage.md) makes re-reading it the standing cost of changing an
index. Here too are the things that touch the host volume — the key that makes a
token readable ([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md))
and the rolling file log ([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md)).

**`Logaffe.Api` is the adapters and the composition root.** One binary that is
the server and the CLI both: the HTTP endpoints, the MCP tools — five for a
reading agent token and twenty-one for an administering one
([MCP](./mcp.md)) — the `backup`, `restore` and `recover` verbs that
[Operations](./operations.md) and [Setup](./setup.md) document, the
authentication of three different credentials, the rate limits every public
surface carries, and the static files of the built SPA. Its name understates it
and is kept for the convention.

The layer holds no rules and no queries of its own. This is where
[ADR 0012](./adr/0012-log-content-reaches-an-agent-as-data-never-as-prose.md) is
enforced and is therefore auditable: shaping an entry for a consumer happens in
an adapter and nowhere else, so the question of whether log content ever becomes
prose has one place to be answered.

## The frontend is built separately and joined once

`src/web/` is an ordinary Vite project with its own `package.json`, and nothing
in the .NET build knows it exists. Development runs the two side by side — the
Vite dev server against `dotnet run` — and the only place they are joined is the
`Dockerfile`, which builds the SPA in a Node stage and copies it into the
published output.

The alternative, an MSBuild target calling npm, would make `dotnet publish`
produce the whole artifact and make Node a requirement of every backend build.
Joining them in one place instead keeps the two toolchains ADR 0001 accepted from
becoming one that always runs both.

## The HTTP contract is an artifact, not an intention

ADR 0001 takes as its cost that the frontend cannot share types with the backend
and says the contract has to be written down and kept honest by tests rather than
by the compiler. `docs/api/openapi.json` is that document, and it is checked in.

**It is captured from a running installation, not generated at build.** The
build-time tooling starts the host to read the document out of it, which would
have meant every build running the migrations on startup. CI instead brings up a
Postgres, starts the installation, fetches the document it serves, and fails if
it differs from the one in the repository. The web client's API layer is
generated from the same file, which is what makes the contract load-bearing
rather than descriptive.

## The collector is a deployable, not a layer

`Logaffe.Collector` sits beside the four layers rather than inside them, and
references none of them. It reads a machine and posts numbers over HTTP
([ADR 0043](./adr/0043-metrics-come-from-the-host-not-from-the-application.md)),
which is the same relationship to the installation that a sending application
has — so it depends on the wire format and on nothing else, exactly as the client
packages do.

It ships as **its own container image**, built from a second `Dockerfile` under
`deploy/` and released on the same tag as the installation. That is a second
artifact in the release workflow and the one part of this product an operator
upgrades separately from `docker compose pull`, which is the cost ADR 0043
accepts.

**It was built last rather than first**, and the order was the argument: what a
collector *is* is what it posts, so it could not be written before the sample
endpoint it posts to — which needed the host, its token and the two tables of
[Storage](./storage.md#the-sample-tables) ahead of it.

**It carries no project references and no packages**, and both are load-bearing.
It references none of the four layers because it is not one of them; it carries
no packages because it runs on every machine the operator wants numbers from,
which makes its dependency list a surface on hosts nobody here administers.
HTTP and JSON are in the framework, and the whole of its logging is a timestamp
and a sentence on standard output.

What holds it to the installation is therefore the wire format alone, with no
compiler between them — so the test that they agree is a real one: a reading is
written by the collector and parsed by the installation's own parser, in one
process, and a member renamed on either side fails.

Its tests are the unit project's, because what a test of it needs is a directory
of files that look like `/proc` — not a machine and not a database. What would
need a real machine is the container's two mounts, which are a `docker run` line
rather than code.

## The client packages are three

[Ingestion](./ingestion.md) requires the Serilog sink and the `ILoggerProvider`
to behave identically under stress — a bounded in-memory queue dropping the
oldest, never throwing into the caller, never blocking it, and a flush with a
timeout on shutdown. That is one behaviour, so it is one project:

- **`Logaffe.Client`** — the delivery itself, and everything the promise above
  consists of.
- **`Logaffe.Serilog`** — `CompactJsonFormatter` pointed at the endpoint.
- **`Logaffe.Extensions.Logging`** — the same CLEF built from `ILogger`'s
  template and state.

They keep logaffe's own package prefix rather than reaching into Serilog's, which
is reserved by the project that owns it.

## Tests are split by what they need

**`Logaffe.UnitTests`** runs in seconds and needs nothing installed: the rules of
Domain and the use cases of Application against substituted ports. **`Logaffe.IntegrationTests`**
brings up Postgres with Testcontainers, because ADR 0003's hand-written SQL and
binary `COPY` are exactly the parts no substitute can vouch for — the migrations,
the indexes doing what [Storage](./storage.md) claims, the claim flow, and the
retention sweep. The split is by what a test needs rather than by what it covers,
because that is the distinction CI has to act on.

It also **starts the installation itself**, through `WebApplicationFactory`, for
the one class of fact that cannot be read off a registration: what an endpoint
admits. That every operator surface sits behind the session is a one-line
mistake away from not being true, so it is asked of a running composition root
rather than of the line that was supposed to say so. That is why this project
references `Logaffe.Api` and the unit tests do not.

The frontend carries its own tests inside `src/web/`, run by the same CI job that
builds it.

## What is deliberately not here

- **No shared types across the two languages.** Settled in ADR 0001; the contract
  is `docs/api/openapi.json` and it is checked.
- **No second read path, and no second write path.** The MCP adapter and the
  HTTP endpoints call the same use cases, which is what
  [Querying](./querying.md) promises and what ADR 0030 makes structural. The
  administering tools add no act of their own either: what they decide is a
  shape and a refusal.
- **No context split.** This is a single-context repository — one `CONTEXT.md`
  and one `docs/adr/` at the root — and `src/` is laid out by layer rather than
  by bounded context.
- **No prototypes on `main`.** Code that measures something not yet decided lives
  on its own branch and stays unmerged, so that `main` carries the validated
  decision and not the experiment behind it.
- **No test project per production project.** Covered above: the split that
  matters is what a test needs to run.
- **No generated code checked in besides the contract.** The web API client is
  generated at build time from `openapi.json`; the document is the artifact, its
  output is not.

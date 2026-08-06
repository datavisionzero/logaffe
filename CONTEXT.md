# logaffe Domain Language

This glossary defines the canonical product language of logaffe. It describes
domain concepts and distinguishes them from competing ideas without prescribing a
technical implementation.

It grows one area at a time. A term appears here once the decision behind it has
been made; the behaviour itself lives in the area document under `docs/`, and the
reasoning behind a contested one lives in `docs/adr/`.

**The entry heading is the canonical name; running prose writes it in ordinary
case.** `docs/` says "a project", "a log entry", "the operator", and nothing is
lost by that. Capitals are kept only where they resolve an ambiguity the sentence
cannot — **Installation** and **Instance** against each other, **Operator** and
**Agent** as the two consumers against the ordinary words, and a term standing
beside its own definition.

## Language

**Installation**:
One deployed logaffe: a container, its database, and the host volume holding what
is not in the database. It is claimed by exactly one operator, and everything the
product knows lives inside one of them.
_Avoid_: Instance, deployment, tenant, server, node

**Operator**:
The single human account of an installation, which can see and do everything.
There is exactly one, it is established by the claim, and the product has no
concept of a second person.
_Avoid_: User, admin, account, owner, tenant

**Agent**:
An AI acting on the operator's behalf, reaching the installation over MCP as the
second first-class consumer beside the operator. It reads on request and never
watches on its own.
_Avoid_: Bot, assistant, integration, client, sender

**Claim**:
The act by which an operator takes an unclaimed installation and establishes
their account, credentials and second factor. An installation is unclaimed until
it happens, and claimable by anyone who can reach it while it is.
_Avoid_: Signup, registration, onboarding, first login, setup

**Claim Window**:
The limited period after an installation first runs during which a claim may be
made over the network. Once it lapses, claiming is only re-enabled from the host
the installation runs on.
_Avoid_: Grace period, trial, timeout, expiry

**Project**:
The unit of separation: every log entry belongs to exactly one, the operator
creates them explicitly, and separation holds in storage, in the UI and in agent
access alike. It owns its retention window and its ingest tokens.
_Avoid_: Application, service, tenant, stream, bucket, source, workspace

**Ingest Token**:
The write-only secret that admits a delivery to one project. It permits writing
and grants no read access of any kind, it is what identifies the project, and the
operator can hold two at once while rotating.
_Avoid_: API key, password, credential, project id, access token

**Sender**:
An application delivering log entries to a project. It is trusted because the
operator runs it themselves, which says nothing about the content it delivers.
_Avoid_: Client, source, producer, agent, publisher

**Instance**:
One running copy of a sender — a container, a replica, a machine — named by the
sender itself so that several copies serving one project stay separable. Not to
be confused with an **Installation**, which is the deployed logaffe.
_Avoid_: Installation, node, host, source, machine, replica

**Batch**:
The set of log entries a sender hands over in one delivery. Delivery is
fire-and-forget: a sender does not wait for it, does not retry it, and learns
nothing later about whether it landed.
_Avoid_: Payload, chunk, page, upload, request

**Log Entry**:
The atomic record logaffe stores: one thing that happened in one sender, carrying
a level, two timestamps, a message template, its properties and an optional
exception. It is never edited after it arrives, and it leaves only by ageing out.
_Avoid_: Log line, log event, log message, record, row, item

**Level**:
The severity a sender assigned to an entry, being one of `Verbose`, `Debug`,
`Information`, `Warning`, `Error` and `Fatal`. An entry that names none is
`Information`.
_Avoid_: Severity, priority, importance, category

**Message Template**:
The message as the sender wrote it, which is always a template — a plain sentence
is one with no placeholders in it. It is what a sender delivers, and it is never
what the operator is shown.
_Avoid_: Format string, raw message, pattern, the message

**Rendered Message**:
The text produced by substituting an entry's properties into its message
template. It is what the operator reads, what a search matches, and it is
computed once when the entry arrives rather than each time it is read.
_Avoid_: Formatted message, display text, output, the message

**Property**:
A named value a sender delivers beside the message template, which may or may not
have a placeholder waiting for it. Properties are how an entry carries anything
the sentence itself does not.
_Avoid_: Field, tag, label, attribute, dimension, metadata

**Promoted Property**:
A property logaffe recognizes by name and lifts into a first-class, searchable
field of the entry — the instance, the logger name, and the trace and span that
an entry belongs to. Promotion asks nothing of a sender: a delivery carrying none
of them is complete.
_Avoid_: Reserved key, special property, indexed field, system field

**Event Time**:
The moment a sender says an entry happened. It is what the product orders entries
by, because it is the order in which things occurred.
_Avoid_: Timestamp, occurred at, log time, created at

**Receipt Time**:
The moment an installation received the batch an entry arrived in. It is what
retention counts from, because it is the only one of the two clocks a sender
cannot get wrong.
_Avoid_: Ingested at, server time, stored at, created at

**Retention Window**:
The period a project keeps its entries, counted from receipt time, after which
they are removed. Time is the only limit a project has — there is no size cap, no
row quota, and no interaction between limits.
_Avoid_: Retention policy, TTL, expiry, archive, quota

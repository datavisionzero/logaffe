# A Notification Carries Numbers and Names, Never Log Content

An alert leaves the installation carrying a project's name, the condition that
fired, the numbers behind it, and a link into the log view with the filters
already set. It carries **no text that came out of an entry** — no rendered
message, no exception, no property value, no logger name and no instance. "Three
errors in `checkout`, and here is the first of them" is not on offer, and cannot
be turned on.

Three things make this the line rather than a setting.

**It is the first thing in the product that travels on its own.** Every other
surface is reached by someone presenting a credential the operator issued: the
UI behind a session, MCP behind an agent token, ingestion behind a write-only
token. A notification goes outward, to a service the operator does not run, and
in the notifier this product supports the default topic is unauthenticated and
guessable by anyone who guesses the word. `VISION.md` puts the whole
installation on the public internet on purpose and hardens every surface for it;
a channel that carries log text off the box would be the one surface that was
never hardened, because it is not this product's to harden.

**Log content is untrusted, and this is where it would be read as prose.**
[ADR 0012](./0012-log-content-reaches-an-agent-as-data-never-as-prose.md) settles
that an entry reaches an agent as data and never as instructions, on the grounds
that an outsider needs no access to anything to put their text in a log — a
username from a failed login is the standing example. A notification is the
exact opposite shape: text rendered as a sentence, on a phone, by a client
nobody here wrote, arriving with the installation's authority behind it. It is
the one place the product would render untrusted content as prose, and it is
avoidable.

**The link is the better answer anyway.** What the operator wants at that moment
is not one message, it is the view — the surrounding entries, the filters, the
band of what the machine was doing. The notification is a reason to open a
screen, and a screen is what it opens. Behind the session, where the content
already is.

## Consequences

**It bounds what this feature can ever become.** A digest of the day's errors, a
message in the notification title, grouping alerts by message template, a
"top errors" summary — all of them are shipping content, so none of them is a
later phase of this. Wanting one is a reason to reopen this document, and the
answer will usually be the link.

**It is checkable in one place.** Like ADR 0012, the rule survives by living in
an adapter: what composes a notification takes identities and numbers and has no
access to an entry at all. There is no path from the condition evaluation of
[ADR 0050](./0050-the-alert-conditions-are-a-closed-set.md) to the entry table —
the conditions run on the tally of
[ADR 0047](./0047-the-volume-history-is-tallied-as-it-arrives.md), which holds counts
and nothing else — so the rule is a property of what the code can reach rather
than a discipline about what it chooses to send.

**One notifier is enough, and that is not a coincidence.** A notification that
is a name, three numbers and a URL formats identically everywhere, so the
argument for a second integration is not that the first one renders poorly. ntfy
is the one this product supports: it pushes, it needs no inbound port, it is
self-hostable, and it reaches a phone. Email in particular stays absent for the
reason [ADR 0015](./0015-the-operator-has-no-username-and-no-email.md) already
gave — there is no address in this product and adding one to send alerts to
would put a whole delivery apparatus behind a feature this small.

**The link is a URL the installation has to know it is reachable at**, which is
the one new thing an operator configures that they did not have to before. An
installation that has not been told its own public address sends the
notification without a link rather than with a wrong one.

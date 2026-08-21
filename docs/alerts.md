# Alerts

Everything else in this product waits to be asked. Reading a log begins with the
operator or with the agent they sent, nothing analyses entries on their behalf,
and `VISION.md` keeps that as a principle rather than a default — one that
survives this document intact, because a condition here counts rows it was handed
as they arrived and reads no entry at all.

Three things do not fit under it, and they have one property in common: **the
whole point of them is that the operator does not know.** The store is filling
up, an application has stopped delivering, or a project is suddenly writing far
more than it does. The first ends in a database that stops accepting writes; the
second is usually how a self-hoster finds out that a service died; the third is
what fills the disk while nobody is watching. None of them is discovered by
looking, because looking is what is not happening.

So the installation says something, unasked, on **three conditions and no
others**. This document is what those are, exactly what makes each of them fire,
what the notification carries, and what happens when it cannot be sent.

It is deliberately not an alerting system. There is no rule to write, no
threshold to type, nothing to attach to a filter, and no second place for a
notification to go
([ADR 0050](./adr/0050-the-alert-conditions-are-a-closed-set.md)).

## The condition

A **condition** is one of three things the installation checks about itself,
named in the product rather than written by the operator. Each is **off until it
is switched on**, and each derives whatever it compares against from the
installation's own recent history rather than from a number somebody guessed.

That last part is the whole case for a closed set. A threshold is a number the
operator has to guess about a quantity they have never looked at — how many
entries an hour is normal for `shop / api` at three in the morning — and every
wrong guess is a false alarm. Nothing is typed in here, so nothing is typed in
wrong.

**What every condition reads is the tally**, not the entries: how many entries a
project received in each hour, counted as the deliveries arrived
([ADR 0047](./adr/0047-the-volume-history-is-tallied-as-it-arrives.md)). No
condition touches `log_entry`, no condition reads a message, and there is no path
from here to one — which is what makes the rule about what a notification carries
a property of the code's reach rather than a discipline it has to remember.

**Conditions are evaluated on the hour, on the hour that has just closed**, on
the pass the retention sweep already runs. Never the hour in progress: a burst at
five past would otherwise look like twelve times the hour it is a twelfth of.

## The three

### The store is filling up

The **filesystem the installation's database sits on** crosses **85 per cent**,
and again at **95**.

It is read off the samples that already exist — the installation names the host
it runs on and the mount on that host, and the figure is `used / total` from that
host's newest filesystem reading ([Metrics](./metrics.md)). Nothing new is
collected and nothing is asked of Postgres: a machine that reports its
filesystems every minute is already saying this, and a disk size the operator
typed in once would be a number that goes stale without anyone being told.

**The mount is chosen from the ones the host reports**, not written out by hand.
The collector's configuration is where mounts are named
([Deployment](./deployment.md#the-collector-on-a-machine)), so the host's newest
sample already knows which strings are real ones — the same shape a filter's
values have, which come from the entries rather than from a list the operator
maintains
([ADR 0029](./adr/0029-filter-values-come-from-the-entries-not-from-a-list.md)).

**Each threshold notifies once and arms again when the figure falls back below
it.** Crossing 85 sends one notification; the disk continuing to fill sends
nothing more until it reaches 95, which sends the second. A disk that goes back
under 85 and fills again notifies again, because that is a second event and not
the same one still happening.

**It is not evaluated at all, and says so, when it cannot be.** No host named on
the installation, no mount named, the named mount absent from the host's newest
sample, or no sample in the last hour: in each case the condition is switched on
and blind, and an operator who thinks a disk is being watched when it is not is
worse off than one who was never offered the switch. The state is legible where
the switch is ([The web UI](./ui.md)).

### A project has gone quiet

Nothing received for **more than three times the project's longest quiet stretch
of the last fourteen days**, and never sooner than **an hour**.

Precisely, on each closed hour:

- **`quiet`** is how many whole closed hours have passed since the most recent
  hour this project has a tally row for.
- **`tolerated`** is the longest run of consecutive hours with no row, anywhere
  in the last fourteen days, multiplied by three — and at least one hour whatever
  that comes to.
- It fires when `quiet` is greater than `tolerated`.

**What this means at the two ends.** A project that delivers something every hour
has no run of empty hours at all, so it tolerates the floor of one and is noticed
on its second silent hour — in practice between two and three hours after it
stopped, because the evaluation is hourly and only on closed hours. A project
that is idle every night from one until six has a run of five, so it tolerates
fifteen: an outage at breakfast on Sunday is noticed some time after midnight.

That second number is a real cost and it is the trade this whole document is
built on: **a late true alarm beats a false one**. A project woken for every
ordinary night would be a project whose alerts the operator learns to ignore, and
an ignored alarm is worth less than none.

**A project that has never received anything never fires this.** It has no
longest quiet stretch because it has no history at all, and a project created and
not yet deployed is not an incident.

### A project is delivering far more than it does

A closed hour above **ten times the median of that hour of the day across the
last fourteen days**, and above a floor of **a thousand entries**.

Precisely: for the closed hour, take the same hour of the day on each of the
fourteen days before it — fourteen figures, counting an hour with no row as
**nought**, because a project that is normally silent at three in the morning is
normally silent rather than absent from the arithmetic. The median of those is
the baseline. It fires when the closed hour is both above ten times it and at
least a thousand entries.

**A median by hour of the day, not an average over the day.** The batch job that
writes fifty thousand entries at three in the morning is normal at three in the
morning; averaged across the day it would fire every single night, and it would
drag the daytime baseline up until a real daytime flood fitted underneath it.

**A baseline of nought is an ordinary answer**, and it is the interesting one: a
project that has never written anything at four in the morning, writing five
thousand entries at four in the morning, is exactly what this condition is for.
The floor is what makes that safe — ten times nothing is nothing, so without it
every first entry of a quiet hour would fire.

**The floor is absolute and it is not a ratio.** Two entries becoming twenty is a
tenfold rise and is not an incident, in any project, ever.

## What makes a closed set defensible is the guarding

The arithmetic above is the smaller half. These are the rules that stand between
it and an operator who stops reading their notifications:

- **A fortnight of tally before any rate condition fires.** A project whose
  oldest tally row is less than fourteen days old has no normal, so it has no
  alarm — however it behaves. This covers the first two weeks of every project
  and the first two weeks after an installation is restored.
- **An absolute floor under every rate condition**, whatever the ratio says.
- **Closed hours only.** Nothing is ever judged on the hour in progress.
- **One notification per project per condition, then six hours of silence.** The
  condition continuing to hold sends nothing more; it is one event, still
  happening.
- **Nothing is sent when a condition clears.** There is no "resolved", no second
  message, and no record of one. A notification exists to make a person look, and
  something that has stopped needing a person is not worth a message that will be
  read at three in the morning.
- **A project can be muted**, in its own settings, and a muted project is not
  evaluated at all.

## The alert

### It carries numbers and names, and never log content

An alert carries the **project or host name**, the **condition**, the **numbers
behind it**, and a **link**. It carries no rendered message, no exception, no
property value, no logger name and no instance — nothing that came out of an
entry, on any setting, with no flag that turns it on
([ADR 0049](./adr/0049-a-notification-carries-numbers-and-names-never-log-content.md)).

"Three errors in `checkout`, and here is the first of them" is therefore not on
offer. What arrives is that there were three, and where to look.

Three things make that the line rather than a preference. A notification is the
one thing in this product that **travels outward on its own**, to a service the
operator does not run and this project cannot harden. Log content is **untrusted
text** — a username from a failed login is the standing example — and a
notification is precisely where it would be rendered as prose, by a client
nobody here wrote, with the installation's authority behind it. And the **link is
the better answer anyway**: what the operator wants at that moment is the view
with its filters and the band above it, not one line of it.

### The link, and the address it is built from

The link lands in the log view of that project with the filters that make the
alert legible already set — for a flood, the hour it fired on.

**The installation has to be told the address it is reachable at**, and this is
the one thing about alerting that is deployment configuration rather than a
setting ([Deployment](./deployment.md#the-variables)). Everywhere else in the
product the address comes free: the delivery snippet and an agent's MCP
configuration are composed inside a request, so `X-Forwarded-Host` and
`X-Forwarded-Proto` say what the operator reached the installation by. **An alert
has no request behind it.** It is composed by a background pass on the hour, with
nobody on the other end of anything.

Remembering the address off whatever last called was the alternative and it is
refused: that value is chosen by a header, so a link built from it is a link an
outsider could choose — and it would arrive in a notification the operator trusts
and clicks. An address that came from the deployment cannot be moved by traffic.

**An installation that has not been told sends the alert without a link**, rather
than with a wrong one. The notification is still worth having; a link to a
container port is not.

## The notifier

There is **one, and it is ntfy**: a server, a topic, and an optional access
token, set once for the installation. It pushes, it needs no inbound port, it is
self-hostable, and it reaches a phone.

A notification that is a name, three numbers and a URL formats identically
everywhere, so the case for a second integration is not that the first renders
poorly. Email in particular stays absent for the reason it was always absent —
this product has no address to send anything to
([ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md)).

The access token is stored the way every other secret in this product is: sealed
under the key on the host volume
([ADR 0022](./adr/0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)),
and readable back by the operator.

**A public ntfy topic is readable by anyone who guesses the word**, and the
product does not pretend otherwise — it is the plainest reason the rule about
what an alert carries is a rule. What an outsider on the operator's topic learns
is that a project called `shop / api` went quiet, which is a great deal less than
a line of what it logged.

### When it cannot be reached

A failure to deliver costs **one line in the installation's own file log**
([ADR 0002](./adr/0002-logaffe-logs-to-files-not-into-itself.md)) and nothing
else. There is no retry, no queue, and no second attempt on the next pass.

A queue of undelivered alerts arriving together an hour later is the failure mode
this product would least like to have: it is a burst of notifications about
things that are no longer true, which is the fastest way to teach an operator to
swipe them away. The alert that was missed is a cost, and it is the smaller one.

### Proving it works

The operator can **send a test notification** from the settings, which is the
same shape a real alert has and is theirs rather than any condition's.

A notifier nobody has ever proved is a notifier that gets discovered broken on
the night it was needed — and unlike everything else in this product, a wrong
value here fails silently by design, because a failed send is one line in a log
file.

## What the operator sets, and where

- **In the installation's settings**: the notifier, the three switches, and — for
  the first condition — which host the installation sits on and which of that
  host's mounts holds the database.
- **In the project's settings**: whether it is muted, beside the group and the
  host.
- **In the deployment**: the public address, because it is a property of how the
  installation was put on the network rather than something the operator changes
  on a Tuesday.

**When each condition last fired is kept and shown**, per project, on the alerts
screen. It is the only history there is, and it is what makes "is this thing
working?" answerable without waiting for an incident.

## What is deliberately not here

- **No rule an operator writes, and no fourth condition.** No threshold to type,
  no expression, no alert attached to a filter or a saved search — that last one
  is refused in [Querying](./querying.md) and is exactly the alternative ADR 0050
  rejected. A fourth condition is a change to that document.
- **No error-burst condition, yet.** Entries at `Error` or above are in the tally
  and the arithmetic is a few lines away. It is left out because it is the least
  stable baseline in the set — a deploy produces errors, a retry storm produces
  thousands and resolves itself, and a project that logs a handled exception per
  request has a normal nothing like one that does not. It comes back once the
  three above have been quiet in a real installation for a season.
- **No notification carrying log content**, in any form: no message, no
  exception, no property value, no digest of the day's errors, and no grouping of
  alerts by what entries say.
- **No second notifier, no email, no webhook, and no per-condition destination.**
- **No severity, no priority, no tags.** Every alert is the same weight on the
  way out, because a per-condition priority is a routing model and a routing
  model is the thing after it.
- **No quiet hours, no schedule, and no on-call anything.** Quiet hours are the
  obvious thing to want here and they are refused because the conditions already
  learn a project's normal by hour of the day, which is the same idea done better
  and with nothing to enter.
- **No incident, no acknowledging, no escalation, and nothing to resolve.** An
  alert is a message, not an object with a lifecycle.
- **No notification bell and no alert list in the interface.** An alert leaves the
  installation; it does not accumulate on a screen, and there is nothing to mark
  as read or dismiss ([The web UI](./ui.md)).
- **No agent that watches.** Nothing analyses entries in the background, no agent
  is handed a log stream to keep an eye on, and no notification is written by a
  model. A condition counts rows it was handed as they arrived.
- **No alerting surface over MCP.** Neither kind of agent token reaches the
  notifier, the switches or the mute — a reading token reaches no setting at all,
  and the administering surface is the twenty-one tools [MCP](./mcp.md) names.
  Whether that changes is a change to that document rather than a tool added
  here.
- **No quiet host.** A machine that stops reporting is left exactly as it is
  ([Operations](./operations.md)); it is the project going quiet that fires, and
  the difference is that a silent project means an application stopped while a
  silent collector usually means a collector.

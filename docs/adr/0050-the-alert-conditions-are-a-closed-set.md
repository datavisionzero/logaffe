# The Alert Conditions Are a Closed Set

There are **three conditions**, named in the product, and there is no way for an
operator to write a fourth. The obvious alternative is the one every logging
tool offers — an alert defined on the query surface, "notify me when this filter
matches more than N in an hour" — and it is rejected for the reason
[ADR 0044](./0044-a-sample-has-a-closed-schema.md) rejected the label: it moves the
limit out of the installation and into the discipline of whoever writes the
rules. It is also refused already, in as many words, by
[Querying](../querying.md).

The deeper objection is that a threshold is a number the operator has to guess
about a quantity they have never looked at, and every wrong guess is a false
alarm. A closed set can do what a rule language cannot: **derive its own
thresholds from the project's own history**, using the tally of
[ADR 0047](./0047-the-volume-history-is-tallied-as-it-arrives.md). Nothing is
typed in, so nothing is typed in wrong.

## The three

- **The store is filling up.** The filesystem the database sits on crosses 85
  per cent, and again at 95. Read from the samples of the host the installation
  is named onto ([Metrics](../metrics.md)); a condition that cannot be evaluated,
  because no host was named or none is reporting, is off and says so rather than
  staying quiet.
- **A project has gone quiet.** Nothing received for more than three times the
  project's longest quiet stretch of the last fourteen days, and never sooner
  than an hour. A project that delivers continuously is therefore noticed on its
  second silent hour, which the hourly evaluation makes two to three hours after
  it stopped; a project that is silent every night is not woken for it.
  [Alerts](../alerts.md#a-project-has-gone-quiet) has the arithmetic.
- **A project is delivering far more than it does.** A closed hour above ten
  times the median of that hour of the day across the last fourteen days, and
  above a floor of a thousand entries.

Every one of them is off until the operator turns it on, one notifier serves the
installation, and a project can be muted.

## What makes a closed set defensible is the guarding, not the counting

A late true alarm beats a false one, and the whole of the following exists to
buy that trade:

- **Fourteen days of tally before a rate condition can fire at all.** A project
  created this morning has no normal, so it has no alarm.
- **An absolute floor under every rate condition**, whatever the ratio says. Two
  entries becoming twenty is a tenfold rise and is not an incident.
- **Closed hours only.** A rate is never evaluated on the hour in progress, so a
  burst at five past never looks like twelve times the hour.
- **A median by hour of the day, not a mean over the day.** The batch job at
  three in the morning is normal at three in the morning, and averaging it across
  the day would make it fire every night and make the daytime baseline wrong too.
- **One notification per project per condition, then six hours of silence**, and
  no second one while the condition still holds.
- **Nothing is sent when a condition clears.** Silence is not information here,
  a "resolved" is a notification for something that no longer needs a person, and
  the operator looks at the screen either way.
- **A notifier that cannot be reached costs one line in the installation's own
  file log** ([ADR 0002](./0002-logaffe-logs-to-files-not-into-itself.md)) and no
  retry. A queue of undelivered alerts arriving at once, an hour later, is the
  failure mode this product would least like to have.

## Consequences

**The error burst is deliberately absent, and it is the one everybody wants.**
Entries at `Error` or above are in the tally, and the condition is a few lines
away. It is left out because it is the least stable baseline in the set: a
deploy produces errors, a retry storm produces thousands and resolves itself,
and a project that logs a handled exception per request has a normal that is
nothing like a project that does not. Adding it before the three above have been
quiet in a real installation for a season would spend the credibility the whole
feature runs on. It comes back by changing this document.

**`docs/operations.md` said that deciding a quiet machine is a problem is
alerting, and refused it.** That sentence now stands for hosts and no longer for
projects. A host that stops reporting is still left exactly as it is — nothing
marks it stale and nothing says anything about it — and the reason the project
got the condition and the machine did not is that a silent project means an
application stopped, which is the thing a self-hosting operator most wants to be
told, while a silent collector usually means a collector.

**Adding a condition is a change to this document, deliberately.** The friction
is the same one ADR 0044 built for the sample schema, and it is what stands
between three conditions and the first expression box.

**Every condition runs on the tally and none of them reads an entry.** That
keeps evaluation off the largest table in the database — one pass over a few
hundred small rows on the hour, on the timer the retention sweep already uses —
and it is what makes
[ADR 0049](./0049-a-notification-carries-numbers-and-names-never-log-content.md) a
property of the code's reach rather than a rule it has to remember.

**`VISION.md`'s eighth principle is narrowed rather than dropped.** "Nothing
happens unasked" was about reading: no agent watches the log stream, nothing
analyses entries, and every look into log content still begins with the operator
or their agent. A condition counts rows it was handed as they arrived, and the
operator asked for it once, when they switched it on.

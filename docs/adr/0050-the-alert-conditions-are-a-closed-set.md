# The Alert Conditions Are a Closed Set

There are **four conditions**, named in the product, and there is no way for an
operator to write a fifth. The obvious alternative is the one every logging
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

## The four

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
- **A project is failing far more than it does.** The same ratio and the same
  median, over entries at `Error` or above, above a floor of ten, and true of two
  closed hours in a row. Each hour is judged against its own hour of the day.
  [Alerts](../alerts.md#a-project-is-failing-far-more-than-it-does) has the
  arithmetic and what the second hour costs in latency.

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
- **At most one thing said about a project on a pass**, in a fixed order: gone
  quiet, then failing, then flooding. The more specific sentence wins, and the
  condition that loses is not evaluated rather than evaluated and dropped.
- **Nothing is sent when a condition clears.** Silence is not information here,
  a "resolved" is a notification for something that no longer needs a person, and
  the operator looks at the screen either way.
- **A notifier that cannot be reached costs one line in the installation's own
  file log** ([ADR 0002](./0002-logaffe-logs-to-files-not-into-itself.md)) and no
  retry. A queue of undelivered alerts arriving at once, an hour later, is the
  failure mode this product would least like to have.

## Consequences

**The fourth condition was deferred by this document, and it came back by
changing it. What follows is why, stated straight.**

The original deferral said the error burst would come back "once the three above
have been quiet in a real installation for a season". **That did not happen, and
this decision does not pretend it did.** The three conditions landed on
2026-08-21 and v0.5.0 was tagged on 2026-08-23, at the commit that put their
switches on a screen. They had not run anywhere for a day.

What changed instead is that the gap acquired a price. A second consumer,
`payaffe`, delivers its logs here — a non-custodial payment service for one shop
— and keeps a separate error tracker running for exactly one capability this
product did not offer: **being told that something started failing without going
to look.** Everything else that tracker was carrying turned out to be either
already available or already lost. The trail leading to an error is served
better here than by breadcrumbs, because `TraceId` and `SpanId` are promoted to
indexed fields and the real entries of the request are one filter away. Browser
stack traces were never resolved, because that project uploads no source maps.
What remained was grouping and an issue lifecycle — at fifteen payments a day, a
luxury — and the notification, which is not. So the gap was costing a whole
second service, a second secret to rotate, and a publicly-exposed DSN in a
browser bundle, to deliver one message.

**The three original objections were answered rather than argued past**, and two
of them by the same clause. "A deploy produces errors" and "a retry storm
produces thousands and resolves itself" are both answered by requiring two
consecutive closed hours: both shapes are over inside one hour, so neither fires
— not because the burst was filtered, but because it stopped. The third, that a
project logging a handled exception per request has a normal nothing like one
that does not, was already answered by the design the other rate condition uses:
a median per project, per hour of the day, with an absolute floor underneath it.

**What is not answered is the floor of ten**, and this decision would rather say
so than dress it up. Nothing derives it. `Flood`'s thousand is equally a
judgement, so it is not a new kind of number in this design, but it is the
weakest part of the fourth condition and it is mitigated rather than solved: it
only decides anything where the ratio has already passed, and only where the hour
before it passed too.

**The waiting would have been nearly free, and that is the honest counterweight.**
The fourteen-day guard means no rate condition can fire in any installation until
a fortnight of tally exists behind it, so the first two weeks of this cost
nothing either way. What tipped it was that the second service would have been
paid for over those two weeks too.

**`docs/operations.md` said that deciding a quiet machine is a problem is
alerting, and refused it.** That sentence now stands for hosts and no longer for
projects. A host that stops reporting is still left exactly as it is — nothing
marks it stale and nothing says anything about it — and the reason the project
got the condition and the machine did not is that a silent project means an
application stopped, which is the thing a self-hosting operator most wants to be
told, while a silent collector usually means a collector.

**Adding a condition is a change to this document, deliberately, and the fourth
one paid the toll.** It cost this decision rewritten, `docs/alerts.md` rewritten,
a column and a migration, a switch on a screen and a sentence saying what it will
do. The friction is the same one ADR 0044 built for the sample schema, and it is
what stands between a named set and the first expression box — the point of it is
not that a condition can never be added but that adding one is visible, argued
and paid for.

**Every refusal this decision made survives the fourth condition**, and that is
the test it had to pass. The set is still closed and still named in the product;
there is still no threshold to type, no expression, and no alert attached to a
filter or a saved search. No entry is read — the level is in the envelope at
ingestion and the tally already holds it, so there is still no path from a
condition to `log_entry`. The notification still carries numbers and names.
Nothing is grouped, nothing is fingerprinted, and nothing has a lifecycle. There
is still no severity and no second notifier: the fourth alert weighs exactly what
the other three weigh on the way out.

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

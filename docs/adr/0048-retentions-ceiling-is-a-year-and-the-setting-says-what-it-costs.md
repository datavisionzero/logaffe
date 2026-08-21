# Retention's Ceiling Is a Year, and the Setting Says What It Costs

The ceiling on a project's retention window moves from 90 days to **365**, and
the work of keeping this product bounded moves off the number and onto
arithmetic the operator is shown. [ADR 0020](./0020-retention-has-a-maximum.md)
stands in everything it decided except the figure: there is still a ceiling no
installation can raise, time is still the only limit a project has, lowering a
window still removes entries and still says how many first, and raising one
still brings nothing back.

**The number moved because days were the wrong axis.** What the ceiling protects
is disk and index churn, and what it measures is the calendar. At the 1.2 KiB
per entry [Storage](../storage.md) measured:

| A project's rate | 90 days | 365 days |
| --- | --- | --- |
| 5 000 entries a day | 0.5 GiB | 2.1 GiB |
| 1 entry a second | 9.1 GiB | 37 GiB |
| 10 entries a second | 91 GiB | 370 GiB |

The 90-day ceiling permits one noisy project 91 GiB and refuses a quiet one a
year that costs 2.1 GiB. It is not a bound on anything the product actually pays
for, which is why ADR 0020 had to write that the operator "will not be given a
reason beyond this decision" — there was no reason available in the units the
setting is in.

**What does the work instead is that the field states the cost before it is
applied.** The tally of
[ADR 0047](./0047-the-volume-history-is-tallied-as-it-arrives.md) gives the project's
own rate over the last fourteen days; multiplied by 1.2 KiB and by the days
asked for, it is what that window will hold in steady state. It is shown beside
what the installation holds today — `pg_database_size`, which is exact and costs
one call — and beside what the filesystem it sits on has left, which the host's
own samples already report ([Metrics](../metrics.md)). An operator raising a
window sees three numbers and their own decision. That is a better bound than a
ceiling, because it is in the units the limit is really in.

**Two of the three things ADR 0020 said would have to be revisited do not hold
up.** Query performance does not degrade with a longer window: the cursor of
[Querying](../querying.md) is depth-independent by construction and was measured
at 1.3 ms five thousand entries deep, and every read is cut off at five seconds
either way ([ADR 0026](./0026-a-read-has-five-seconds.md)). Index size grows,
but it grows with entries and not with days, and it was never the ceiling that
bounded it — a week of the third row above is more index than a year of the
first.

## Consequences

**The third one does hold up, and it is the price.**
[ADR 0005](./0005-the-rendered-message-is-stored-not-recomputed.md) makes a change to
the rendering rules repair itself by letting old rows age out, and it says
outright that it leans on the window being short. At the new ceiling that repair
takes a year rather than a quarter, and an installation that has raised a
project to it will hold two generations of rendered text for that long, which a
search will mix. This is accepted rather than solved: the alternative is a
rewrite of the largest table in the database, and the failure it guards against
is a cosmetic inconsistency in text nobody has looked at since the day it
arrived.

**An installation can now be built four times the size of the one that was
measured.** Every figure in [Storage](../storage.md) comes from ten million
entries on two cores and 4 GB, and forty million is not a place this product has
been. The measured numbers stay true of the shape they were taken on; what is
unknown is the GIN index of
[ADR 0010](./0010-search-is-a-substring-match-not-a-full-text-query.md) under
four times the churn, which
[ADR 0023](./0023-retention-deletes-rows-rather-than-dropping-partitions.md)
already names as the part of the design most likely to need attention. That is
the open benchmarking question, not a new one.

**The default is unchanged and stays low.** A new project keeps entries for 30
days, because the ceiling is what an operator may choose and the default is what
the product recommends, and those were never the same number. Raising the
ceiling is not advice to use it.

**Time stays the only limit, and the arithmetic is advisory.** The field says
what a window will cost; it does not refuse a window on the grounds that it
costs too much, and no row quota, size cap or drop-oldest appears anywhere. The
store filling up is answered by the operator being told about it
([ADR 0050](./0050-the-alert-conditions-are-a-closed-set.md)), which is a notification
and not a limit. Keeping retention explicable in one sentence was the point of
the rule and the rule is intact.

**"Not a multi-year archive" is now carried by a sentence rather than by a
quarter.** A year is long enough that the phrase in `VISION.md` needs the
ceiling to mean it, so the ceiling stays absolute and stays in the domain type
rather than in configuration: no environment variable raises it, no installation
sets it, and moving it again is a change to this document.

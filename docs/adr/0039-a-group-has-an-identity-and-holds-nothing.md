# A Group Has an Identity and Holds Nothing

A group is a row of its own, with an identity that survives its rename, and it
carries a name and nothing else — no retention window, no token, no colour, and
no query. A word written on the project would have done every job the product
asks of a group today and would have cost a single nullable column; it was
rejected because the day a group has to carry anything is the day every
installation in the field needs a migration that invents identities for strings
and reconciles the ones that differ by a capital letter. logaffe is early enough
that the expensive half can be bought while nobody owns data yet, and that is the
whole of the reason.

## Consequences

**The entity is deliberately empty, and that is not an oversight.** Whoever finds
a class holding one string and reaches for the simplification is undoing the
decision rather than tidying up after it. What may be added later is a property
of the group — a retention default its projects inherit, a colour, a description.
What may not be added is anything that makes a group a thing to *read* from: a
query still names one project ([Querying](../querying.md)), and a group is for
finding a project, never for asking across several.

**A group is nothing to delete carefully.** It holds no entries, no tokens and no
settings, so removing one leaves its projects in no group and destroys nothing.
It is a plain act stating how many projects it affects, and pointedly not the
typed-name confirmation a project's deletion carries
([ADR 0019](./0019-a-project-is-deleted-at-once-and-its-entries-follow.md)) —
that guard is proportionate to data that cannot come back, and copying it here
would teach the operator that both acts are the same weight when one of them is
free.

**Empty groups exist.** A word on a project vanishes when the last project stops
saying it; a row does not, and so an operator can make a group before there is
anything to put in it, and is left with one after taking the last project out.
Both are consequences of the identity rather than accidents around it, and the
interface therefore has to show a group with nothing in it rather than quietly
omitting one the operator just created.

**A project's name is unique within its group** rather than within the
installation. The uniqueness exists for the operator reaching for one of two
projects called `api` at three in the morning
([Projects and tokens](../projects.md)), and inside a group that is named beside
it the ambiguity the rule guards against is already resolved. Two ungrouped
projects called `api` still collide, which is why the index treats the absent
group as one value rather than as many.

**A group does not say how many projects it holds, and neither does the answer
that lists them.** That number is a fact about the projects, and both consumers
of it already read the project list — so answering it twice is two things to keep
current, which is exactly what the first attempt failed to do: the groups were
read once when a session began, the projects were re-read after every act, and a
group filled a minute ago still said nought. What is counted off the list that is
already correct cannot disagree with it.

**A group holds projects and never another group.** Nesting is what the identity
makes cheap and what an installation holding ten to thirty projects has no use
for, so the absence of a parent is a decision and is written down as one.

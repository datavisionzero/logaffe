# A Sample Has a Closed Schema

A sample carries a fixed set of numbers in fixed columns — processor, memory,
load, and a filesystem's used and total — and there is no way for an operator or
a sender to introduce another. The obvious alternative is the shape every metrics
system in the world uses, a name with labels and a value, and it was rejected
because that shape moves the limit on how much data exists out of the store and
into the discipline of whoever writes the labels. Everywhere else in this product
boundedness is a property the installation enforces: retention has a ceiling no
installation can raise, a query names one project, a page has one size. A label
carrying a request id would put a series per request into a product whose case
for being simple is that it cannot grow in ways nobody chose.

## Consequences

**Most of what a metrics stack is made of stops being needed.** With no
dimensions there is nothing to aggregate across, so there is no query language to
design, no recording rules, no cardinality limits, no per-series retention and no
dashboard builder to arrange the results in. The storage is a narrow table with a
timestamp and a host, and the query is a range on it. This is not a saving on the
side; it is most of the reason the feature is small enough to be in this product
at all.

**Adding a number is a decision and a migration, deliberately.** Swap, network
throughput, disk I/O and inode usage are all defensible and all absent, and the
way to add one is to change the schema and this document rather than to fill in a
field. That friction is the point: it is what stands between six numbers and the
first custom counter.

**Custom, application-defined metrics are not a later phase of this.** Counters,
gauges, histograms and latency percentiles are the shape that was rejected, not a
shape this one grows into, and wanting them is a reason to reopen this document —
or to run one of the tools that does it well beside logaffe — rather than to add
a labelled table next to the closed one.

**The closed schema is also what keeps a sample free of text.** Every field is a
number the machine reported about itself, so no sample carries a process name, a
command line, a container label or a mount that somebody else chose the words
for. That is what makes samples safe to hand an agent without the care an entry
needs, and it is the load the closed schema carries in
[ADR 0045](./0045-a-sample-is-not-an-entry-and-may-be-read-across-projects.md) —
the mount path being the single string in the whole shape, and one the operator
wrote in their own collector's configuration.

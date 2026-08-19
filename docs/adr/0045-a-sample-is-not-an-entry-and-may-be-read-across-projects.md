# A Sample Is Not an Entry, and May Be Read Across Projects

`get_host_samples` names a host, and a host may carry several projects, so it is
the one tool on the agent interface whose answer is not confined to one project —
against [MCP](../mcp.md), which says every tool names one, exactly as the UI does.
The rule is kept for entries and not extended to samples, because what it protects
is not present here: an entry is text an application wrote, routinely containing
strings that reached it from outside
([ADR 0012](./0012-log-content-reaches-an-agent-as-data-never-as-prose.md)),
while a sample is a number the operator's own collector read off a machine. There
is one string in the whole shape, the mount path, and the operator wrote it into
their own collector's configuration
([ADR 0044](./0044-a-sample-has-a-closed-schema.md)).

Confidentiality was never what the rule was for either. There is one operator, one
agent acting for them, and an agent token that already reads every project
([ADR 0021](./0021-an-agent-token-is-a-copied-secret.md)) — so a per-project
boundary on samples would separate one person's data from itself.

## Consequences

**The rule now has to be stated with its reason.** "Every tool names one project"
was a sentence that could be checked against the interface; it becomes "every tool
that returns log content names one project", which is a sentence about what a tool
returns. [MCP](../mcp.md) says it that way, so that the next tool is measured
against the reason rather than against the count.

**Anything that later carries text off a machine falls on the entry side.** The
running processes, the container names, the command line that is using the
memory — all of them would be genuinely useful on the screen this feature exists
for, and all of them are text chosen by something other than the operator, arriving
through a credential that writes. If any of them is ever collected, it is an entry
in every way that matters here and gets the entry's handling, not a sample's. This
is the second reason the schema is closed rather than merely small.

**The host is still not a scope.** Reading a host's samples across the projects on
it is not a step towards querying entries across them: no filter takes a host, and
the answer to `get_host_samples` contains no entry, no message and no project's
data. What crosses the boundary is the machine's own numbers, which belonged to no
project in the first place.

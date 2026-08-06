# Log Content Reaches an Agent as Data, Never as Prose

An entry is delivered over MCP as structured values in named fields. It is never
rendered into markdown, never assembled into a transcript or a narrative summary,
and never concatenated into a sentence addressed to the agent. `VISION.md` holds
that log content is untrusted, that it is a prompt-injection surface, and that
this is the normal case rather than an edge one — an outsider needs no access to
the operator's systems to get their text stored verbatim, only an HTTP request to
any application that logs what it receives. This is the mechanism that claim
rests on, and without it the claim is a sentence in a document.

## Consequences

The pleasant-to-read rendering is the thing given up. A tool result that reads
like a log file is easier for a model to consume and is exactly the shape in
which a stored line stops being distinguishable from the surrounding
instructions. The structure is the boundary, and it is kept even where prose
would be shorter.

**The rendered message is a field, not a formatting instruction.** It is
delivered as text and nothing in it is interpreted — no markdown, no escape
handling, no substitution beyond the one already performed at ingestion under
[ADR 0004](./0004-the-ingestion-format-is-clef-and-the-server-renders.md)'s
narrower rule.

This decision guards a boundary rather than removing a risk. It does not make
stored text safe, and it cannot: a model that reads an instruction inside a
clearly labelled data field may still follow it. What it removes is the product's
own contribution to the confusion, and it is why agent access over MCP is
read-only by default — the two together mean the worst outcome of a hostile log
entry is a wrong answer, not an action.

# An Agent Token Is a Copied Secret

An agent authenticates with a token the operator issues, reads once and pastes
into a client's configuration. The alternative was a browser handoff — the client
receives its authorization by sending the operator into the product, where they
are already signed in, and no secret passes through their hands. That is what the
MCP specification provides for remote servers, and it is what the sibling product
just-basis decided in its own ADR 0029. It was not taken here because it would
be a second, entirely different mechanism for the second machine that talks to
this installation: **logaffe already issues copied secrets to machines**, and
making the agent work like the sender leaves one credential model pointing in two
directions — an ingest token writes to one project, an agent token reads
everything — rather than two models with nothing in common. The saving is real
beyond the symmetry: no authorization server, no redirect handling, and no matrix
of which clients can complete a browser flow.

## Consequences

**The cost is a long-lived read-everything secret that runs past the password and
the second factor.** An agent token keeps working after a password change, by
design, and nothing about it is re-checked against the operator's credentials
once it exists. There is also no moment at which the operator stands in the
product and confirms what is being allowed, which is the part of a handoff that
cannot be replicated by a token.

What bounds it is what the token model already carries: the token is named, it
records when it was last used, and it is revocable individually and immediately.
The last-used timestamp is the load-bearing one — it is what turns "which of
these can I revoke" from a guess into a reading, and without it a list of
long-lived credentials is a list nobody prunes.

The two kinds of token carry different prefixes and are refused at each other's
endpoints, so the mistake this decision makes possible — pasting the wrong one —
fails at the door rather than three layers in.

If the handoff is ever reconsidered, the thing that changed is the client
landscape, not the argument. It should replace the token rather than joining it:
a handoff beside a copied secret leaves the boundary as strong as its weaker
entrance, which is the objection just-basis raised and it holds here too.

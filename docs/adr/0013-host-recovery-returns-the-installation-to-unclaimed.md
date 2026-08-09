# Host Recovery Returns the Installation to Unclaimed

The host command does one thing: it puts the installation back into the state it
was in before anyone claimed it, and arms a fresh claim window. `VISION.md` asks
the escape hatch to cover two cases — an operator who lost their second factor
and their backup codes, and a claim window that lapsed before anyone used it —
and describes them as the same hatch. Making them the same *operation* rather
than two that share a door means one state to reason about, one code path that
can grant access to an installation, and one flow that establishes an operator,
which is the flow that already exists and is already guarded.

## Consequences

**Recovery does not reset a password; it removes the account.** Somebody reading
the command name will expect the smaller thing, so the product says plainly what
it does before it does it. Projects, ingest tokens and log entries survive — the
installation changes hands, it does not lose its contents, and senders keep
delivering throughout.

**The two kinds of token part company here, and this is the only place they do.**
An ingest token survives because the whole point of the sentence above is that an
application shipping logs through this installation does not notice; an agent
token is **removed**, because it reads every entry in every project and runs past
the password and the second factor
([ADR 0021](./0021-an-agent-token-is-a-copied-secret.md)). One credential model
pointing in two directions is what makes the asymmetry legible rather than
arbitrary: the direction that survives protects the record, and the direction that
does not would hand the reading of it to whoever held the token before the
installation changed hands. It costs the operator one paste per agent, in an act
they perform once in an installation's life, and the product hands back the
finished client configuration ([MCP](../mcp.md)).

The removal is a **step in the command** rather than a cascade, because an agent
token names no operator to be cascaded from. It runs before the account is
removed, so that a failure between the two leaves an installation that still has
its operator and has lost its agent configurations — recoverable by running the
command again — rather than live read-everything credentials on an installation
anybody can claim.

The installation is briefly claimable by anyone again, exactly as it was on first
boot. That is the same exposure, accepted for the same reason and bounded by the
same 30-minute window — and the operator running the command is at the keyboard,
which is the best moment the product ever gets for that window to be open.

There is deliberately no second, gentler mechanism **on the host** — no host-side
password reset, and no host-side way to re-enrol a second factor while keeping
the account. A second unauthenticated path into an account is a second thing to
secure, and this one is only as safe as it is because it can do nothing that the
claim flow does not already do in the open. A signed-in operator re-enrolling
their own second factor is a different act behind the full credential, and
[ADR 0016](./0016-the-second-factor-is-totp.md) provides for it.

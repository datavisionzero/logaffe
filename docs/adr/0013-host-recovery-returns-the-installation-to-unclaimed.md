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

The installation is briefly claimable by anyone again, exactly as it was on first
boot. That is the same exposure, accepted for the same reason and bounded by the
same 30-minute window — and the operator running the command is at the keyboard,
which is the best moment the product ever gets for that window to be open.

There is deliberately no second, gentler mechanism — no host-side password reset,
no way to re-enrol a second factor while keeping the account. A second path into
an account is a second thing to secure, and this one is only as safe as it is
because it can do nothing that the claim flow does not already do in the open.

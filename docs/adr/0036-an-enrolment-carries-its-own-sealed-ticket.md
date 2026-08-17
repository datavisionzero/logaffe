# An Enrolment Carries Its Own Sealed Ticket

Enrolling a second factor, and replacing one, has a shape that fits in no single
request: a new secret has to be shown, scanned and confirmed before it goes into
the row, and nothing may be stored in between — a half-written second factor is
an operator locked out of their own installation. So the installation draws the
secret and a fresh sheet of backup codes, hands both to the browser, and hands
back a **ticket** sealed under the key on the host volume. The confirming request
returns the ticket, and opening it is how the installation knows those were the
values it drew.

**This is the only enrolment there is.** The claim used to carry one and had a
ticket of its own bound to the claim window
([ADR 0035](./0035-the-claim-hands-its-enrolment-back-sealed.md)); with the
second factor out of the claim
([ADR 0041](./0041-the-second-factor-is-offered-not-required.md)) there is one
path and one ticket type, bound to the **operator** and the instant it was drawn.
Every enrolment therefore happens behind a full credential, which is a stronger
starting position than the claim's ticket ever had.

The alternative was to let the browser hand the plain new secret back with a code
computed from it, and verify by proof instead of by provenance. It is rejected
for the reason ADR 0035 rejects it: the product would no longer know that the
secret it enrolled was drawn at full entropy.

## Consequences

**A ticket is refused after a Host Recovery**, because the account it names is
gone and the account that exists afterwards never drew it. That is the same
property the claim's window binding gives, arrived at through the other end of
the same event.

**It expires**, thirty minutes after it was drawn, which is the half hour the
claim window also gives and for the same reason: it is what one person needs to
scan a code and type six digits. The deadline is not what stands between an
attacker and the account — the request that spends a ticket carries the password
and the second factor in use as well — it is a bound on how long a drawn secret
stays interesting.

**Nothing is written until the confirming request**, so an abandoned
re-enrolment leaves a ticket in a browser and an account whose authenticator
still works. There is no state to clean up and no sweep to write.

**The fresh backup codes go with it.** The ticket carries their hashes, the
confirming request writes them as the operator's set, and the previous set is
replaced wholesale
([ADR 0032](./0032-each-operator-secret-is-stored-for-what-it-is.md)) — because a
re-enrolment is exactly the event after which the old sheet must not remain a way
back to the old authenticator.

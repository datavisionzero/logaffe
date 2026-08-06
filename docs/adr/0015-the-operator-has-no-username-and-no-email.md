# The Operator Has No Username and No Email

Signing in is a password and the second factor, with nothing identifying *which*
account is meant. An installation has exactly one, so a username selects from a
set of one and is a field kept out of habit. An email address is the more
tempting of the two and is refused for a stronger reason: logaffe sends no mail,
has no need of any, and an address stored against a promise never to write to it
is an invitation to the feature that eventually does.

## Consequences

**There is no password reset, and there cannot be one.** A reset needs a channel
to a person, the product has none, and adding one would mean an SMTP
configuration in a product whose operational story is a compose file. Forgetting
the password, losing the second factor, and losing the backup codes are one event
with one answer, which is
[Host Recovery](./0013-host-recovery-returns-the-installation-to-unclaimed.md).

This keeps the account model as small as `VISION.md` claims it is. There is no
users table with one row in it, no identity to verify, no address to change, no
notification preference, and no lifecycle beyond claimed and unclaimed — the
whole dimension the vision calls out as removed is genuinely absent rather than
present with a count of one.

If mail is ever wanted for something else, this is the decision to reopen first,
and it should be reopened as a question about mail rather than answered quietly
by adding an address field to the operator.

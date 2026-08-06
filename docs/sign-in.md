# Signing In and Sessions

The claim establishes an operator; this is how they get back in afterwards, from
whatever machine they happen to be at. [Setup and the claim](./setup.md) covers
how the account comes to exist and how it is recovered when everything is lost.

## What the operator carries

A **password**, a **second factor**, and a set of **backup codes** that stand in
for the second factor. There is no username and no email address
([ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md)), so those
three are the whole of it.

**Nothing is bound to a device or a browser.** That is a deliberate property
rather than an omission: with one account, no email and no reset channel, every
binding to a particular machine is a way for the operator to lock themselves out
of their own installation by replacing a laptop. Signing in from a strange
computer has to work with nothing but what the operator knows and what is in
their pocket.

## Signing in

Open the installation's address, enter the password, enter the six digits. There
is nothing to select, nothing to remember about which account is meant, and
nothing to prepare on a machine being used for the first time.

A **backup code** may be given instead of the second factor. It is consumed when
used, and the product says how many remain whenever one is spent, because a set
that quietly runs out ends at Host Recovery.

## The second factor is TOTP

The second factor is a time-based one-time code from an authenticator app,
enrolled during the claim from a QR code and the secret in text for anyone typing
it by hand ([ADR 0016](./adr/0016-the-second-factor-is-totp.md)).

**It can be re-enrolled while signed in**, which is what makes replacing a phone
an ordinary afternoon instead of an incident. Re-enrolling asks for the password
and the current second factor — or a backup code — and it issues a fresh set of
backup codes, retiring the old set. What cannot happen is turning it off:
`VISION.md` puts it in the guided setup so that it is not optional, and a
god-mode account on the public internet does not get to become single-factor
later.

The honest cost is stated in the ADR: TOTP is phishable in a way a passkey is
not. It is chosen because it is the only common second factor that asks nothing
of the machine in front of the operator.

## The password

A minimum length and nothing else — no composition rules, no forced rotation, and
no check against an outside service, which would put a network dependency and a
disclosure into the sign-in path of a self-hosted product. Length is the property
that matters, and the second factor is what carries the rest.

Changing the password requires the current one, and it ends every other session.

## Sessions

A session lasts generously — on the order of **30 days** — and every use pushes
the deadline forward, so an installation in regular use is not a place where the
operator keeps re-authenticating.

**There is no separate "trust this browser".** The session *is* the remembering.
A second mechanism whose entire purpose is skipping the second factor would
weaken precisely the thing that makes public exposure defensible, in exchange for
convenience the sliding session already provides.

**Several sessions can exist at once**, because one person with a desktop and a
laptop is the normal case and forcing them to fight over one seat helps nobody.
Each is listed with where it was last used and when, and each can be ended
individually, along with an "end all others". With no email in the product, that
list is the only way the operator can ever notice a session that is not theirs,
which makes it a security surface rather than a convenience.

A session ends when it is signed out, when it is revoked from that list, when the
password changes, when the second factor is re-enrolled, when it goes 30 days
untouched, or when Host Recovery removes the account it belonged to.

## A wrong password never locks the account

Failed sign-ins are throttled **by where they come from**, with the delay growing
as attempts accumulate. The account itself is never locked
([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md)).

With exactly one account, a lockout is a weapon pointed at its owner: anyone able
to reach the installation could hold the operator out of it indefinitely by
guessing wrong on purpose, and the only way back would be the command that
deletes the account. The protection a lockout is meant to provide is already
there, because the second factor means a correctly guessed password on its own
opens nothing.

## What is deliberately not here

- **No "remember this browser"**, and no device trust or fingerprinting of any
  kind. Covered above.
- **No password reset, and no account recovery over the network.** Settled in
  [ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md): the answer
  is [Host Recovery](./setup.md#host-recovery).
- **No external identity.** No SSO, no OAuth, no "sign in with" anything —
  `VISION.md` makes enterprise identity a non-goal, and a single self-hosted
  account has nothing to federate with.
- **No sign-in notification.** There is no channel to send one on, and the
  session list is what serves the purpose instead.
- **No second account**, not even a read-only or break-glass one. Settled in
  `VISION.md`.

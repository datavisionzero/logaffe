# Signing In and Sessions

The claim establishes an operator; this is how they get back in afterwards, from
whatever machine they happen to be at. [Setup and the claim](./setup.md) covers
how the account comes to exist and how it is recovered when everything is lost.

## What the operator carries

A **password**, and — if they enrolled one — a **second factor** with the set of
**backup codes** that stands in for it. There is no username and no email address
([ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md)), so that is
the whole of it.

**The second factor is the operator's to enrol and to remove**
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)). It is not
part of the claim, it is offered by the guide that follows one, and an
installation running without it says so in the interface for as long as that is
true — so that having none is a decision somebody made rather than a thing that
happened.

**Nothing is bound to a device or a browser.** That is a deliberate property
rather than an omission: with one account, no email and no reset channel, every
binding to a particular machine is a way for the operator to lock themselves out
of their own installation by replacing a laptop. Signing in from a strange
computer has to work with nothing but what the operator knows and what is in
their pocket.

## Signing in

Open the installation's address, enter the password, and enter the six digits if
there are six digits to enter. There is nothing to select, nothing to remember
about which account is meant, and nothing to prepare on a machine being used for
the first time. An account with no second factor signs in on the password alone
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)).

**The code is the one field an account may leave empty**, and the screen says so
beside it. Asking for the password first and the code on a second screen would
read better and is refused for what it would give away: the installation would be
answering *that password was right*, and every way of not getting in is one
refusal here ([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md)).
So the form asks for both at once, an operator with no second factor sends
nothing in the second box, and the installation says the same thing to every
attempt that fails.

A **backup code** may be given instead of the second factor. It is consumed when
used, and the product says how many remain whenever one is spent, because a set
that quietly runs out ends at Host Recovery.

A code is **stored as a plain fast hash and cannot be read back**, which is the
deliberate opposite of a token
([ADR 0032](./adr/0032-each-operator-secret-is-stored-for-what-it-is.md)): a
token is a copy of something the operator can already reach, and a backup code is
what stands in when they can reach nothing. Being consumed is a timestamp rather
than a deletion, so how many remain is a count and a spent code stays visibly
spent.

## The second factor is TOTP

The second factor is a time-based one-time code from an authenticator app,
enrolled from the settings behind the operator's own password, from a QR code and
the secret in text for anyone typing it by hand
([ADR 0016](./adr/0016-the-second-factor-is-totp.md)).

The secret is **encrypted with the key on the host volume**, like a token, for
the plain reason that a code cannot be computed without it — so unlike the other
two credentials it is not hashed, and unlike the other two it is unusable if that
key is lost
([ADR 0032](./adr/0032-each-operator-secret-is-stored-for-what-it-is.md)).

**It can be re-enrolled while signed in**, which is what makes replacing a phone
an ordinary afternoon instead of an incident. Re-enrolling asks for the password
and the current second factor — or a backup code — and it issues a fresh set of
backup codes, retiring the old set. Both replacements are **overwrites**: the
previous secret and the previous codes are gone rather than kept beside the new
ones, and what is kept of the old enrolment is the date it happened.

**It can also be turned off**, which asks for the password and a current code —
the same credential enrolling asks for. A session that has been taken is not a
session that can strip the account down to a password, and the act that removes a
factor is not cheaper than the act that added one. What goes with it is the sheet
of backup codes, because a code that stands in for a second factor that is not
there stands in for nothing.

The honest cost is stated in the ADR: TOTP is phishable in a way a passkey is
not. It is chosen because it is the only common second factor that asks nothing
of the machine in front of the operator.

## The password

**At least sixteen characters**, and nothing else — no composition rules, no
forced rotation, and no check against an outside service, which would put a
network dependency and a disclosure into the sign-in path of a self-hosted
product. Length is the property that matters, and on an installation with no
second factor it is the only property there is
([ADR 0042](./adr/0042-the-password-carries-more-so-it-gets-longer.md)). Sixteen
is a passphrase — three words and a separator — rather than a rule about symbols.

**It is a rule about choosing a password and not about giving one.** A sign-in
takes what was typed and lets the hash answer, so an operator whose password was
long enough when they set it is never locked out by the minimum rising later —
they are asked for their password, and it is right or it is wrong. What is
refused before the hasher is only what would make it work for nothing: an empty
box, and anything past a few hundred characters.

It is stored as a **slow hash** — the framework's PBKDF2-HMAC-SHA512, at OWASP's
current figure — whose cost parameters are versioned, and a successful sign-in
rewrites the hash at the current cost, so raising that cost later is a thing the
product does on its own
([ADR 0032](./adr/0032-each-operator-secret-is-stored-for-what-it-is.md)). What
that buys and what it does not is stated rather than argued away: a stolen
database dump is the one place this credential can be attacked without limit, and
against an operator who enrolled no second factor it is the whole account. The
length above is what stands there, and it is the product's largest single
accepted risk.

Changing the password requires the current one, and it ends every other session.

## Sessions

A session lasts generously — on the order of **30 days** — and every use pushes
the deadline forward, so an installation in regular use is not a place where the
operator keeps re-authenticating.

What the browser holds is **a cookie and nothing else** — `HttpOnly`, so no
script can read the value that is the whole of the operator's standing
permission; `Secure`, because an installation on the open internet is behind TLS
and a browser already treats `localhost` as a secure origin; and `SameSite=Strict`,
because everything the operator does is at the installation's own address and
nothing in the product is linked to from elsewhere. **The cookie carries the
secret and nothing about who it belongs to**: the row is read on every request,
which is what makes ending a session from the list below take effect
immediately.

The value the browser holds is one the installation draws, and it is stored as a
**fast hash** — the same storage a backup code gets and for the same reasons
([ADR 0032](./adr/0032-each-operator-secret-is-stored-for-what-it-is.md)): it
carries all of its own entropy, so there is nothing a slow hash would defend
against, and it is not readable back. Unlike the operator's three credentials it
is not theirs to keep, and losing it costs a sign-in and nothing else.

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
password changes, when the second factor changes at all, when it goes 30 days
untouched, or when Host Recovery removes the account it belonged to.

**The list says which row is this browser**, because nothing else can: it carries
no secret and the cookie carries nothing but one, so there is nothing the
interface could compare. Without it "end all others" is a guess and ending a row
signs the operator out of the screen they are on. Ending the current one from the
list is allowed and is a sign-out by another name.

**A session that has expired is not on the list** — it admits nothing, so putting
it there would be asking the operator to recognize a browser they cannot be
signed in from. The row itself is removed by a **daily sweep**
([Operations](./operations.md#housekeeping-that-runs-on-a-timer)), which is
housekeeping rather than a security measure: expiry is what refuses the session,
and the sweep is what keeps the list from filling with rows that cannot act.

## Enrolling and replacing the second factor, and reprinting the sheet

**Enrolling and re-enrolling are one act with one optional half.** The
installation draws a secret and a fresh sheet of backup codes, shows both, and
hands back a sealed ticket carrying them
([ADR 0036](./adr/0036-an-enrolment-carries-its-own-sealed-ticket.md)); nothing
is stored until the confirming request, so the authenticator in the operator's
pocket — if there is one — keeps working until the moment it is replaced. That
request asks for the password, a code from the app just enrolled, which is what
proves the enrolment took, and, when there is already a second factor in place,
the current code or a backup code, which is the case of the phone that is already
gone. It ends every other session, and the fresh sheet replaces whatever was
there before.

**Turning it off** asks for the password and a current code, ends every other
session for the same reason, and takes the backup codes with it. Every change to
the second factor ends the other sessions: the point of them all is that the
operator notices when somebody else is signed in, and this is the moment worth
noticing.

**A fresh sheet can also be asked for on its own**, which replaces the previous
set entirely, spent codes and unspent alike. It requires the password, because
ten of these are ten ways past the second factor. It ends no session: replacing
the way back in says nothing about the browsers already signed in.

## A wrong password never locks the account

Failed sign-ins are throttled **by where they come from**, with the delay growing
as attempts accumulate. The account itself is never locked
([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md)).

Concretely: **five attempts in a burst**, which is what a person mistyping a
passphrase actually makes, and then **one every thirty seconds**, with a couple
held waiting rather than refused before the rest are answered `429`. Those are
product values, the same in every installation, and not something an operator is
asked to have an opinion about. Which address a burst is counted against is
[the reverse proxy question](./operations.md#behind-a-reverse-proxy).

With exactly one account, a lockout is a weapon pointed at its owner: anyone able
to reach the installation could hold the operator out of it indefinitely by
guessing wrong on purpose, and the only way back would be the command that
deletes the account. That is worse on an installation with no second factor
rather than better — a password-only account is one a lockout could hold hostage
just as easily — which is why the decision stands where it did.

**What the throttle can promise depends on the account it protects.** With a
second factor enrolled, a correctly guessed password on its own opens nothing.
Without one, the throttle is the whole of it and the honest statement is that
guessing is slow rather than that guessing cannot succeed
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)).

## What is deliberately not here

- **No "remember this browser"**, and no device trust or fingerprinting of any
  kind. Covered above.
- **No password reset, and no account recovery over the network.** Settled in
  [ADR 0015](./adr/0015-the-operator-has-no-username-and-no-email.md): the answer
  is [Host Recovery](./setup.md#host-recovery).
- **No external identity.** No SSO, no OAuth, no "sign in with" anything —
  `VISION.md` makes enterprise identity a non-goal, and a single self-hosted
  account has nothing to federate with.
- **No sign-in notification.** There is a channel now
  ([Alerts](./alerts.md)) and this still does not go down it. The reason was
  never only that there was nowhere to send it: an installation has one account,
  so a sign-in the operator is told about is a sign-in the operator just made,
  and a message that is right every time until the one time it is not is a
  message nobody reads by then. The session list is what serves the purpose
  instead, and it serves it by being looked at deliberately.
- **No second account**, not even a read-only or break-glass one. Settled in
  `VISION.md`.

# Signing In and Sessions

How somebody gets into an installation, from whatever machine they happen to be
at. [Setup and the first administrator](./setup.md) covers how the first account
comes to exist, how the ones after it are invited, and how an installation is
recovered when every way in is lost.

## What a user carries

An **email address**, a **password**, and — if they enrolled one — a **second
factor** with the set of **backup codes** that stands in for it. The address is
what selects the account and the only thing about it that is not a secret
([ADR 0052](./adr/0052-a-user-and-an-agent-are-one-identity.md)).

**The address is stored twice**: as it was written, which is what a list shows,
and folded — trimmed, Unicode-normalized and lowercased — which is what a sign-in
looks up and what a unique index stands on. Two spellings of one address are one
account, and the database is what says so rather than a check somewhere above it.

**The second factor is each user's to enrol and to remove**
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)). It is not
part of anything that creates an account, it is offered by the guide that follows
the first sign-in, and somebody running without it is told so for as long as that
is true — so that having none is a decision they made rather than a thing that
happened.

**Nothing is bound to a device or a browser.** That is a deliberate property
rather than an omission: every binding to a particular machine is a way for
somebody to lock themselves out by replacing a laptop. Signing in from a strange
computer has to work with nothing but what they know and what is in their pocket.

## Signing in

Open the installation's address, enter the address and the password, and enter
the six digits if there are six digits to enter. There is nothing to prepare on a
machine being used for the first time. An account with no second factor signs in
on the address and the password alone
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)).

**The code is the one field an account may leave empty**, and the screen says so
beside it. Asking for the address and password first and the code on a second
screen would read better and is refused for what it would give away: the
installation would be answering *that password was right*. So the form asks for
all of it at once, somebody with no second factor sends nothing in the last box,
and the installation says the same thing to every attempt that fails.

**Every refusal is one refusal, and they cost the same.** An address nobody
holds, a wrong password, a wrong code, a code already spent, an account that was
invited and never arrived, and one that has been deactivated are one answer, in
one wording. An address nobody holds is verified against a hash belonging to
nobody, so the absence of an account cannot be read off how quickly the refusal
came back — otherwise the wording would be careful and the clock would say it
anyway, and the sign-in would become a way of asking who has an account here.

A **backup code** may be given instead of the second factor. It is consumed when
used, and the product says how many remain whenever one is spent, because a set
that quietly runs out ends at Host Recovery.

A code is **stored as a plain fast hash and cannot be read back**, which is the
deliberate opposite of a token
([ADR 0057](./adr/0057-a-users-secrets-are-stored-for-what-they-are-and-the-password-is-argon2id.md)):
a token is a copy of something its holder can already reach, and a backup code is
what stands in when they can reach nothing. Being consumed is a timestamp rather
than a deletion, so how many remain is a count and a spent code stays visibly
spent.

## The second factor is TOTP

The second factor is a time-based one-time code from an authenticator app,
enrolled from a user's own settings behind their own password, from a QR code and
the secret in text for anyone typing it by hand
([ADR 0016](./adr/0016-the-second-factor-is-totp.md)).

The secret is **encrypted with the key on the host volume**, like a token, for
the plain reason that a code cannot be computed without it — so unlike the other
two credentials it is not hashed, and unlike the other two it is unusable if that
key is lost
([ADR 0057](./adr/0057-a-users-secrets-are-stored-for-what-they-are-and-the-password-is-argon2id.md)).

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
of the machine somebody is sitting at.

### It belongs to the user, and nobody else can touch it

**Each user enrols, replaces and removes their own**, behind their own password.
There is no act anywhere in the product by which one account changes another's
second factor, password or backup codes — not for an administrator, not over
MCP, and not from the host. An account somebody else can re-enrol a factor on is
an account they own.

**An administrator sees who has one and cannot require it.** The user list says
whether each account has a second factor, because an administrator who cannot see
that cannot have the conversation, and the state is not a secret from the person
who invited them. What there is no switch for is *making* it mandatory: a forced
enrolment is the one most likely to be dispatched with a screenshot of a QR code
and a sheet of backup codes nobody writes down, which buys the appearance of a
second factor
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)). What the
product does instead is keep saying so — to the person whose account it is, on
every screen, until they enrol one.

## The password

**At least sixteen characters**, and nothing else — no composition rules, no
forced rotation, and no check against an outside service, which would put a
network dependency and a disclosure into the sign-in path of a self-hosted
product. Length is the property that matters, and on an account with no second
factor it is the only property there is
([ADR 0042](./adr/0042-the-password-carries-more-so-it-gets-longer.md)). Sixteen
is a passphrase — three words and a separator — rather than a rule about symbols.

**It is a rule about choosing a password and not about giving one.** A sign-in
takes what was typed and lets the hash answer, so somebody whose password was
long enough when they set it is never locked out by the minimum rising later —
they are asked for their password, and it is right or it is wrong. What is
refused before the hasher is only what would make it work for nothing: an empty
box, and anything past a few hundred characters.

It is stored as **Argon2id** — 64 MiB, three passes, one lane, which is OWASP's
second recommended option and the figures planaffe and vaultaffe hash at
([ADR 0057](./adr/0057-a-users-secrets-are-stored-for-what-they-are-and-the-password-is-argon2id.md)).
Argon2id is memory-hard, which is the property that takes the advantage away from
the hardware an offline attacker would bring, and against a human-chosen password
that advantage is the whole game.

**The stored value carries its own parameters**, in the PHC form
`$argon2id$v=19$m=…,t=…,p=…$salt$hash`, and a candidate is hashed with what the
value says rather than with what the code says. Raising the figures later is one
line and a rewrite per sign-in, with no schema change and nothing to migrate: a
successful sign-in against older parameters rewrites the hash at the current
ones.

**An installation from before this crosses over on its own.** Passwords used to
be hashed with the framework's PBKDF2-HMAC-SHA512, and that format is still read:
a hash in it admits exactly as a current one does, and the next successful
sign-in rewrites it as Argon2id. Nobody is locked out, nobody is asked to reset
anything, and a password nobody signs in with keeps its old hash — which costs
nothing, because it is also a password nobody is signing in with.

What that buys and what it does not is stated rather than argued away: a stolen
database dump is the one place this credential can be attacked without limit, and
against somebody who enrolled no second factor it is the whole account. It is a
much worse deal for the attacker than it used to be, and it is still the
product's largest single accepted risk.

Changing the password requires the current one, and it ends every other session.

## Sessions

A session has **two deadlines**. It ends after **seven days without a use**, and
every use pushes that one forward, so an installation somebody works in daily is
not a place where they keep re-authenticating. And it ends **thirty days after
the sign-in** whatever happens in between: nothing pushes that one, which is what
keeps a cookie somebody took from being permanent as long as it is used. The
earlier of the two is the one that applies.

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
([ADR 0057](./adr/0057-a-users-secrets-are-stored-for-what-they-are-and-the-password-is-argon2id.md)):
it carries all of its own entropy, so there is nothing a slow hash would defend
against, and it is not readable back. Unlike a user's three credentials it is not
theirs to keep, and losing it costs a sign-in and nothing else.

**There is no separate "trust this browser".** The session *is* the remembering.
A second mechanism whose entire purpose is skipping the second factor would
weaken precisely the thing that makes public exposure defensible, in exchange for
convenience the sliding session already provides.

**Several sessions can exist at once**, because one person with a desktop and a
laptop is the normal case and forcing them to fight over one seat helps nobody.
Each is listed with where it was last used and when, and each can be ended
individually, along with an "end all others". That list is how somebody notices a
session that is not theirs, which makes it a security surface rather than a
convenience.

**Nobody sees anybody else's list**, not even an administrator: it is a record of
where a person has been, and the role is about running the installation rather
than about looking into it ([ADR 0055](./adr/0055-project-access-is-one-filter.md)).
A session id that belongs to somebody else answers exactly as one that never
existed does.

A session ends when it is signed out, when it is revoked from that list, when the
password changes, when the second factor changes at all, when either deadline
passes, when the account is deactivated, or when Host Recovery removes every
identity on the installation.

**The list says which row is this browser**, because nothing else can: it carries
no secret and the cookie carries nothing but one, so there is nothing the
interface could compare. Without it "end all others" is a guess and ending a row
signs somebody out of the screen they are on. Ending the current one from the
list is allowed and is a sign-out by another name.

**A session that has expired is not on the list** — it admits nothing, so putting
it there would be asking somebody to recognize a browser they cannot be signed in
from. The row itself is removed by a **daily sweep**
([Operations](./operations.md#housekeeping-that-runs-on-a-timer)), which is
housekeeping rather than a security measure: expiry is what refuses the session,
and the sweep is what keeps the list from filling with rows that cannot act.

## Enrolling and replacing the second factor, and reprinting the sheet

**Enrolling and re-enrolling are one act with one optional half.** The
installation draws a secret and a fresh sheet of backup codes, shows both, and
hands back a sealed ticket carrying them
([ADR 0036](./adr/0036-an-enrolment-carries-its-own-sealed-ticket.md)); nothing
is stored until the confirming request, so the authenticator in their pocket —
if there is one — keeps working until the moment it is replaced. That
request asks for the password, a code from the app just enrolled, which is what
proves the enrolment took, and, when there is already a second factor in place,
the current code or a backup code, which is the case of the phone that is already
gone. It ends every other session, and the fresh sheet replaces whatever was
there before.

**Turning it off** asks for the password and a current code, ends every other
session for the same reason, and takes the backup codes with it. Every change to
the second factor ends that user's other sessions: the point of them all is that
somebody notices when another person is signed in as them, and this is the moment
worth noticing.

**A fresh sheet can also be asked for on its own**, which replaces the previous
set entirely, spent codes and unspent alike. It requires the password, because
ten of these are ten ways past the second factor. It ends no session: replacing
the way back in says nothing about the browsers already signed in.

## The sign-in is throttled per account and per source

Failed sign-ins fill **two rolling fifteen-minute windows**: five attempts
against one normalized address, and twenty from one source address. Either one
exhausted is answered `429` until it drains
([ADR 0056](./adr/0056-sign-in-is-throttled-per-account-and-per-source.md)).
Those are product values, the same in every installation, and not something
anybody is asked to have an opinion about. Which address a source window is
counted against is
[the reverse proxy question](./operations.md#behind-a-reverse-proxy).

**It is a throttle that expires, never a lockout that has to be lifted.** Nothing
latches, no administrator has to unlock anything, and there is no state a
stranger can put somebody's account into that outlasts a coffee. That is what
makes counting per account affordable: the old refusal to do it turned on there
being exactly one account, where a limit is a weapon pointed at its owner and the
way back is the command that deletes it. With several accounts a window on one
address touches nobody else, drains on its own in minutes, and inconveniences
precisely the person whose password is being guessed.

**A successful sign-in clears the account's window and not the source's.**
Somebody who mistyped four times and then got it right is back to zero. A source
that has been working through twenty addresses has proven nothing by guessing one
of them correctly, and clearing its window on a success is exactly what a
spraying attempt would use.

**An address nobody holds fills its own window.** Counting per account must not
become a way of asking which addresses exist here, so an attempt against an
address this installation has never heard of costs the same, counts the same, and
is refused the same.

**The counters live in a bounded store in the process and are not product data.**
A restart forgets them, which is the safer end of the trade: the alternative
makes signing in depend on a table that has to be swept, or on a second service,
and an installation whose sign-in path can be taken down by its own bookkeeping
is worse than one that occasionally forgives four wrong guesses. The store is
bounded because an unauthenticated caller chooses the account key — a flood of
distinct addresses evicts rather than growing.

**In front of both sits the ordinary rate limit** every public surface carries:
a burst of five per source and one every thirty seconds after that. It runs
before a body has been read, so it can only count by where a request came from;
the two compose, one shaping a burst and the other capping the quarter hour.

**What the throttle can promise depends on the account it protects.** With a
second factor enrolled, a correctly guessed password on its own opens nothing.
Without one, the throttle is the whole of it and the honest statement is that
guessing is slow rather than that guessing cannot succeed
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)).

## What is deliberately not here

- **No "remember this browser"**, and no device trust or fingerprinting of any
  kind. Covered above.
- **No external identity provider.** Recovering a password is a one-time link to
  an address ([Setup](./setup.md)), and where no mail is configured the answer is
  still [Host Recovery](./setup.md#host-recovery).
- **No external identity.** No SSO, no OAuth, no "sign in with" anything —
  `VISION.md` makes enterprise identity a non-goal, and a single self-hosted
  account has nothing to federate with.
- **No sign-in notification**, by mail or by the notifier. A message that is
  right every time until the one time it is not is a message nobody reads by
  then. The session list is what serves the purpose instead, and it serves it by
  being looked at deliberately.
- **No self-registration.** Nobody creates their own account: an administrator
  invites an address, and that is the only way a second person exists.

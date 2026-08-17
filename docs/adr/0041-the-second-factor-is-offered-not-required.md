# The Second Factor Is Offered, Not Required

The claim establishes a password and nothing else. The TOTP second factor of
[ADR 0016](./0016-the-second-factor-is-totp.md) is enrolled afterwards by a
signed-in operator who decides to, and can be turned off again by the same
operator.

Mandatory enrolment inside the claim was the original position, and it was held
for a good reason — one god-mode account on the public internet — but it pays for
that strength in the wrong currency. It makes the first act of a new installation
depend on the claimant having an authenticator to hand at that minute, which is
not a property of the person who owns the installation but of where they happen
to be standing; it forces the claim to show a secret and ten codes before it has
anything to attach them to, which is the entire reason the claim needs machinery
to hold values it has not stored yet
([ADR 0035](./0035-the-claim-hands-its-enrolment-back-sealed.md)); and an
enrolment nobody chose is the one most likely to be dispatched with a screenshot
of the QR code and a sheet of backup codes that is never written down. What that
buys is the *appearance* of a second factor.

**The cost is real and is not compensated elsewhere.** An installation whose
operator declines is an installation behind one password, and this document does
not pretend otherwise. Two things bound it: the sign-in rate limits
([ADR 0017](./0017-a-wrong-password-never-locks-the-account.md)) apply either way,
and the interface says the second factor is off for as long as it is off, so that
the state is a decision rather than an oversight. Neither of those is a
substitute, and the honest summary is that the product moved a judgement about
one operator's threat model from itself to that operator.

## Consequences

**The claim becomes a single request**, which is what makes ADR 0035
unnecessary: with nothing to carry between two requests, there is no ticket to
seal and no window to bind it to. The sealed-ticket mechanism survives where it
was always needed, behind a signed-in operator
([ADR 0036](./0036-an-enrolment-carries-its-own-sealed-ticket.md)), and there is
now one enrolment path rather than two that must be kept saying the same thing.
[ADR 0014](./0014-the-claim-is-atomic-and-holds-nothing.md) survives unchanged in
substance and cheaper to hold: a claim with one step is atomic for free.

**Backup codes belong to the second factor, not to the account.** They are issued
when it is enrolled and replaced when it is re-enrolled, and an operator without a
second factor has none and needs none. They were never the way back from a
forgotten password — that has always been Host Recovery
([ADR 0013](./0013-host-recovery-returns-the-installation-to-unclaimed.md)) — and
attaching them to the claim implied otherwise.

**The sign-in keeps one shape and lets the code be empty.** Asking for the
password on one screen and the code on the next is what an optional second factor
seems to want, and it is refused: the installation would be answering *that
password was right*, which is precisely what
[ADR 0017](./0017-a-wrong-password-never-locks-the-account.md) keeps it from
saying to an unlimited guesser. So both are asked for at once, an account with
none sends nothing in the second field, and every failed attempt gets the same
answer as before.

**Turning it off asks for the password and a current code**, the same credential
that enrolling asks for. A session that has been taken is not a session that can
strip the account down to a password, and the act that removes a factor is not
cheaper than the act that added one.

**Two earlier decisions predicted this and named themselves as its cost.**
[ADR 0017](./0017-a-wrong-password-never-locks-the-account.md) and
[ADR 0032](./0032-each-operator-secret-is-stored-for-what-it-is.md) both rest on
a correct password opening nothing on its own, and 0032 says in as many words
that they are reopened together if the second factor ever becomes optional. It
has, so they are. 0017 survives on its own terms — what it refuses is a lockout,
and a lockout is a worse idea on a password-only installation, not a better one —
while what it can promise shrinks from *guessing cannot succeed* to *guessing is
slow*. 0032 does not survive on its own terms: the password hasher was chosen
because a cracked password was worthless, and on an installation with no second
factor it is worth everything. That is answered in
[ADR 0042](./0042-the-password-carries-more-so-it-gets-longer.md), where the
password gets longer rather than the hasher getting replaced.

**An installation can end up with neither factor recoverable and that is
unchanged.** The way back is the host, for a lost password and a lost
authenticator alike, and making the second factor optional removes one of the two
cases rather than adding one.

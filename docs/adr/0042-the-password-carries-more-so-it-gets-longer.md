# The Password Carries More, So It Gets Longer

[ADR 0032](./0032-each-operator-secret-is-stored-for-what-it-is.md) chose
PBKDF2-HMAC-SHA512 out of the shared framework over Argon2id, and paid for that
with the second factor: a cracked password on its own opened nothing.
[ADR 0041](./0041-the-second-factor-is-offered-not-required.md) took that premise
away, and 0032 says in as many words that this has to be answered. It is answered
here, and the answer is not the hasher.

**The hasher stays**, at PBKDF2-HMAC-SHA512 and OWASP's current figure of 210,000
iterations, which is where it already sits and is above the framework's own
default. There is no work factor to raise: the number is the recommended one, and
paying past it buys arithmetic that a GPU discounts anyway, at a cost every
sign-in pays. Argon2id would buy something real — memory hardness is the thing
PBKDF2 does not have — and it is still a third-party package on the public,
pre-authentication sign-in path of a product whose whole case is being small.
That trade did not get cheaper because the premise changed; it is the same
package it always was.

**The minimum length goes from twelve characters to sixteen.** What changed is
what a cracked password is worth, and the property that decides how hard it is to
crack is the one the product can actually set. Twelve was chosen while the second
factor stood behind it and was written down as such; sixteen is what it is worth
without one, and it is still a passphrase — three words and a separator — rather
than a composition rule. No installations exist yet, so this costs nobody a
password change.

## Considered alternatives

**A minimum that depends on whether a second factor is enrolled** — twelve with
one, sixteen without. It is rejected because the two acts have an order: an
operator sets a sixteen-character password, enrols, and is then free to turn the
second factor off and keep a password the rule would no longer allow. Enforcing
it at that moment means refusing to disable the second factor until the password
is changed, which is a rule that fires in the one place an operator is already
being asked to make a considered choice. A minimum that can be walked around by
doing two permitted things in a particular order is not a minimum.

**Requiring the second factor after all**, which is the honest way to keep 0032's
premise. That is ADR 0041's decision and not this one's to relitigate.

## Consequences

**The minimum applies to choosing a password and not to presenting one.** A
sign-in, a password change, an enrolment and every other act that asks for the
password again reads what was typed and lets the hasher answer. The alternative
is what this document originally shipped and it is a trap: raising the minimum
would lock out every operator whose password was long enough when they set it,
at the moment they upgrade, with Host Recovery — which removes the account — as
the only way back. A rule that arrives with an upgrade and takes the installation
with it is not a rule about passwords, and the honest answer to a short one
presented is that it is either right or wrong. What survives on that path is the
*maximum*, because that is not a rule about passwords either: it is a bound on
what a public, pre-authentication surface may ask PBKDF2 to do.

**A stolen dump ground offline is still the place this credential is attacked
without limit**, and on an installation with no second factor what it yields is
everything. Sixteen characters and 210,000 iterations are what stands there. This
is stated rather than argued away, exactly as 0032 stated it, and it is now the
product's largest single accepted risk.

**Raising the count later still costs one line.** The stored format carries its
own iteration count, a sign-in against an older hash comes back out of date and
rewrites itself, and none of that was affected by anything here.

**The rule stays in the domain**, on `Password`, where it was — a minimum is a
decision that does not change when the algorithm does, and that separation is
ADR 0032's and holds unaltered.

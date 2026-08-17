# The Second Factor Is TOTP

This decides *what* the second factor is. Whether an installation has one at all
is the operator's to decide, and it is not part of the claim
([ADR 0041](./0041-the-second-factor-is-offered-not-required.md)).

The second factor is a time-based one-time code from an authenticator app, not a
passkey. A passkey is bound to the origin and therefore cannot be phished, which
is a genuine advantage for an account that can do everything in an installation
reachable by anyone — and it is bound to a device or a synced keychain, which is
the property this product cannot pay for. There is one account, no email, and no
reset channel, so the operator must be able to sign in from a machine they have
never touched, with nothing but what they know and what is in their pocket. TOTP
asks nothing of the computer in front of them.

## Consequences

**Phishing stays an open risk and is not mitigated elsewhere.** An operator led
to a convincing copy of their own installation's sign-in page can have both
factors relayed in real time. What bounds it is that the target is a self-hosted
address known to one person rather than a service with a population to fish in,
and that is a statement about who would be attacked rather than about the
mechanism being sound.

The second factor is **enrolled and re-enrolled while signed in**, asking for the
password and — when there is already one in place — either the current code or a
backup code, and issuing a fresh set of backup codes when it succeeds. Replacing
a phone is an ordinary event, and a product where it costs the installation would
be one where operators avoid enrolling properly. This is not in tension with
[ADR 0013](./0013-host-recovery-returns-the-installation-to-unclaimed.md), which
refuses a *host-side* re-enrolment: that path is unauthenticated by nature, this
one is behind the full credential.

If passkeys are ever reconsidered, the thing that changed is the fallback, not
the preference. Passkeys as an *additional* factor alongside TOTP would leave the
account exactly as phishable as it is now, because an attacker picks the weaker
route; the only version worth having is one where TOTP goes away, and that trades
the strange-machine case for phishing resistance.

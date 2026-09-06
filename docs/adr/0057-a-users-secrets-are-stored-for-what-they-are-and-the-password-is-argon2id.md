# A User's Secrets Are Stored for What They Are, and the Password Is Argon2id

Every user carries up to three secrets and each is stored differently, for the
reason [ADR 0032](./0032-each-operator-secret-is-stored-for-what-it-is.md) gave
and which has not changed: what the product has to be able to *do* with each of
them differs. The **password** is hashed slowly and is never held; a **backup
code** is hashed once with SHA-256 and is never recoverable; the **TOTP secret**
is encrypted under the key on the host volume, because a code cannot be computed
without it
([ADR 0022](./0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)).

This supersedes 0032 in both of its halves. Its subject was *the operator*, who
no longer exists ([ADR 0052](./0052-a-user-and-an-agent-are-one-identity.md));
and its choice of hash was PBKDF2, which is replaced by **Argon2id**.

## Why Argon2id now, when 0032 said the framework's hasher

0032's reasoning was sound and is worth restating, because what it turned on is
what moved. Argon2id is the stronger answer — it is memory-hard, which takes the
advantage away from the hardware an offline attacker brings — and against it
stood one fact: there is no Argon2 anywhere in .NET 10, so it is a third-party
package on the sign-in path of a product whose case is being small.
`PasswordHasher<T>` arrives with the shared framework at no dependency cost.

Three things changed.

**The dependency is no longer a first.** planaffe and vaultaffe both took
`Konscious.Security.Cryptography.Argon2`, in the same version. The cost of the
package is a cost the family already pays, and being the one product that hashes
differently is itself a cost — it is the last hard technical divergence between
three codebases that are otherwise nearly identical.

**What a cracked password is worth went up.** 0032 was affordable because the
second factor was mandatory and a cracked password on its own opened nothing.
[ADR 0041](./0041-the-second-factor-is-offered-not-required.md) removed that, and
0032 said in as many words that it was reopened by it — the answer at the time
was [ADR 0042](./0042-the-password-carries-more-so-it-gets-longer.md), a longer
minimum length rather than a better hash. That was the right small answer for one
account. An installation now holds several accounts behind one publicly reachable
sign-in form, several of them without a second factor, and a stolen dump ground
offline yields all of them. That is the case the expensive hash was designed for.

**The password path is open anyway.** The hasher is a port
([ADR 0030](./0030-the-solution-is-four-layers-not-one-project.md)), the
verification and rehash path is being rewritten from the operator to the user
regardless, and doing the swap now costs a fraction of doing it as its own
undertaking later.

## Consequences

**Nobody is locked out and nobody has to reset anything.** The hash carries its
algorithm, version, salt and cost parameters in the encoded value, exactly as it
carried a version marker before, so the verifier reads both formats. A PBKDF2
hash is verified as PBKDF2 and **rewritten as Argon2id on the next successful
sign-in**. The distinction the code already draws — between a password change,
which ends every other session, and a silent rehash, which must not — is what
makes that safe.

**A password that is never signed in with keeps its old hash**, which costs
nothing, because it is also a password nobody is signing in with.

**The parameters can rise later without a schema change**, which was the whole
point of the marker in 0032 and stays the point here. The 256 characters the
column already holds are room for that.

**A stolen database dump is still grindable, and that is still the accepted
cost** — it is simply a much worse deal for the attacker than it was. It is
stated rather than argued away, because `VISION.md` expects dumps to exist on
other machines.

**Everything 0032 said about the other two secrets stands.** A backup code gets a
fast unsalted hash deliberately: the installation generates it at full entropy,
so there is no candidate list to grind and no precomputed table to salt against,
and what it needs is a constant-time comparison. It is consumed by a timestamp
rather than a deletion, so *how many remain* is a filtered count. A fresh set
replaces the previous one wholesale. The TOTP secret is encrypted, so an
installation restored without its key cannot verify a code at all and the backup
codes are the way in.

**What changes is whose secrets they are.** All three belong to a user rather
than to the installation, and each user's second factor is their own to enrol and
to remove ([ADR 0041](./0041-the-second-factor-is-offered-not-required.md) is
unchanged in substance and now reads per account).

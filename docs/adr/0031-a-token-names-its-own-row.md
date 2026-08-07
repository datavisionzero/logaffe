# A Token Names Its Own Row

A token carries a non-secret identifier beside its secret, and a delivery is
authenticated by looking the row up by that identifier, decrypting the one token
it finds, and comparing the secret halves in constant time.
[ADR 0022](./0022-a-token-is-recoverable-and-encrypted-rather-than-hashed.md)
stored a token encrypted rather than hashed so that the operator can read one
back, and a randomized ciphertext is a different value every time it is
written — so the lookup that hashing would have given away for free is gone, and
the ingest path meets that fact on every single delivery. Three other things
could have replaced it, and each buys the same flat lookup for a price this one
does not pay. **Decrypting every token and comparing** adds nothing at all, but
it scales with how many tokens an installation holds, on the hottest path in the
product. **Deterministic encryption** makes the ciphertext column its own key
and gives up semantic security to do it, turning the nonce into a decision of
its own. A **blind index** — `HMAC(key, token)` stored beside the ciphertext —
keeps the encryption randomized, and costs a second purpose for the key material
on the host volume that the backup, a key rotation and every re-encryption
afterwards all have to carry.

The identifier is the only answer that introduces no further cryptography, and
the token was already half public: `docs/projects.md` gives it a recognizable
prefix so that a scanner can find one that leaked. A token is therefore
`<prefix>_<identifier>_<secret>`, and the part that admits a delivery is the
last one alone.

## Consequences

**This is decided now because it cannot be decided later.** The identifier lives
inside the token, so an already-issued token cannot acquire one without being
reissued — deciding this after the first installation ships means rotating
every credential in it. Nothing is deployed, which makes today the only cheap
moment.

**A bad token still learns nothing.** `docs/ingestion.md` requires that a
delivery with a bad token is answered `401` and reveals neither whether the
project exists nor whether the token once did, and an identifier that misses a
row is exactly the case that would otherwise answer faster than one that finds a
row and mismatches. The two have to cost the same: the comparison is
constant-time, and a lookup that finds nothing still runs one against a dummy
value rather than returning early.

**The identifier is public, and it is not a capability.** It travels in the
token, and once ADR 0022 lets the operator read a token back it appears wherever
the token appears. It names a row and admits nothing, so it can be indexed, put
in a log line, and — unlike the secret — compared with an ordinary equality.

**The secret half carries the entropy on its own.** Splitting a credential into
a public and a secret part is only sound if the secret part would have been
sufficient alone, so the identifier is added to the token rather than taken out
of it, and the token is that much longer. `docs/ingestion.md` calls it a
high-entropy random value, and that is now a statement about the last part.

**The lookup is flat and stays flat.** How many tokens an installation holds
stops being a question the ingest path asks, which is what lets a project keep
two during a rotation, and an operator keep a handful of agent tokens, without
any of it reaching the hot path.

**One mechanism, two prefixes.** Ingest and agent tokens have the same shape,
as [ADR 0021](./0021-an-agent-token-is-a-copied-secret.md) intends, and the
prefix is what refuses each at the other's endpoint. That check now happens
before the lookup rather than after it, so the mistake that will
happen — pasting one where the other belongs — fails without touching the
database.

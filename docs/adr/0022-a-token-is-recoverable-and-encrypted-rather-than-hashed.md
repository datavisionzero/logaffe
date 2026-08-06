# A Token Is Recoverable, and Encrypted Rather Than Hashed

Ingest tokens and agent tokens are stored encrypted, with the key kept on the
host volume beside the rest of the installation's secrets and never in the
database, and a signed-in operator can read one back at any time. The instinct is
to hash a credential and show it once, and that instinct comes from products
where the person looking and the credential they are looking at have different
reach. Here they do not: there is one account, it can do everything, and a token
grants strictly **less** than the session displaying it — an agent token reads
logs the operator is already reading, and an ingest token writes to a project the
operator owns. Hiding it protects nothing and costs the re-issue-and-redeploy
cycle every time one is mislaid.

## Consequences

**A stolen backup yields no usable credential.** `VISION.md` asks operators to
back the database up and to automate it, so database dumps will exist on other
machines and in other people's storage — that is the leak this decision has to
survive, and keeping the key out of the database is what makes it survivable.

**A host compromise is not survived**, since an attacker there holds the database
and the key together. That is accepted without argument: at that point they also
hold the log files, the volume and the running process, and the token is the
least of it.

The operator's **password stays hashed**, and so do the **backup codes**. Neither
is a thing the product ever needs to reproduce, and a human recovery factor is
not a machine credential — the reasoning above turns on a token being a copy of
something the operator can already reach, which a recovery code is precisely not.

**A leaked ingest token lets someone write entries the operator and their agent
may later read as true.** That is a real harm and the one hashing would have
bounded, since a write is a capability the database does not itself confer. It is
accepted in exchange for one credential model rather than two, and it is bounded
in turn by the token being revocable immediately and by its last-used timestamp
making an unexpected sender visible.

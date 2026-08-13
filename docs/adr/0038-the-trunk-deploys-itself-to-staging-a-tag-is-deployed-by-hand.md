# The Trunk Deploys Itself to Staging, a Tag Is Deployed by Hand

The project runs two installations of its own, and they are upgraded by two
different rules. Staging exists to be lived with, so it tracks the trunk: it is
replaced whenever `main` moves and is green, which is what
[`ci.yml`](../../.github/workflows/ci.yml) publishes `:main` for. Production is
an installation like any other operator's — it runs `:latest`, which only a
release tag moves, and it is upgraded deliberately by the path
[Upgrades](../operations.md#upgrades) documents.

The alternative is one environment, upgraded when it feels safe, which is what
the project has today. It answers neither of the questions staging exists to
answer: whether the migration chain survives contact with data that has been
sitting there for weeks, and whether the documented upgrade path is still the
one that works.

## The installation pulls; CI does not push

The self-hosted runners are machines on a home network, and none of them is the
host the installations run on. A deploy job would therefore mean an SSH key as a
repository secret and an inbound path from CI into that host — on a repository
that is about to be public, whose runners are not ephemeral, that is a
credential worth stealing. A timer on the host that pulls needs no secret in
GitHub, no inbound connection, and no change when the repository's visibility
changes.

What that accepts is roughly a minute between a green trunk and a running
staging, and a deployment whose outcome is not visible in the Actions run that
caused it. Both are affordable for one operator. Neither would be for a team
waiting on a deploy button, which is the condition under which this record
should be reopened.

## Production is not on a timer

[`docs/operations.md`](../operations.md) says plainly that logaffe does not
check for versions, announce them, or update itself, and that
`docker compose pull` is the operator's to run. An installation of ours that
updated itself would contradict the document it ships.

Staging is the project's own infrastructure and may do what the product does
not. Production is an installation like anyone else's and takes the documented
path, which is also the only way we find out that the documented path still
works.

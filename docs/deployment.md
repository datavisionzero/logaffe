# Deploying

Two things get deployed. **The installation** goes on the open internet on
purpose, and most of this page is about the shape that puts in front of it,
because that shape has parts which are easy to assemble wrongly and quiet about
it. **A collector** goes on each machine the operator wants numbers from
([Metrics](./metrics.md)), and is at the end, because it is one command.

`VISION.md` puts an installation on the open internet on purpose, and refuses a
VPN, a tunnel or an authenticating proxy as the answer to a security question.
What it still needs from somebody is **TLS**, and the moment anything terminates
that in front, every request appears to come from that thing instead of from its
caller. [Operations](./operations.md#behind-a-reverse-proxy) states the setting
that fixes it and why an unset one trusts nothing. This page is the shape of a
deployment in which that setting is true, because the setting is easy to fill in
wrongly and an installation gives no sign of it.

One shape, not the only one. An installation reached directly needs none of what
follows.

## The shape

```
  internet ──:443──> proxy ──:8080──> logaffe ──> db
                     └ logaffe-edge ┘ └── default ─┘
                      172.30.0.0/24    the stack's own
```

Four things hold it together, and each of them is load-bearing:

- **The proxy terminates TLS.** logaffe serves plain HTTP on `8080`, holds no
  certificate and knows nothing about one.
- **The proxy and the installation share one Docker network with a range the
  operator chose**, rather than one Docker picked.
- **The installation publishes no ports at all.** Nothing on the host reaches it
  but the proxy.
- **`Logaffe__TrustedProxies` is that range**, which is the only reason the
  address a request is throttled by and listed under is the caller's.

### The network

It belongs to neither stack, so it is created once and named by both:

```
docker network create --subnet 172.30.0.0/24 logaffe-edge
```

The range is stated rather than assigned, and that is the point: a range Docker
chooses is a range that can come back different after the network is recreated,
and the variable below names it in writing.

### The installation

`deploy/docker-compose.yml` publishes `8080:8080`, which is right for trying it
out on a laptop and wrong here. An override beside it takes the port away and
joins the shared network:

```yaml
# docker-compose.override.yml
services:
  logaffe:
    ports: !reset []
    networks:
      default:
      edge:

networks:
  edge:
    name: logaffe-edge
    external: true
```

Two details, and both are easy to get wrong:

**`!reset` is what removes a published port.** Compose merges the `ports` of two
files rather than letting the later replace the earlier, so a plain `ports: []`
in an override leaves `8080` published on every interface of the host.

**`default` has to be named beside `edge`.** A service that lists networks is on
those and no others, and the one it would otherwise lose is the one the database
is on.

An override named that way beside the Compose file is picked up by a plain
`docker compose up -d`; one kept elsewhere — beside a clone that is not edited,
say — is composed explicitly and in order:

```
docker compose -p logaffe \
  -f /path/to/repo/deploy/docker-compose.yml \
  -f /path/to/config/docker-compose.override.yml up -d
```

### The proxy

Anything that terminates TLS and sets `X-Forwarded-For` does; below is Caddy,
because a certificate that obtains and renews itself is one thing fewer to
operate. The proxy's own stack joins the same external network, and reaches the
installation by its service name on it:

```
logs.example.com {
	reverse_proxy logaffe:8080
}
```

**Three headers matter, not one.** `X-Forwarded-For` is the address, and
`X-Forwarded-Proto` and `X-Forwarded-Host` are what the installation writes into
the blocks it hands over — the delivery snippet
([Setup](./setup.md#after-the-claim)) and an agent's MCP configuration
([MCP](./mcp.md)) carry the name the operator reached it by, which without those
two is `http://` and a container port. Caddy sends all three; a proxy configured
by hand is worth checking on this point.

If more than one installation shares the network, they all answer to `logaffe`,
because the service name in the published Compose file is the same in each. Give
each one an alias on the shared network — `aliases: [logaffe-staging]` under
`edge:` — and let the proxy use that.

### The variable

In the `.env` file beside the Compose file:

```
LOGAFFE_TRUSTED_PROXIES=172.30.0.0/24
```

The published Compose file maps it into `Logaffe__TrustedProxies`. It is the
network's range and not the proxy's single address, because the address inside
that network is Docker's to hand out and is handed out again when the container
is recreated.

## Why not publish on loopback

The obvious alternative is to publish `127.0.0.1:8080:8080` and proxy from the
host. It looks tighter than a shared network and is not, because **the address
the installation sees is not loopback**: a connection through a published port
arrives from the gateway of the container's own bridge network, so the value
that would have to be trusted is a Docker address, on a range Docker chose and
can choose differently. What ends up in the variable is then something wide
enough to survive that — `172.16.0.0/12` — which is a great many addresses
nobody vouched for, on a host where anything else may later be running.

Trusting too broadly costs both of the things the product uses a source address
for: a sign-in throttle partitioned by a value the caller picks
([ADR 0017](./adr/0017-a-wrong-password-never-locks-the-account.md)), and a
session list showing whatever an intruder wanted it to show
([Signing in](./sign-in.md#sessions)). The setting looks configured, the
installation serves perfectly, and neither of those is doing its job.

## Claiming a deployment of this shape

The moment the proxy obtains a certificate, the hostname is in the public
Certificate Transparency logs, which [Setup](./setup.md#the-claim-window) names
as *the* way a fresh installation gets found — within seconds, not within days.
What that costs depends on which of the two guards the installation was brought
up with.

**With a claim secret**, which is the default, it costs nothing. Whoever finds
the hostname finds a door they cannot open, the order of the steps above stops
mattering, and the claim happens whenever the operator gets to it. This is the
mode a deployment of this shape wants: there are four moving parts in front of
the installation, and the one thing you do not also want is a clock.

**In window mode** the order is decided for you: the network, the proxy and its
certificate first, the installation last, and the claim walked while it is all
still fresh. The window is **30 minutes from the first run** — from when the
installation first runs, not from when it first answers — so twenty minutes of
DNS trouble after the container is already up is two thirds of it gone on
something unrelated to it.

A window missed, or a secret lost, is
`docker compose exec logaffe logaffe recover`
([Host Recovery](./setup.md#host-recovery)) — a nuisance rather than a loss, and
still worth not needing.

Either way, enrol the second factor while sitting there, and put its backup codes
somewhere that is not this host. It is not part of the claim
([ADR 0041](./adr/0041-the-second-factor-is-offered-not-required.md)), the guide
after the claim offers it first, and an installation of this shape — one account,
reachable by name, on the open internet — is the case the offer is aimed at.

## The check that says it worked

Sign in and open the sessions list. **The address beside the current session is
the browser's, or the setting is wrong.** A `172.30.0.x` there is the proxy, and
means the header is being ignored: either the range does not cover the address
the proxy actually connects from, or the variable never reached the container —
`docker compose config` shows the value the stack will start with.

There is no other way to tell. Nothing fails, nothing is logged, and the two
places the address matters are both places nobody looks until the day they
matter.

## The collector on a machine

A collector reports one machine, so one runs on each machine the operator wants
numbers from — which is not the machine the installation is on, except by
coincidence.

**It is handed over rather than assembled.** Issuing a host's token in the
settings gives back the command below with this installation's address, that
token and the mounts already filled in ([Metrics](./metrics.md#the-collector)),
and the same command comes back whenever the token is read. What follows is what
is in it, so that an operator can see what they are running:

```
docker run -d --name logaffe-collector --restart unless-stopped \
  -v /proc:/host/proc:ro \
  -v /:/rootfs:ro,rslave \
  -e LOGAFFE_ENDPOINT=https://logs.example.com \
  -e LOGAFFE_HOST_TOKEN=logaffe_host_… \
  -e LOGAFFE_MOUNTS=/ \
  ghcr.io/datavisionzero/logaffe-collector:latest
```

**The two mounts are what a container needs to see its host.** `/proc` is where
the processor, the memory and the load are read; the root filesystem is how the
mounts named in `LOGAFFE_MOUNTS` are measured. `rslave` on the second is what
makes a filesystem mounted after the collector started visible to it — without
it, a disk added next month is a disk the collector never reports.

**That is the whole of what it asks for.** It is not `--privileged`, it does not
join the host's PID namespace, and it never sees the Docker socket. Reading
processes is what would need the first and reading containers the second, and the
closed schema collects neither
([ADR 0044](./adr/0044-a-sample-has-a-closed-schema.md)) — so the smallest ask is
available, and it is the one taken.

**It publishes no port and takes no inbound connection.** The collector opens an
outgoing HTTPS connection to the installation and nothing ever connects to it, so
a machine that reports needs no firewall rule, no proxy in front of it and no
address anyone has to be able to reach. That is a deliberate consequence of
collectors pushing rather than the installation scraping (`VISION.md`), and on a
fleet of machines it is the difference between one exposed surface and one per
machine.

**On a machine that already runs a Compose stack**, the same thing is a service in
it, which is worth preferring when there is a stack to put it in — it is then
pulled and restarted by whatever already pulls and restarts that stack:

```yaml
  logaffe-collector:
    image: ghcr.io/datavisionzero/logaffe-collector:latest
    restart: unless-stopped
    volumes:
      - /proc:/host/proc:ro
      - /:/rootfs:ro,rslave
    environment:
      LOGAFFE_ENDPOINT: https://logs.example.com
      LOGAFFE_HOST_TOKEN: ${LOGAFFE_HOST_TOKEN}
      LOGAFFE_MOUNTS: /
```

### Upgrading it is the installation's arrangement, one machine at a time

The collector image is built and tagged by the same workflows as the
installation, so `:latest` moves when a release tag is pushed and `:main` follows
the trunk — the arrangement of
[ADR 0038](./adr/0038-both-installations-pull-on-a-timer-and-the-tag-is-the-deliberate-act.md),
with the pulling timer on each reporting machine rather than only on the
installation's host.

**A collector is upgraded on its own schedule, and may be older than the
installation it reports to.** There is no coordinated upgrade and no version
handshake, because the alternative is an operator who has to walk a fleet before
they can upgrade the one thing that matters. What that requires of the product
instead is that **the sample format only ever grows**: a number may be added, and
an installation reads a delivery that lacks it as a delivery that lacks it. A
change that made an old collector's delivery invalid would be a change that turns
`docker compose pull` on the installation into a silent stop of every machine's
reporting, which is precisely the failure nobody would look for.

**No backup goes before this one.** ADR 0038 puts an artifact before an
unattended upgrade because there is no downgrade; a collector holds no state at
all, so the rollback is the previous tag and the cost of getting it wrong is a
gap in a band.

### The check that says the collector worked

Open *hosts* in the installation's settings. **The host reports within a minute of
the collector starting, or something in the command is wrong.** A host that has
never reported and a host that stopped reporting look different there — the first
has no last-reported at all — and that distinction is the one worth having while
setting one up.

A collector that cannot reach the installation, or holds a revoked token, says so
in its own container log and does nothing else: it drops the sample and takes the
next one a minute later ([Metrics](./metrics.md#the-collector)). Nothing retries,
nothing accumulates, and a machine that was unreachable for an hour has an hour
of gap rather than an hour of samples arriving at once.

## What is deliberately not here

- **No TLS in the product.** logaffe holds no certificate and terminates
  nothing. That, and nothing else, is what a proxy in front of it is for.
- **No authentication in front of it.** Settled in `VISION.md`: an auth layer in
  the proxy is not an acceptable answer to a security question about logaffe,
  and this page adds none.
- **No private-network deployment.** The same passage refuses "run it where
  nobody can reach it" as a security answer, so there is no variant here that
  keeps the installation off the internet.
- **No worked example of a real host.** The hostnames, ranges and paths above
  are examples and stay examples; a particular deployment's are that
  deployment's own business.
- **No orchestrator but Compose.** `VISION.md` makes Docker Compose the standard
  way an installation is run, and it is the only one this documentation
  describes or tests.
- **No collector that is not a container.** There is no package, no systemd unit
  and no binary to place on a machine. A second way to run it is a second thing
  to release, document and answer questions about, for a machine that in this
  product is already running containers — it is what the projects on it are.
- **No inbound path to a collector.** It is never scraped, never polled and never
  told anything; the installation does not know where its collectors are and
  could not reach one if it did.
- **No collector without a host.** A token is issued for a host the operator
  created, exactly as an ingest token is issued for a project they created
  ([Projects and tokens](./projects.md)), and there is no delivery that brings a
  host into existence.

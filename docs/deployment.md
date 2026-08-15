# Deploying an Installation

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

## Claim it while the certificate is new

The moment the proxy obtains a certificate, the hostname is in the public
Certificate Transparency logs, which [Setup](./setup.md#the-claim-window) names
as *the* way a fresh installation gets found — within seconds, not within days.
The claim window is **30 minutes from the first run** and a restart does not
extend it.

It opens when the installation first runs rather than when it first answers,
which decides the order: the network, the proxy and its certificate first, the
installation last, and the claim walked while it is all still fresh — password,
second factor, backup codes, the last two stored somewhere that is not this
host. Twenty minutes of DNS trouble after the container is already up is two
thirds of the window gone on something unrelated to it.

A window missed is `docker compose exec logaffe logaffe recover`
([Host Recovery](./setup.md#host-recovery)) — a nuisance rather than a loss, and
still worth not needing.

## The check that says it worked

Sign in and open the sessions list. **The address beside the current session is
the browser's, or the setting is wrong.** A `172.30.0.x` there is the proxy, and
means the header is being ignored: either the range does not cover the address
the proxy actually connects from, or the variable never reached the container —
`docker compose config` shows the value the stack will start with.

There is no other way to tell. Nothing fails, nothing is logged, and the two
places the address matters are both places nobody looks until the day they
matter.

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

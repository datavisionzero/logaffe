# logaffe

A self-hostable, central logging tool for a single operator and their AI agent.
It collects logs from many applications, keeps them separated by project, and
makes them accessible through a web UI and through MCP — safe enough to expose
directly to the public internet.

See [VISION.md](VISION.md) for what logaffe is and, just as importantly, what it
deliberately is not.

## Status

The product is built. An installation ingests CLEF over HTTP,
stores and queries it, and is claimed by the one operator it belongs to. Behind
their sign-in are the projects and their ingest tokens, the log view with its
filters and its live tail, and the settings for the installation and for each
project. The same reads are offered to an agent as four MCP tools. `logaffe
backup` writes both halves of an installation into one artifact and `logaffe
restore` puts them back; `logaffe recover` is the way back in when the sign-in
is lost. All of it has been exercised end to end against a running installation.

**The first stable release is out**, and it is what
[`deploy/docker-compose.yml`](deploy/docker-compose.yml) pulls:

```
ghcr.io/datavisionzero/logaffe:latest
```

[`Logaffe.Client`](https://www.nuget.org/packages/Logaffe.Client),
[`Logaffe.Serilog`](https://www.nuget.org/packages/Logaffe.Serilog) and
[`Logaffe.Extensions.Logging`](https://www.nuget.org/packages/Logaffe.Extensions.Logging)
carry the same number, because one tag releases all four of them at once.

**`:latest` moves on a stable tag and never on a prerelease**, so no installation
is carried into a version nobody asked for
([ADR 0038](docs/adr/0038-both-installations-pull-on-a-timer-and-the-tag-is-the-deliberate-act.md)).
One that wants to stay on a number names it in that file instead; the documented
`docker compose pull` works as written either way.

See [VISION.md](VISION.md) for where it is going and
[docs/codebase.md](docs/codebase.md) for how the repository is laid out.

Working on it needs the .NET 10 SDK, Node 24 or newer, and Docker:

```
docker compose -f deploy/docker-compose.dev.yml up -d   # Postgres
dotnet run --project src/Logaffe.Api                    # the server
npm --prefix src/web install && npm --prefix src/web run dev
```

## Security

logaffe is meant to be reachable from the open internet. To report a
vulnerability, see [SECURITY.md](SECURITY.md).

## License

logaffe is released under the MIT License. See [LICENSE](LICENSE).

## Trademark

"logaffe" is a trademark of datavisionzero. The MIT License covers the source
code and grants no rights to the project name or logo. Forks and derivative
works are welcome, but please distribute them under a different name so that
users can tell whose software they are running.

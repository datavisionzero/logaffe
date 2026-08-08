# logaffe

A self-hostable, central logging tool for a single operator and their AI agent.
It collects logs from many applications, keeps them separated by project, and
makes them accessible through a web UI and through MCP — safe enough to expose
directly to the public internet.

See [VISION.md](VISION.md) for what logaffe is and, just as importantly, what it
deliberately is not.

## Status

Pre-release, and the product is built. An installation ingests CLEF over HTTP,
stores and queries it, and is claimed by the one operator it belongs to. Behind
their sign-in are the projects and their ingest tokens, the log view with its
filters and its live tail, and the settings for the installation and for each
project. The same reads are offered to an agent as four MCP tools. `logaffe
backup` writes both halves of an installation into one artifact and `logaffe
restore` puts them back; `logaffe recover` is the way back in when the sign-in
is lost. All of it has been exercised end to end against a running installation.

**What is not there yet is a release.** No version has been tagged, so neither
the image nor the three client packages have been published, and today logaffe
is run by building this repository. Pushing a tag is what changes that.

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

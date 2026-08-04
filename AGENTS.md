# logaffe

## Language

The user writes and speaks **German** — reply to them in German.

Everything that lands **in the repository is written in English**: source code, identifiers, comments, docs, `CONTEXT.md`, ADRs, commit messages, PR titles and bodies, and GitHub issues. No German in committed artifacts.

## Git workflow

- **Host**: GitHub — `datavisionzero/logaffe`. The `gh` CLI is installed and authenticated.
- **Pushing to `main` is allowed.** Committing and pushing straight to `main` is the normal path for this repo; no PR required.
- **Feature branches are optional** — use one when the work is large, risky, or wants review, otherwise work on `main`.
- Commit and push only when the user asks for it.

## Agent skills

### Issue tracker

Issues live as GitHub issues in `datavisionzero/logaffe`, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, using the default label strings. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

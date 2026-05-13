# Contributing

Thanks for your interest in contributing to the Travel platform showcase!

## Getting started

1. Open the repo in VS Code with Dev Containers extension installed.
2. Click "Reopen in Container" — this builds the full dev environment.
3. Run `dotnet run --project apps/Travel.AppHost` to start the full stack.
4. Run `npx nx serve web` in another terminal for the Angular frontend.

See [README.md](README.md) for full setup details.

## Workflow

- Branch from `dev`. Use Conventional Commits format for messages (enforced by commitlint).
- Use `npm run commit` for an interactive commit prompt (commitizen).
- Open PR to `dev`. CI must pass (lint + build + tests + arch tests).
- For breaking architectural changes — first discuss in an issue, then write/update an ADR in `docs/adr/`.

## Code style

- .NET: CSharpier formatting (auto-applied via Lefthook pre-commit hook).
- TypeScript: Biome formatting (same).
- Tests follow seven-layer strategy (see [docs/adr/0006-testing-strategy.md](docs/adr/0006-testing-strategy.md)).

## AI-augmented development

This repo uses Claude Code with custom agents and slash commands in `.claude/`. See [CLAUDE.md](CLAUDE.md) for the harness overview.

## Reporting issues

Use the issue templates in `.github/ISSUE_TEMPLATE/`. For security concerns, see [SECURITY.md](SECURITY.md).

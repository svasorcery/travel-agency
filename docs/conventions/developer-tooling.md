# Convention: Developer Tooling

## Purpose

The root [lefthook.yml](../../lefthook.yml) defines local Git hooks. CI remains the authoritative merge gate; hooks run only when Lefthook has been installed in that checkout. The root package scripts provide Commitizen and the repository validators.

## Current hooks

| Hook | Current command | Trigger |
|---|---|---|
| pre-commit C# | dotnet csharpier format {staged_files}, then stage_fixed | staged *.cs |
| pre-commit TypeScript/JavaScript/JSON | npx biome format --write {staged_files} && npx biome check --write {staged_files}, then stage_fixed | staged *.ts, *.tsx, *.js, *.jsx or *.json, excluding package-lock.json |
| commit-msg | npx commitlint --edit {1} | commit message |
| pre-push | dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture --no-build | changed modules/**/*.cs |

The pre-push command assumes the architecture test project has already been built. Its file glob does not cover edits outside modules. CI runs the architecture lane independently for the full repository, and the [project inventory validator](../../tools/ci/validate-dotnet-inventory.mjs) checks the remaining .NET lanes.

On Windows PowerShell, use npm.cmd and npx.cmd when execution policy blocks the .ps1 shims. For example:

    npm.cmd run check:ai-harness
    npm.cmd run check:dotnet-inventory
    npm.cmd run check:readme-examples

The regular dependency install runs the package prepare script that installs Lefthook. A local install with --ignore-scripts deliberately skips hooks and package install scripts; it does not establish that those hooks ran.

[Commitizen](../../package.json) offers npm run commit as an optional message wizard. Conventional Commits and formatting tools are local conveniences; their presence does not grant Git publication authority.

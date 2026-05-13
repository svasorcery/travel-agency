# 0004. NX 22 Monorepo Tooling

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform is polyglot: backend code is .NET 10 (C#), frontend is Angular 21 (TypeScript), infrastructure scripts are YAML and shell, and documentation tooling is Node-based. Without a unified build orchestration layer these separate ecosystems produce duplicated CI logic, no shared dependency graph, no incremental build caching, and no consistent task interface (`build`, `test`, `lint`, `serve`) across technologies.

The two most prominent community plugins for managing .NET inside NX historically were `@nx-dotnet/core` (community-maintained) and the newer official `@nx/dotnet` (Nx organization). As of NX 22, the official `@nx/dotnet` plugin reached a GA-quality state and the community `@nx-dotnet/core` was officially deprecated. This consolidation removes ambiguity about which plugin to adopt for new projects.

## Decision

The monorepo uses **NX 22** as its single build orchestration layer with the **official `@nx/dotnet` plugin** for .NET project integration. All task targets (`build`, `test`, `lint`, `serve`) for both TypeScript and .NET projects are defined in `project.json` files and invoked uniformly via `nx run <project>:<target>`. Remote build caching is provided by NX Cloud Hobby tier (see `docs/conventions/nx-cloud-free-tier.md`). The workspace layout follows a domain-oriented structure (`apps/`, `libs/`, `modules/`, `tests/`) with NX tags enforcing dependency boundaries between domains.

## Alternatives Considered

### Option A: Separate Frontend and Backend Repositories

Frontend lives in a dedicated Angular workspace; .NET solution lives in a separate repository. Each has its own CI pipeline, tooling, and versioning.

Rejected because: this eliminates the possibility of unified end-to-end tasks (e.g., "test everything before merge"), makes cross-cutting tooling changes (Renovate config, Lefthook hooks, commitlint rules) require synchronization across two repositories, and prevents NX's dependency graph from understanding the OpenAPI client generation dependency between backend and frontend builds.

### Option B: Cake + npm Scripts

.NET build orchestration via Cake (C# build DSL); frontend tasks via npm scripts in `package.json`. Two separate tooling surfaces unified only by a top-level shell script.

Rejected because: there is no shared dependency graph, no incremental build caching, and no remote cache. CI time scales linearly with the number of projects even when most are unaffected by a change. Cake adds a C#-syntax DSL that contributors must learn in addition to the project code, increasing onboarding friction without meaningfully improving on `dotnet build` for the use cases needed.

### Option C: Bazel

Hermetic, reproducible builds with fine-grained caching. Used by large multi-language monorepos (Google, Uber).

Rejected because: Bazel's learning curve is steep, its .NET support requires third-party rules, and its build file verbosity is disproportionate for a showcase repository. The marginal caching benefit over NX Cloud does not justify the contributor onboarding cost.

## Consequences

### Positive
- `nx affected --target=test` runs tests only for projects affected by a PR diff, dramatically reducing CI time as the repository grows.
- All tasks — regardless of technology — share the same invocation interface, making the repository approachable for contributors who know only one of the two ecosystems.
- NX Cloud remote cache means repeated CI runs (e.g., after a documentation-only change) complete in seconds rather than minutes.

### Negative / Trade-offs
- `@nx/dotnet` is the official plugin but is newer than the deprecated community alternative. Early adopter issues are possible; the plugin's feature surface for .NET is narrower than NX's TypeScript-native capabilities. Some .NET-specific workflows (e.g., `dotnet publish` with runtime identifiers) may require custom executor wrappers.
- NX versioning and plugin compatibility must be managed carefully: NX major versions may break plugin APIs, requiring coordinated updates.

### Neutral
- NX Cloud Hobby tier has usage limits (50 000 credits/month, 5 contributors). A solo OSS project is comfortably within those limits; if the project grows, migration to a self-hosted S3-backed remote cache is documented in `docs/conventions/nx-cloud-free-tier.md`.

## Out of Scope

- NX Cloud plan selection and pricing — covered in `docs/conventions/nx-cloud-free-tier.md`.
- Specific NX target configurations for individual projects — those are implementation details in `project.json` files.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 5.3
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0004), § 5.3
- NX official documentation: https://nx.dev
- `@nx/dotnet` plugin: https://nx.dev/nx-api/dotnet

# Foundation (Subproject 0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable scaffold for the Travel platform — empty domain modules, full Aspire infrastructure stack, CI pipeline, devcontainer, AI-harness, and one end-to-end vertical slice (`GET /api/status`) proving every layer wires together.

**Architecture:** Modular monolith (`Travel.Host`) + extracted AI service (`Travel.AI`), oriented around a domain-first NX monorepo. Aspire orchestrates PostgreSQL+pgvector, Redis, NATS JetStream, Keycloak, and Mailpit. Frontend is Angular 21 with NgRx SignalStore + Tailwind v4 + PrimeNG unstyled.

**Tech Stack:** .NET 10, Aspire, Wolverine, Marten, EF Core 10, PostgreSQL 17 (+ pgvector), Redis, NATS JetStream, Keycloak; Angular 21 + Signals + httpResource + Tailwind v4 + PrimeNG; NX 22 + `@nx/dotnet`; xUnit v3 + Verify + Testcontainers + ArchUnitNET; Playwright E2E.

**Spec reference:** `docs/superpowers/specs/2026-05-04-foundation-design.md` — for full agent prompts, ADR content, and detailed structure tables. Plan tasks reference spec sections by number to avoid duplication of large content blocks.

---

## File Structure (high level)

```
travel-agency/
├── .claude/{settings.json, agents/*.md, commands/*.md}
├── .devcontainer/devcontainer.json
├── .github/{workflows/{ci.yml,deploy.yml}, ISSUE_TEMPLATE/*, PULL_REQUEST_TEMPLATE.md, FUNDING.yml}
├── apps/{Travel.Host, Travel.AI, Travel.AppHost, Travel.ServiceDefaults, web}/
├── modules/{flights,hotels,rail,trips,identity}/
├── shared/{dotnet/{Abstractions,Domain,Infrastructure,Web,TestInfrastructure},ts/{ui-kit,api-client}}/
├── infra/{docker,keycloak}/
├── docs/{adr,conventions,superpowers/{specs,plans},ai-conversations,blog-template.md}
├── prompts/v1/
├── tests/{<module>,Architecture,Contract,AiEvals,Travel.Host.Tests.Integration,travel-e2e}/
├── CLAUDE.md, README.md, LICENSE, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md
├── nx.json, package.json, biome.json, .editorconfig, Travel.sln
├── lefthook.yml, commitlint.config.mjs, renovate.json
└── global.json, Directory.Build.props, Directory.Packages.props
```

---

# Phase 1 — Repository Wipe & Base Configuration

## Task 1: Wipe existing source, preserve git history

> **AUTHORIZED DESTRUCTIVE WIPE.** Пользователь явно разрешил удалить весь legacy-код (включая untracked файлы под `src/Modules/Rail/...`). Не останавливаться на confirmation. Если агент-исполнитель имеет встроенные safe-guards против `rm -rf` — этот таск пре-авторизован.

**Files:**
- Delete: `src/`, `Viajante.sln`, all build artifacts (`obj/`, `bin/`)
- Keep: `.git/`, `docs/`, `README.md`

- [ ] **Step 1: List what's currently tracked**

```bash
git ls-files | head -50
```

- [ ] **Step 2: Remove all source code, keep docs and git**

```bash
rm -rf src/
rm -f Viajante.sln
rm -rf "**/obj" "**/bin"
git add -A
```

- [ ] **Step 3: Verify only docs and root metadata remain tracked**

```bash
git status
```
Expected: `src/` and `Viajante.sln` deleted; `docs/` intact.

- [ ] **Step 4: Commit the wipe**

```bash
git commit -m "chore: wipe legacy code before Foundation rewrite"
```

---

## Task 2: Create root configuration files

**Files:**
- Create: `.gitignore`, `.editorconfig`, `.gitattributes`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`

- [ ] **Step 1: Write `.gitignore`**

Use the standard .NET + Node.js + JetBrains/VS gitignore. Include:
- `bin/`, `obj/`, `out/`
- `node_modules/`, `.nx/cache/`, `.angular/`, `dist/`
- `.env`, `.env.*` (except `.env.example`)
- `.vs/`, `.idea/`, `.vscode/` (except `.vscode/extensions.json`)
- `TestResults/`, `coverage/`
- `*.user`, `*.suo`

- [ ] **Step 2: Write `.editorconfig`**

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{json,yml,yaml,md,ts,tsx,js,jsx,html,css,scss}]
indent_size = 2

[*.cs]
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_indent_case_contents = true
dotnet_sort_system_directives_first = true
```

- [ ] **Step 3: Write `.gitattributes`**

```
* text=auto eol=lf
*.cs       diff=csharp
*.png      binary
*.jpg      binary
*.ico      binary
```

- [ ] **Step 4: Write `global.json`**

```json
{
  "sdk": {
    "version": "10.0.203",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 5: Write `Directory.Build.props`** (root, applies to all .NET projects)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Skeleton-phase exemptions: marker classes / empty modules trigger these en masse.
         Tighten this list as modules get real content. -->
    <WarningsNotAsErrors>CA1822;CS1591;CA1812;CA1052;IDE0058</WarningsNotAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
    <AssemblyName>$(MSBuildProjectName)</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Roslynator.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Write `Directory.Packages.props`** (central package version management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Roslynator.Analyzers" Version="4.15.0" />

    <!-- Aspire 13.x (new SDK-style AppHost) -->
    <PackageVersion Include="Aspire.Hosting.AppHost" Version="13.2.4" />
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.2.4" />
    <PackageVersion Include="Aspire.Hosting.Redis" Version="13.2.4" />
    <PackageVersion Include="Aspire.Hosting.Nats" Version="13.2.1" />
    <!-- Keycloak hosting integration is still preview as of 2026-05; accepted risk -->
    <PackageVersion Include="Aspire.Hosting.Keycloak" Version="13.2.4-preview.1.26224.4" />

    <!-- Critter Stack (Wolverine + Marten) — MIT-only alternative to commercialized MediatR/MassTransit -->
    <PackageVersion Include="WolverineFx" Version="5.13.0" />
    <PackageVersion Include="WolverineFx.Http" Version="5.13.0" />
    <PackageVersion Include="WolverineFx.Marten" Version="5.13.0" />
    <PackageVersion Include="WolverineFx.Postgres" Version="5.13.0" />
    <PackageVersion Include="Marten" Version="8.28.0" />

    <!-- EF Core 10 -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.4" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.4" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.1" />
    <!-- Aspire client integration: provides builder.AddNpgsqlDbContext<T>("name") that resolves
         connection string + OTel + health checks from the Aspire-injected configuration -->
    <PackageVersion Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="13.2.4" />
    <!-- Maintained by Npgsql team; provides UseSnakeCaseNamingConvention() — no custom reflection -->
    <PackageVersion Include="EFCore.NamingConventions" Version="10.0.0" />

    <!-- Result-pattern -->
    <PackageVersion Include="ErrorOr" Version="2.0.1" />

    <!-- AI (used from Subproject 1+; pinned in Foundation for centralized version mgmt) -->
    <PackageVersion Include="Anthropic" Version="12.20.0" />
    <PackageVersion Include="Microsoft.Extensions.AI" Version="10.5.2" />
    <PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.5.0" />

    <!-- Tests -->
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.v3.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.5.1" />
    <!-- Verify.XunitV3 is the xUnit v3 adapter; do NOT use Verify.Xunit which targets v2 -->
    <PackageVersion Include="Verify.XunitV3" Version="31.12.5" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.11.0" />
    <PackageVersion Include="TngTech.ArchUnitNET.xUnitV3" Version="0.13.1" />
    <!-- Shouldly instead of FluentAssertions (FA 8.0+ went commercial Jan 2025) -->
    <PackageVersion Include="Shouldly" Version="4.3.0" />
    <!-- Alba — official JasperFx HTTP integration-testing companion for Wolverine.Http -->
    <PackageVersion Include="Alba" Version="8.4.0" />
    <!-- Aspire testing host — spins up the AppHost (with all containers) inside a test -->
    <PackageVersion Include="Aspire.Hosting.Testing" Version="13.2.4" />
    <!-- TimeProvider fakes for time-sensitive tests (Microsoft official) -->
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.0" />
  </ItemGroup>
</Project>
```

> NOTE on versions: verified against NuGet on 2026-05-10. If a package version is unavailable, replace with the latest stable in the same major track and document the substitution in the commit message. The `TngTech.ArchUnitNET.xUnitV3` package version (0.13.1 estimated) should be confirmed on NuGet directly.

- [ ] **Step 7: Commit**

```bash
git add .gitignore .editorconfig .gitattributes global.json Directory.Build.props Directory.Packages.props
git commit -m "chore: add root .NET + tooling configuration"
```

---

## Task 3: Initialize NX workspace

**Files:**
- Create: `nx.json`, `package.json`, `biome.json`, `tsconfig.base.json`

- [ ] **Step 1: Initialize npm and install NX 22**

```bash
npm init -y
npm install -D nx@~22 @nx/workspace @nx/dotnet @nx/angular @nx/playwright @nx/vite typescript@~5.6 @biomejs/biome
```

- [ ] **Step 2: Write `nx.json`**

```jsonc
{
  "$schema": "./node_modules/nx/schemas/nx-schema.json",
  "namedInputs": {
    "default":      ["{projectRoot}/**/*", "sharedGlobals"],
    "production":   ["default", "!{projectRoot}/**/*.spec.ts", "!{projectRoot}/**/?(*.)+(spec|test).ts"],
    "sharedGlobals": ["{workspaceRoot}/global.json", "{workspaceRoot}/Directory.Build.props", "{workspaceRoot}/Directory.Packages.props"]
  },
  "targetDefaults": {
    "build":   { "cache": true, "dependsOn": ["^build"], "inputs": ["production", "^production"] },
    "test":    { "cache": true, "dependsOn": ["build"],  "inputs": ["default", "^production"] },
    "lint":    { "cache": true, "inputs": ["default"] },
    "e2e":     { "cache": false }
  },
  "defaultBase": "master",
  "parallel": 3
}
```

- [ ] **Step 3: Write `package.json` scripts**

Add to `package.json`:

```json
{
  "name": "travel-agency",
  "private": true,
  "scripts": {
    "build":  "nx affected -t build",
    "test":   "nx affected -t test",
    "lint":   "nx affected -t lint",
    "format": "biome format --write .",
    "aspire": "dotnet run --project apps/Travel.AppHost"
  }
}
```

- [ ] **Step 4: Write `biome.json`**

```jsonc
{
  "$schema": "./node_modules/@biomejs/biome/configuration_schema.json",
  "files":     { "ignore": ["node_modules", "dist", ".nx", ".angular", "**/bin", "**/obj"] },
  "formatter": { "enabled": true, "indentStyle": "space", "indentWidth": 2, "lineWidth": 120 },
  "linter":    { "enabled": true, "rules": { "recommended": true } },
  "javascript":{ "formatter": { "quoteStyle": "single", "trailingCommas": "all" } }
}
```

- [ ] **Step 5: Write `tsconfig.base.json`**

```jsonc
{
  "compilerOptions": {
    "target":      "ES2022",
    "module":      "ESNext",
    "moduleResolution": "bundler",
    "strict":      true,
    "noImplicitOverride":             true,
    "noPropertyAccessFromIndexSignature": true,
    "noImplicitReturns":              true,
    "noFallthroughCasesInSwitch":     true,
    "skipLibCheck": true,
    "esModuleInterop": true,
    "experimentalDecorators": true,
    "emitDecoratorMetadata":  true,
    "baseUrl": ".",
    "paths": {}
  }
}
```

- [ ] **Step 6: Verify NX is installed and runs**

```bash
npx nx --version
```
Expected: prints version 22.x.

- [ ] **Step 7: Commit**

```bash
git add nx.json package.json package-lock.json biome.json tsconfig.base.json
git commit -m "chore: initialize NX 22 workspace + Biome"
```

---

## Task 4: Create empty `Travel.sln` and basic folder structure

- [ ] **Step 1: Create the solution file**

```bash
dotnet new sln --name Travel
```

- [ ] **Step 2: Create root folder skeleton**

```bash
mkdir -p apps modules/flights modules/hotels modules/rail modules/trips modules/identity \
         shared/dotnet shared/ts \
         infra/docker infra/keycloak \
         prompts/v1 \
         tests \
         docs/adr docs/ai-conversations \
         .claude/agents .claude/commands \
         .github/workflows \
         .devcontainer
```

- [ ] **Step 3: Verify**

```bash
ls -la
```
Expected: all folders above exist; `Travel.sln` exists.

- [ ] **Step 4: Commit**

```bash
git add Travel.sln
# Folders without files are not tracked yet — that's fine.
git commit -m "chore: add empty Travel.sln + root folder skeleton"
```

---

# Phase 2 — Shared .NET Infrastructure

## Task 5: Create `Travel.Shared.Abstractions` (marker interfaces)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Abstractions/Travel.Shared.Abstractions.csproj`
- Create: `shared/dotnet/Travel.Shared.Abstractions/IDomainEvent.cs`
- Create: `shared/dotnet/Travel.Shared.Abstractions/IModuleAssemblyMarker.cs`

> **Decision:** `ErrorOr` is **not** re-exported via `global using` here. Result-pattern with HTTP-flavored error semantics belongs in Application/Web layers, not in shared abstractions. Each consumer adds `using ErrorOr;` locally where actually needed. Documented in ADR 0008.

- [ ] **Step 1: Create the `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.Abstractions --framework net10.0 --no-restore
```

Edit the generated `.csproj` — no package references; Abstractions stays dependency-free:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

Delete the auto-generated `Class1.cs`.

- [ ] **Step 2: Write `IDomainEvent.cs`**

```csharp
namespace Travel.Shared.Abstractions;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
```

- [ ] **Step 3: Write `IModuleAssemblyMarker.cs`**

```csharp
namespace Travel.Shared.Abstractions;

/// Marker interface used by module assemblies to expose themselves to scanning
/// (Wolverine handler discovery, ArchUnit boundary tests).
public interface IModuleAssemblyMarker { }
```

- [ ] **Step 4: Add to solution**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Abstractions/Travel.Shared.Abstractions.csproj
```

- [ ] **Step 5: Verify build**

```bash
dotnet build shared/dotnet/Travel.Shared.Abstractions
```
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add shared/dotnet/Travel.Shared.Abstractions Travel.sln
git commit -m "feat(shared): add Travel.Shared.Abstractions (IDomainEvent, IModuleAssemblyMarker)"
```

---

## Task 6: Create `Travel.Shared.Domain` (base aggregate / entity types)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Domain/Travel.Shared.Domain.csproj`
- Create: `shared/dotnet/Travel.Shared.Domain/AggregateRoot.cs`
- Create: `shared/dotnet/Travel.Shared.Domain/Entity.cs`

> **No `ValueObject` base type.** C# `record` types already provide structural equality — wrapping them in an empty abstract base adds inheritance noise without behavior. Convention is documented in `shared/CLAUDE.md` (Task 46) and enforceable in architecture tests later if needed. Aligns with modern (C# 13+) DDD idiom.

- [ ] **Step 1: Create `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.Domain --framework net10.0 --no-restore
rm shared/dotnet/Travel.Shared.Domain/Class1.cs
```

Edit `Travel.Shared.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write `AggregateRoot.cs`**

```csharp
using Travel.Shared.Abstractions;

namespace Travel.Shared.Domain;

public abstract class AggregateRoot<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _events = new();

    public TId Id { get; protected set; } = default!;

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    protected void Raise(IDomainEvent @event) => _events.Add(@event);
    public    void ClearEvents()              => _events.Clear();
}
```

- [ ] **Step 3: Write `Entity.cs`** — identity-based equality (DDD semantics; `class`, not `record`, because record's value-equality is wrong for entities)

```csharp
namespace Travel.Shared.Domain;

public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        // transient entities (Id == default) are never equal — they're not yet "the same thing"
        if (EqualityComparer<TId>.Default.Equals(Id, default!) ||
            EqualityComparer<TId>.Default.Equals(other.Id, default!)) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    public static bool operator ==(Entity<TId>? a, Entity<TId>? b) => Equals(a, b);
    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !Equals(a, b);
}
```

- [ ] **Step 4: Add to solution and build**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Domain/Travel.Shared.Domain.csproj
dotnet build shared/dotnet/Travel.Shared.Domain
```
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add shared/dotnet/Travel.Shared.Domain Travel.sln
git commit -m "feat(shared): add Travel.Shared.Domain (AggregateRoot, Entity with identity equality)"
```

---

## Task 7: Create `Travel.Shared.Infrastructure` (initialization pattern only — no web deps)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Travel.Shared.Infrastructure.csproj`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/IInitializer.cs`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/AppInitializer.cs`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/InitializationExtensions.cs`

> Rationale: REPR layer is implemented via WolverineFx.Http (attributes + source generation on endpoint methods in `apps/Travel.Host`). A custom `IEndpoint` interface is not needed. This project hosts **only** the module initialization pattern (`IInitializer`, borrowed from Pulsell). **No ASP.NET dependencies here** — otherwise all Domain/Application modules transitively pull in AspNetCore.App, breaking DependencyDirectionTests. Web helpers (ErrorOr → ProblemDetails) live in a separate `Travel.Shared.Web` project (Task 7b). Snake_case naming uses the maintained `EFCore.NamingConventions` package — no hand-rolled reflection. Decision recorded in ADR 0009.

- [ ] **Step 1: Create `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.Infrastructure --framework net10.0 --no-restore
rm shared/dotnet/Travel.Shared.Infrastructure/Class1.cs
```

Edit `Travel.Shared.Infrastructure.csproj` — minimal dependencies; no AspNetCore framework reference, no EF Core:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <ProjectReference Include="..\Travel.Shared.Domain\Travel.Shared.Domain.csproj" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

> Add the three `Microsoft.Extensions.*.Abstractions` packages to `Directory.Packages.props` if not already present (centrally-managed versions, pinned to the .NET 10 servicing track).

- [ ] **Step 2: Write `Initialization/IInitializer.cs`** — module bootstrap hook

```csharp
namespace Travel.Shared.Infrastructure.Initialization;

/// Implemented by per-module bootstrap logic (Marten schema apply, EF migrate,
/// realm import, projection warm-up). Discovered and executed once at host
/// startup by AppInitializer hosted service. Order is not guaranteed —
/// initializers must be independent.
public interface IInitializer
{
    Task InitializeAsync(CancellationToken ct);
}
```

- [ ] **Step 3: Write `Initialization/AppInitializer.cs`** — hosted service that runs all initializers once at startup

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Travel.Shared.Infrastructure.Initialization;

/// Sequential by design: initializers may have implicit ordering through DI scope
/// (e.g. Marten schema apply before any module that reads from it). Independence
/// is a guideline, not a guarantee — modules should not rely on cross-initializer
/// state but the runtime does not enforce parallelism.
internal sealed class AppInitializer(
    IServiceProvider services,
    ILogger<AppInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var initializers = scope.ServiceProvider.GetServices<IInitializer>().ToArray();

        logger.LogInformation("Running {Count} initializer(s)", initializers.Length);

        foreach (var initializer in initializers)
        {
            var name = initializer.GetType().Name;
            logger.LogInformation("Initializing: {Name}", name);
            await initializer.InitializeAsync(ct);
            logger.LogInformation("Done: {Name}", name);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 4: Write `Initialization/InitializationExtensions.cs`** — registration helpers

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Travel.Shared.Infrastructure.Initialization;

public static class InitializationExtensions
{
    /// Adds the AppInitializer hosted service. Call once in Program.cs.
    public static IServiceCollection AddAppInitialization(this IServiceCollection services)
    {
        services.AddHostedService<AppInitializer>();
        return services;
    }

    /// Registers an IInitializer implementation. Each module calls this in its
    /// module-extension method (e.g. AddFlightsModule registers FlightsInitializer).
    public static IServiceCollection AddInitializer<T>(this IServiceCollection services)
        where T : class, IInitializer
    {
        services.AddScoped<IInitializer, T>();
        return services;
    }
}
```

> Snake_case naming for EF Core is **not** implemented here — see Task 34, which calls `optionsBuilder.UseSnakeCaseNamingConvention()` from the `EFCore.NamingConventions` NuGet package. No hand-rolled metadata-walker.

- [ ] **Step 5: Add to solution and build**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Infrastructure/Travel.Shared.Infrastructure.csproj
dotnet build shared/dotnet/Travel.Shared.Infrastructure
```
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add shared/dotnet/Travel.Shared.Infrastructure Travel.sln
git commit -m "feat(shared): add Travel.Shared.Infrastructure (IInitializer pattern)"
```

---

## Task 7b: Create `Travel.Shared.Web` (web-layer helpers)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Web/Travel.Shared.Web.csproj`
- Create: `shared/dotnet/Travel.Shared.Web/ErrorOrExtensions.cs`

> Rationale: extracted from Task 7 to keep AspNetCore dependencies out of Domain/Application transitive closure. Only consumers that actually render HTTP responses reference this project (`apps/Travel.Host`, future `apps/Travel.AI`). Module Domain/Application layers do **not** reference it.

- [ ] **Step 1: Create `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.Web --framework net10.0 --no-restore
rm shared/dotnet/Travel.Shared.Web/Class1.cs
```

Edit `Travel.Shared.Web.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <PackageReference Include="ErrorOr" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write `ErrorOrExtensions.cs`** — bridge between ErrorOr and ASP.NET ProblemDetails

```csharp
using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Shared.Web;

public static class ErrorOrExtensions
{
    public static ProblemDetails ToProblemDetails(this List<Error> errors)
    {
        var first = errors[0];
        return new ProblemDetails
        {
            Type   = $"https://travel.local/errors/{first.Code}",
            Title  = first.Type.ToString(),
            Status = first.Type switch
            {
                ErrorType.Validation   => 400,
                ErrorType.NotFound     => 404,
                ErrorType.Conflict     => 409,
                ErrorType.Unauthorized => 401,
                ErrorType.Forbidden    => 403,
                _                      => 500,
            },
            Detail     = first.Description,
            Extensions = { ["errors"] = errors.Select(e => new { e.Code, e.Description, Type = e.Type.ToString() }) },
        };
    }
}
```

- [ ] **Step 3: Add to solution and build**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Web/Travel.Shared.Web.csproj
dotnet build shared/dotnet/Travel.Shared.Web
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add shared/dotnet/Travel.Shared.Web Travel.sln
git commit -m "feat(shared): add Travel.Shared.Web (ErrorOr → ProblemDetails bridge)"
```

> **Architecture tests note (Task 26):** `DependencyDirectionTests` should additionally enforce: classes in `Travel.Modules.*.Core` / `Travel.Modules.*.Application` namespaces must NOT depend on `Travel.Shared.Web`. Only `Travel.Modules.*.Api` may reference it (and `apps/Travel.Host` directly).

---

## Task 8: Create `Travel.Shared.TestInfrastructure` (Testcontainers fixtures)

**Files:**
- Create: `shared/dotnet/Travel.Shared.TestInfrastructure/Travel.Shared.TestInfrastructure.csproj`
- Create: `shared/dotnet/Travel.Shared.TestInfrastructure/IntegrationTestBase.cs`

- [ ] **Step 1: Create `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.TestInfrastructure --framework net10.0 --no-restore
rm shared/dotnet/Travel.Shared.TestInfrastructure/Class1.cs
```

Edit `Travel.Shared.TestInfrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write `IntegrationTestBase.cs`**

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace Travel.Shared.TestInfrastructure;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .WithDatabase("travel_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await Postgres.StartAsync();
        await OnInitializedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await OnDisposingAsync();
        await Postgres.DisposeAsync();
    }

    protected virtual ValueTask OnInitializedAsync() => ValueTask.CompletedTask;
    protected virtual ValueTask OnDisposingAsync()    => ValueTask.CompletedTask;

    protected string ConnectionString => Postgres.GetConnectionString();
}
```

- [ ] **Step 3: Add to solution and build**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.TestInfrastructure/Travel.Shared.TestInfrastructure.csproj
dotnet build shared/dotnet/Travel.Shared.TestInfrastructure
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add shared/dotnet/Travel.Shared.TestInfrastructure Travel.sln
git commit -m "feat(shared): add Travel.Shared.TestInfrastructure (Testcontainers IntegrationTestBase)"
```

---

# Phase 3 — Aspire Host Scaffold

## Task 9: Create `Travel.ServiceDefaults`

**Files:**
- Create: `apps/Travel.ServiceDefaults/Travel.ServiceDefaults.csproj`
- Create: `apps/Travel.ServiceDefaults/Extensions.cs`

- [ ] **Step 1: Generate from Aspire template**

```bash
dotnet new aspire-servicedefaults -o apps/Travel.ServiceDefaults
```

- [ ] **Step 2: Rename project to match folder**

If the generated `.csproj` has a different name, rename it:

```bash
mv apps/Travel.ServiceDefaults/*.csproj apps/Travel.ServiceDefaults/Travel.ServiceDefaults.csproj
```

Update internal namespace references in the generated `Extensions.cs` to use `Travel.ServiceDefaults` namespace.

- [ ] **Step 3: Add to solution and build**

```bash
dotnet sln Travel.sln add apps/Travel.ServiceDefaults/Travel.ServiceDefaults.csproj
dotnet build apps/Travel.ServiceDefaults
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add apps/Travel.ServiceDefaults Travel.sln
git commit -m "feat(aspire): add Travel.ServiceDefaults (OTel, health checks, service discovery)"
```

---

## Task 10: Create infra/ artifacts (Keycloak realm + production docker-compose)

**Files:**
- Create: `infra/keycloak/travel-realm.json`
- Create: `infra/docker/docker-compose.production.yml`

- [ ] **Step 1: Write `infra/keycloak/travel-realm.json`**

```json
{
  "realm": "travel",
  "enabled": true,
  "registrationAllowed": false,
  "loginWithEmailAllowed": true,
  "duplicateEmailsAllowed": false,
  "resetPasswordAllowed": true,
  "users": [
    {
      "username": "dev",
      "email": "dev@travel.local",
      "emailVerified": true,
      "enabled": true,
      "credentials": [{ "type": "password", "value": "dev123", "temporary": false }],
      "realmRoles": ["user"]
    }
  ],
  "roles": {
    "realm": [
      { "name": "user",  "description": "Default user role" },
      { "name": "admin", "description": "Administrator role" }
    ]
  },
  "clients": [
    {
      "clientId": "travel-web",
      "publicClient": true,
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "redirectUris": ["http://localhost:4200/*"],
      "webOrigins": ["http://localhost:4200"],
      "attributes": { "pkce.code.challenge.method": "S256" }
    }
  ]
}
```

- [ ] **Step 2: Write `infra/docker/docker-compose.production.yml`** (overlay to `aspire publish` output)

```yaml
# Overlay applied on top of the docker-compose.yaml emitted by `aspire publish`.
# Production-specific tweaks: explicit restart policy, log rotation, no host-port
# exposure for internal services (only Caddy fronts traffic), persistent volumes
# pinned to host paths.
services:
  postgres:
    restart: unless-stopped
    volumes:
      - /srv/travel/postgres:/var/lib/postgresql/data
    logging:
      driver: json-file
      options: { max-size: "10m", max-file: "5" }

  redis:
    restart: unless-stopped
    volumes:
      - /srv/travel/redis:/data

  nats:
    restart: unless-stopped
    volumes:
      - /srv/travel/nats:/data

  keycloak:
    restart: unless-stopped
    environment:
      KC_HOSTNAME: ${KC_HOSTNAME}
      KC_PROXY_HEADERS: xforwarded
      KC_HTTP_ENABLED: "true"
    volumes:
      - /srv/travel/keycloak:/opt/keycloak/data

  host:
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      OTEL_EXPORTER_OTLP_ENDPOINT: ${OTEL_ENDPOINT}

  ai:
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      OTEL_EXPORTER_OTLP_ENDPOINT: ${OTEL_ENDPOINT}
```

- [ ] **Step 3: Commit**

```bash
git add infra/keycloak/travel-realm.json infra/docker/docker-compose.production.yml
git commit -m "feat(infra): add Keycloak realm + production docker-compose overlay"
```

---

## Task 11: Create `Travel.AppHost` with full infrastructure stack

**Files:**
- Create: `apps/Travel.AppHost/Travel.AppHost.csproj`
- Create: `apps/Travel.AppHost/Program.cs`
- Create: `apps/Travel.AppHost/appsettings.json`
- Create: `apps/Travel.AppHost/Properties/launchSettings.json`

- [ ] **Step 1: Generate from template**

```bash
dotnet new aspire-apphost -o apps/Travel.AppHost
mv apps/Travel.AppHost/*.csproj apps/Travel.AppHost/Travel.AppHost.csproj
```

- [ ] **Step 2: Edit `Travel.AppHost.csproj`** — uses Aspire 13.x SDK-style (new in Aspire 13)

```xml
<Project Sdk="Aspire.AppHost.Sdk/13.2.4">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <UserSecretsId>travel-apphost</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.Nats" />
    <PackageReference Include="Aspire.Hosting.Keycloak" />
  </ItemGroup>
</Project>
```

> Note: Aspire 13 introduced the new `Sdk="Aspire.AppHost.Sdk/<version>"` declaration — the older `<IsAspireHost>true</IsAspireHost>` property is no longer needed.

- [ ] **Step 3: Write `Program.cs`** — register all infrastructure resources

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume()
    .WithPgAdmin();

var travelDb = postgres.AddDatabase("travel");

// Redis
var redis = builder.AddRedis("redis")
    .WithDataVolume();

// NATS JetStream
var nats = builder.AddNats("nats")
    .WithJetStream()
    .WithDataVolume();

// Keycloak
var keycloak = builder.AddKeycloak("keycloak", port: 8180)
    .WithDataVolume()
    .WithRealmImport("../../infra/keycloak");

// Mailpit (custom container)
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.20")
    .WithEndpoint(port: 8025, targetPort: 8025, name: "ui")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp");

// Application projects (will be added by Tasks 12-13 once those projects exist)
// var host = builder.AddProject<Projects.Travel_Host>("host")
//     .WithReference(travelDb)
//     .WithReference(redis)
//     .WithReference(nats)
//     .WithReference(keycloak)
//     .WithEnvironment("Smtp__Host", mailpit.GetEndpoint("smtp"));
//
// var ai = builder.AddProject<Projects.Travel_AI>("ai")
//     .WithReference(travelDb)
//     .WithReference(redis)
//     .WithReference(nats);

// Optional observability stack (gated by env flag)
if (builder.Configuration.GetValue<bool>("ENABLE_OBSERVABILITY_STACK"))
{
    var loki = builder.AddContainer("loki", "grafana/loki", "3.2.0")
        .WithEndpoint(port: 3100, targetPort: 3100);

    var tempo = builder.AddContainer("tempo", "grafana/tempo", "2.6.0")
        .WithEndpoint(port: 3200, targetPort: 3200);

    builder.AddContainer("grafana", "grafana/grafana", "11.3.0")
        .WithEndpoint(port: 3000, targetPort: 3000);
}

await builder.Build().RunAsync();
```

> NOTE: `Projects.Travel_Host` and `Projects.Travel_AI` references are commented out — they get generated automatically by Aspire once the projects are added as ProjectReferences in the `.csproj`. We'll uncomment in Task 14.

- [ ] **Step 4: Write `appsettings.json`**

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Aspire.Hosting.Dcp": "Warning" } },
  "ENABLE_OBSERVABILITY_STACK": false
}
```

- [ ] **Step 5: Add to solution and build**

```bash
dotnet sln Travel.sln add apps/Travel.AppHost/Travel.AppHost.csproj
dotnet build apps/Travel.AppHost
```
Expected: build succeeds. App will not run yet (no application projects referenced).

- [ ] **Step 6: Commit**

```bash
git add apps/Travel.AppHost Travel.sln
git commit -m "feat(aspire): add Travel.AppHost with full infrastructure stack"
```

---

## Task 12: Create `Travel.Host` skeleton

**Files:**
- Create: `apps/Travel.Host/Travel.Host.csproj`
- Create: `apps/Travel.Host/Program.cs`
- Create: `apps/Travel.Host/appsettings.json`
- Create: `apps/Travel.Host/Properties/launchSettings.json`

- [ ] **Step 1: Create from web template**

```bash
dotnet new web -o apps/Travel.Host --framework net10.0 --no-restore
mv apps/Travel.Host/*.csproj apps/Travel.Host/Travel.Host.csproj
```

- [ ] **Step 2: Edit `Travel.Host.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <UserSecretsId>travel-host</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Travel.ServiceDefaults\Travel.ServiceDefaults.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Domain\Travel.Shared.Domain.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Infrastructure\Travel.Shared.Infrastructure.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Web\Travel.Shared.Web.csproj" />
    <PackageReference Include="WolverineFx" />
    <PackageReference Include="WolverineFx.Http" />
    <PackageReference Include="WolverineFx.Postgres" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="EFCore.NamingConventions" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write `Program.cs`** (skeleton — full vertical slice wiring comes in Phase 9)

```csharp
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine();

builder.Services.AddAppInitialization();  // hosted service that runs IInitializer impls at startup

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints();  // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();
```

> Each module's `Add{Module}Module()` extension will register its own `IInitializer` (e.g., `services.AddInitializer<FlightsInitializer>()`). AppInitializer hosted service finds them all and runs them once at startup. Pattern borrowed from Pulsell project — keeps `Program.cs` lean and gives each module a clean bootstrap hook for Marten schema, EF migrations, seed data, Keycloak realm import, etc.

- [ ] **Step 4: Add to solution and build**

```bash
dotnet sln Travel.sln add apps/Travel.Host/Travel.Host.csproj
dotnet build apps/Travel.Host
```
Expected: build succeeds.

- [ ] **Step 5: Reference Travel.Host from Travel.AppHost** — edit `apps/Travel.AppHost/Travel.AppHost.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Travel.Host\Travel.Host.csproj" />
</ItemGroup>
```

Uncomment the `host` resource block in `apps/Travel.AppHost/Program.cs` (from Task 11 Step 3).

- [ ] **Step 6: Commit**

```bash
git add apps/Travel.Host apps/Travel.AppHost Travel.sln
git commit -m "feat(host): add Travel.Host skeleton with Wolverine + endpoints"
```

---

## Task 13: Create `Travel.AI` skeleton

**Files:**
- Create: `apps/Travel.AI/Travel.AI.csproj`
- Create: `apps/Travel.AI/Program.cs`

- [ ] **Step 1: Create**

```bash
dotnet new web -o apps/Travel.AI --framework net10.0 --no-restore
mv apps/Travel.AI/*.csproj apps/Travel.AI/Travel.AI.csproj
```

- [ ] **Step 2: Edit `Travel.AI.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <UserSecretsId>travel-ai</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Travel.ServiceDefaults\Travel.ServiceDefaults.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <PackageReference Include="WolverineFx" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write `Program.cs`** (skeleton)

```csharp
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Host.UseWolverine();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "Travel.AI service running");

await app.RunAsync();
```

- [ ] **Step 4: Add to solution and reference from AppHost** — same pattern as Task 12 Step 5.

- [ ] **Step 5: Build full solution**

```bash
dotnet build Travel.sln
```
Expected: every project builds.

- [ ] **Step 6: Commit**

```bash
git add apps/Travel.AI apps/Travel.AppHost Travel.sln
git commit -m "feat(ai): add Travel.AI skeleton"
```

---

## Task 14: Verify `aspire run` brings up the full stack

- [ ] **Step 1: Run AppHost**

```bash
dotnet run --project apps/Travel.AppHost
```

- [ ] **Step 2: Open Aspire dashboard** at the URL printed (typically `https://localhost:17002`).

Expected resources in the dashboard:
- `postgres` — Running
- `redis` — Running
- `nats` — Running
- `keycloak` — Running
- `mailpit` — Running
- `host` — Running
- `ai` — Running

- [ ] **Step 3: Verify endpoints**
  - Aspire dashboard accessible
  - Keycloak admin: `http://localhost:8180` → realm `travel` exists with user `dev@travel.local`
  - Mailpit UI: `http://localhost:8025`
  - Travel.Host: `http://localhost:5000` (or port shown in dashboard) returns 200 (default route)

- [ ] **Step 4: Stop and commit a note**

```bash
git commit --allow-empty -m "chore: verified aspire run brings up full stack"
```

---

# Phase 4 — Module Skeletons

## Task 15: Create `flights` module skeleton

**Files:**
- Create: 4 .csproj projects under `modules/flights/`
- Create: `modules/flights/CLAUDE.md`

- [ ] **Step 1: Create the four .NET projects**

```bash
for layer in Core Application Infrastructure Api; do
  dotnet new classlib -o "modules/flights/Travel.Modules.Flights.$layer" --framework net10.0 --no-restore
  rm "modules/flights/Travel.Modules.Flights.$layer/Class1.cs"
done
```

- [ ] **Step 2: Set up project references** — edit each `.csproj`:

`Travel.Modules.Flights.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\shared\dotnet\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <ProjectReference Include="..\..\..\shared\dotnet\Travel.Shared.Domain\Travel.Shared.Domain.csproj" />
  </ItemGroup>
</Project>
```

`Travel.Modules.Flights.Application.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Travel.Modules.Flights.Core\Travel.Modules.Flights.Core.csproj" />
    <PackageReference Include="WolverineFx" />
  </ItemGroup>
</Project>
```

`Travel.Modules.Flights.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Travel.Modules.Flights.Application\Travel.Modules.Flights.Application.csproj" />
    <ProjectReference Include="..\..\..\shared\dotnet\Travel.Shared.Infrastructure\Travel.Shared.Infrastructure.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
</Project>
```

`Travel.Modules.Flights.Api.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Travel.Modules.Flights.Infrastructure\Travel.Modules.Flights.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add a module marker to Core**

Create `modules/flights/Travel.Modules.Flights.Core/FlightsModuleMarker.cs`:

```csharp
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core;

public sealed class FlightsModuleMarker : IModuleAssemblyMarker { }
```

- [ ] **Step 4: Reference all four projects from Travel.Host** — add to `apps/Travel.Host/Travel.Host.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Api\Travel.Modules.Flights.Api.csproj" />
  <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Application\Travel.Modules.Flights.Application.csproj" />
  <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Infrastructure\Travel.Modules.Flights.Infrastructure.csproj" />
  <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Core\Travel.Modules.Flights.Core.csproj" />
</ItemGroup>
```

- [ ] **Step 5: Write `modules/flights/CLAUDE.md`** (skeleton — see spec section 7.2 for template)

```markdown
# Flights module

**Status:** каркас (production-grade реализация в подпроекте 1)

## Bounded context
Поиск и бронирование авиабилетов. Mixed bookable + deeplink aggregation (Duffel + Travelpayouts).

## Aggregates
_TBD в подпроекте 1: BookingAggregate с состояниями OfferQuoted → Held → Confirmed → Ticketed → Refunded → Cancelled_

## Domain Events
_TBD в подпроекте 1_

## External integrations
_TBD в подпроекте 1: Duffel (bookable), Travelpayouts (deeplink)_

## Tests
- Unit:        `tests/flights/Travel.Modules.Flights.Tests.Unit/`
- Integration: `tests/flights/Travel.Modules.Flights.Tests.Integration/`
```

- [ ] **Step 6: Add to solution and build**

```bash
for layer in Core Application Infrastructure Api; do
  dotnet sln Travel.sln add "modules/flights/Travel.Modules.Flights.$layer/Travel.Modules.Flights.$layer.csproj"
done
dotnet build Travel.sln
```
Expected: builds succeed.

- [ ] **Step 7: Commit**

```bash
git add modules/flights apps/Travel.Host/Travel.Host.csproj Travel.sln
git commit -m "feat(flights): add module skeleton (Core/Application/Infrastructure/Api)"
```

---

## Task 16: Create `hotels` module skeleton

Identical pattern to Task 15. Substitute `flights` → `hotels`, `Flights` → `Hotels`.

- [ ] **Step 1**: Create four `.csproj` projects (Core/Application/Infrastructure/Api). Same project reference pattern as Task 15.
- [ ] **Step 2**: Add `HotelsModuleMarker.cs` in Core.
- [ ] **Step 3**: Reference four projects from `Travel.Host`.
- [ ] **Step 4**: Write `modules/hotels/CLAUDE.md` skeleton following the Task 15 template (status: "каркас"; bounded context derived from concept doc — hotel search via multi-supplier architecture + one happy-path booking; full content TBD in Subproject 2).
- [ ] **Step 5**: Add to solution, build, commit:

```bash
git commit -m "feat(hotels): add module skeleton"
```

---

## Task 17: Create `rail` module skeleton

Identical pattern. Substitute `rail` / `Rail`. CLAUDE.md follows Task 15 template — bounded context: read-only rail schedules (Yandex.Rasp + DB open data); no booking. Full content TBD in Subproject 3.

- [ ] **Step 1-5**: Same as Task 16. Commit:

```bash
git commit -m "feat(rail): add module skeleton"
```

---

## Task 18: Create `trips` module skeleton

Identical pattern. Substitute `trips` / `Trips`. CLAUDE.md follows Task 15 template — bounded context: trip planning as a composite over Flights/Hotels/Rail + AI-generated itineraries. Full content TBD in Subproject 4.

- [ ] **Step 1-5**: Same as Task 16. Commit:

```bash
git commit -m "feat(trips): add module skeleton"
```

---

## Task 19: Create `identity` module (slightly more substantial)

Foundation needs Identity wired up because Travel.Host references Keycloak from day one. This module is a skeleton with OIDC integration.

- [ ] **Step 1**: Same four-layer creation as Task 15. Substitute `identity` / `Identity`.
- [ ] **Step 2**: Add `IdentityModuleMarker.cs` in Core.
- [ ] **Step 3**: In `Travel.Modules.Identity.Infrastructure`, add OIDC bootstrap extension `IdentityServiceCollectionExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Travel.Modules.Identity.Infrastructure;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Keycloak:Authority"];
                options.Audience  = config["Keycloak:Audience"] ?? "travel-web";
                options.RequireHttpsMetadata = false; // dev only; flip to true in production via config
            });

        services.AddAuthorization();
        return services;
    }
}
```

- [ ] **Step 4**: Wire it up in `Travel.Host/Program.cs` — add the calls just after `builder.AddServiceDefaults()`:

```csharp
builder.Services.AddIdentityModule(builder.Configuration);
```

And after `var app = builder.Build();`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 5**: Configure Keycloak authority in `apps/Travel.Host/appsettings.json`:

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8180/realms/travel",
    "Audience":  "travel-web"
  }
}
```

- [ ] **Step 6**: Write `modules/identity/CLAUDE.md` (substantive, since this module IS implemented in Foundation):

```markdown
# Identity module

**Status:** Foundation-implemented (OIDC через Keycloak)

## Bounded context
Аутентификация и авторизация пользователей. OIDC через Keycloak, JWT bearer tokens.

## Implementation
- `Travel.Modules.Identity.Infrastructure.IdentityServiceCollectionExtensions.AddIdentityModule()` — JWT bearer + Keycloak authority
- Realm: `travel` (см. `infra/keycloak/travel-realm.json`)
- Тестовый пользователь: `dev@travel.local` / `dev123`

## Tests
- Unit/Integration tests появятся когда пишутся защищённые эндпойнты в Flights M1.
```

- [ ] **Step 7**: Add to solution, build, commit:

```bash
git commit -m "feat(identity): add Identity module with Keycloak OIDC integration"
```

---

# Phase 5 — Frontend (Angular)

## Task 20: Create `apps/web` Angular shell

**Files:**
- Create: `apps/web/` Angular 21 app via NX

- [ ] **Step 1: Generate Angular app via NX**

```bash
npx nx g @nx/angular:app web \
  --directory=apps/web \
  --routing=true \
  --style=scss \
  --standalone=true \
  --strict=true \
  --ssr=true \
  --no-interactive
```

- [ ] **Step 2: Set zoneless change detection** — edit `apps/web/src/app/app.config.ts`:

```typescript
import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(appRoutes),
    provideHttpClient(withFetch()),
  ],
};
```

- [ ] **Step 3: Verify dev server starts**

```bash
npx nx serve web
```
Expected: `http://localhost:4200` shows the default Angular page.

- [ ] **Step 4: Commit**

```bash
git add apps/web nx.json package.json package-lock.json
git commit -m "feat(web): add Angular 21 shell with zoneless + standalone + SSR"
```

---

## Task 21: Set up Tailwind v4 + PrimeNG unstyled

- [ ] **Step 1: Install dependencies**

```bash
npm install -D tailwindcss@~4.2 @tailwindcss/postcss
npm install primeng@~21 primeicons tailwindcss-primeui
```

- [ ] **Step 2: Configure PostCSS** — create `apps/web/postcss.config.json`:

```json
{
  "plugins": { "@tailwindcss/postcss": {} }
}
```

- [ ] **Step 3: Set up Tailwind in main stylesheet** — replace `apps/web/src/styles.scss`:

```scss
@import "tailwindcss";
@plugin "tailwindcss-primeui";
```

- [ ] **Step 4: Configure PrimeNG unstyled in `app.config.ts`**

Add to providers:

```typescript
import { providePrimeNG } from 'primeng/config';
import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';

const TravelPreset = definePreset(Aura, { /* customizations TBD per UI design */ });

// In providers array:
providePrimeNG({ theme: { preset: TravelPreset, options: { darkModeSelector: '.dark' } } }),
```

- [ ] **Step 5: Smoke test**

```bash
npx nx serve web
```
Expected: page loads, no console errors.

- [ ] **Step 6: Commit**

```bash
git add apps/web package.json package-lock.json
git commit -m "feat(web): integrate Tailwind v4 + PrimeNG unstyled"
```

---

## Task 22: Create `shared/ts/api-client` lib

- [ ] **Step 1: Generate NX library**

```bash
npx nx g @nx/js:lib api-client \
  --directory=shared/ts/api-client \
  --bundler=vite \
  --unitTestRunner=vitest \
  --no-interactive
```

- [ ] **Step 2: Add path alias** — edit `tsconfig.base.json`:

```json
"paths": {
  "@travel/api-client": ["shared/ts/api-client/src/index.ts"]
}
```

- [ ] **Step 3: Add placeholder export** — `shared/ts/api-client/src/index.ts`:

```typescript
export const apiClientPlaceholder = 'heyAPI generation wired in Flights M1';
```

- [ ] **Step 4: Commit**

```bash
git add shared/ts/api-client tsconfig.base.json nx.json package.json package-lock.json
git commit -m "feat(api-client): add shared/ts/api-client NX lib (heyAPI generation TBD)"
```

---

## Task 23: Create `shared/ts/ui-kit` lib

- [ ] **Step 1: Generate NX Angular library**

```bash
npx nx g @nx/angular:lib ui-kit \
  --directory=shared/ts/ui-kit \
  --buildable=true \
  --standalone=true \
  --no-interactive
```

- [ ] **Step 2: Add path alias** — `tsconfig.base.json`:

```json
"paths": {
  "@travel/api-client": ["shared/ts/api-client/src/index.ts"],
  "@travel/ui-kit":     ["shared/ts/ui-kit/src/index.ts"]
}
```

- [ ] **Step 3: Commit**

```bash
git add shared/ts/ui-kit tsconfig.base.json nx.json package.json
git commit -m "feat(ui-kit): add shared/ts/ui-kit NX lib"
```

---

# Phase 6 — Architecture Tests (Real, Working)

## Task 24: Create `Travel.Tests.Architecture` project

**Files:**
- Create: `tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj`
- Create: `tests/Travel.Tests.Architecture/ArchitectureTestBase.cs`

- [ ] **Step 1: Create project**

```bash
dotnet new classlib -o tests/Travel.Tests.Architecture --framework net10.0 --no-restore
rm tests/Travel.Tests.Architecture/Class1.cs
```

- [ ] **Step 2: Edit `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.v3.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="TngTech.ArchUnitNET.xUnitV3" />
    <PackageReference Include="Shouldly" />

    <!-- Reference shared projects so ArchUnit can resolve their types when scanning modules -->
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Domain\Travel.Shared.Domain.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Infrastructure\Travel.Shared.Infrastructure.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.Web\Travel.Shared.Web.csproj" />

    <!-- Reference all module marker assemblies for ArchUnit scanning -->
    <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Core\Travel.Modules.Flights.Core.csproj" />
    <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Application\Travel.Modules.Flights.Application.csproj" />
    <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Infrastructure\Travel.Modules.Flights.Infrastructure.csproj" />
    <ProjectReference Include="..\..\modules\flights\Travel.Modules.Flights.Api\Travel.Modules.Flights.Api.csproj" />
    <ProjectReference Include="..\..\modules\hotels\Travel.Modules.Hotels.Core\Travel.Modules.Hotels.Core.csproj" />
    <ProjectReference Include="..\..\modules\hotels\Travel.Modules.Hotels.Application\Travel.Modules.Hotels.Application.csproj" />
    <ProjectReference Include="..\..\modules\hotels\Travel.Modules.Hotels.Infrastructure\Travel.Modules.Hotels.Infrastructure.csproj" />
    <ProjectReference Include="..\..\modules\hotels\Travel.Modules.Hotels.Api\Travel.Modules.Hotels.Api.csproj" />
    <!-- Same for rail, trips, identity -->
    <ProjectReference Include="..\..\modules\rail\Travel.Modules.Rail.Core\Travel.Modules.Rail.Core.csproj" />
    <ProjectReference Include="..\..\modules\rail\Travel.Modules.Rail.Application\Travel.Modules.Rail.Application.csproj" />
    <ProjectReference Include="..\..\modules\rail\Travel.Modules.Rail.Infrastructure\Travel.Modules.Rail.Infrastructure.csproj" />
    <ProjectReference Include="..\..\modules\rail\Travel.Modules.Rail.Api\Travel.Modules.Rail.Api.csproj" />
    <ProjectReference Include="..\..\modules\trips\Travel.Modules.Trips.Core\Travel.Modules.Trips.Core.csproj" />
    <ProjectReference Include="..\..\modules\trips\Travel.Modules.Trips.Application\Travel.Modules.Trips.Application.csproj" />
    <ProjectReference Include="..\..\modules\trips\Travel.Modules.Trips.Infrastructure\Travel.Modules.Trips.Infrastructure.csproj" />
    <ProjectReference Include="..\..\modules\trips\Travel.Modules.Trips.Api\Travel.Modules.Trips.Api.csproj" />
    <ProjectReference Include="..\..\modules\identity\Travel.Modules.Identity.Core\Travel.Modules.Identity.Core.csproj" />
    <ProjectReference Include="..\..\modules\identity\Travel.Modules.Identity.Application\Travel.Modules.Identity.Application.csproj" />
    <ProjectReference Include="..\..\modules\identity\Travel.Modules.Identity.Infrastructure\Travel.Modules.Identity.Infrastructure.csproj" />
    <ProjectReference Include="..\..\modules\identity\Travel.Modules.Identity.Api\Travel.Modules.Identity.Api.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write `ArchitectureTestBase.cs`** (shared ArchUnitNET architecture instance)

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Travel.Shared.Abstractions;

namespace Travel.Tests.Architecture;

public static class ArchitectureTestBase
{
    private static readonly Lazy<global::ArchUnitNET.Domain.Architecture> Lazy = new(() =>
        new ArchLoader()
            .LoadAssemblies(
                typeof(IModuleAssemblyMarker).Assembly,
                typeof(Modules.Flights.Core.FlightsModuleMarker).Assembly,
                typeof(Modules.Hotels.Core.HotelsModuleMarker).Assembly,
                typeof(Modules.Rail.Core.RailModuleMarker).Assembly,
                typeof(Modules.Trips.Core.TripsModuleMarker).Assembly,
                typeof(Modules.Identity.Core.IdentityModuleMarker).Assembly
            )
            .Build());

    public static global::ArchUnitNET.Domain.Architecture Architecture => Lazy.Value;
}
```

- [ ] **Step 4: Add to solution and build**

```bash
dotnet sln Travel.sln add tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj
dotnet build tests/Travel.Tests.Architecture
```
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add tests/Travel.Tests.Architecture Travel.sln
git commit -m "test(arch): add Travel.Tests.Architecture project with ArchUnit base"
```

---

## Task 25: Write `ModuleBoundaryTests` (TDD)

**Files:**
- Create: `tests/Travel.Tests.Architecture/ModuleBoundaryTests.cs`

- [ ] **Step 1: Write the test (it should pass — modules don't reference each other yet)**

```csharp
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public class ModuleBoundaryTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch = ArchitectureTestBase.Architecture;

    [Fact]
    public void Flights_module_does_not_depend_on_Hotels_internals()
    {
        Classes()
            .That().ResideInNamespace("Travel.Modules.Flights", true)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Travel.Modules.Hotels", true)
            .Check(Arch);
    }

    [Fact]
    public void Hotels_module_does_not_depend_on_Flights_internals()
    {
        Classes()
            .That().ResideInNamespace("Travel.Modules.Hotels", true)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Travel.Modules.Flights", true)
            .Check(Arch);
    }

    [Fact]
    public void Rail_module_does_not_depend_on_other_module_internals()
    {
        Classes()
            .That().ResideInNamespace("Travel.Modules.Rail", true)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching("Travel\\.Modules\\.(Flights|Hotels|Trips|Identity).*")
            .Check(Arch);
    }

    [Fact]
    public void Trips_module_does_not_depend_on_other_module_internals()
    {
        Classes()
            .That().ResideInNamespace("Travel.Modules.Trips", true)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching("Travel\\.Modules\\.(Flights|Hotels|Rail|Identity).*")
            .Check(Arch);
    }
}
```

- [ ] **Step 2: Run the test**

```bash
dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
```
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Travel.Tests.Architecture/ModuleBoundaryTests.cs
git commit -m "test(arch): add module boundary tests"
```

---

## Task 26: Write `DependencyDirectionTests`

**Files:**
- Create: `tests/Travel.Tests.Architecture/DependencyDirectionTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public class DependencyDirectionTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch = ArchitectureTestBase.Architecture;

    [Fact]
    public void Core_layers_must_not_depend_on_Infrastructure()
    {
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Core.*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Infrastructure.*")
            .Check(Arch);
    }

    [Fact]
    public void Application_layers_must_not_depend_on_Api()
    {
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Application.*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Api.*")
            .Check(Arch);
    }

    [Fact]
    public void Infrastructure_layers_must_not_depend_on_Api()
    {
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Infrastructure.*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Api.*")
            .Check(Arch);
    }

    [Fact]
    public void Core_and_Application_layers_must_not_depend_on_Travel_Shared_Web()
    {
        // Travel.Shared.Web carries AspNetCore framework reference (ProblemDetails etc).
        // Only Api layer and apps/ may consume it; Domain/Application must stay web-free
        // so they can be reused in non-HTTP hosts (background workers, AI service).
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.(Core|Application).*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Travel.Shared.Web", true)
            .Check(Arch);
    }
}
```

- [ ] **Step 2: Run**

```bash
dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
```
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Travel.Tests.Architecture/DependencyDirectionTests.cs
git commit -m "test(arch): add dependency direction tests"
```

---

## Task 27: Write `NamingConventionTests`

**Files:**
- Create: `tests/Travel.Tests.Architecture/NamingConventionTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public class NamingConventionTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch = ArchitectureTestBase.Architecture;

    [Fact]
    public void Classes_in_Handlers_namespace_end_with_Handler()
    {
        Classes()
            .That().ResideInNamespaceMatching(@".*\.Handlers")
            .Should().HaveNameEndingWith("Handler")
            .Check(Arch);
    }

    [Fact]
    public void Classes_in_Exceptions_namespace_end_with_Exception()
    {
        Classes()
            .That().ResideInNamespaceMatching(@".*\.Exceptions")
            .Should().HaveNameEndingWith("Exception")
            .Check(Arch);
    }

    [Fact]
    public void Domain_event_classes_implement_IDomainEvent()
    {
        Classes()
            .That().HaveNameEndingWith("Event").And().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Core.*")
            .Should().ImplementInterface("IDomainEvent")
            .Check(Arch);
    }
}
```

- [ ] **Step 2: Run**

```bash
dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
```
Expected: PASS (all rules vacuously true since modules are empty).

- [ ] **Step 3: Commit**

```bash
git add tests/Travel.Tests.Architecture/NamingConventionTests.cs
git commit -m "test(arch): add naming convention tests"
```

---

# Phase 7 — Per-Module Test Project Skeletons

## Task 28-32: Create empty test projects per module

For each of `flights`, `hotels`, `rail`, `trips`, `identity`, create a Unit and Integration test project (10 projects total).

- [ ] **Step 1: Loop through modules and create projects**

```bash
for mod in flights hotels rail trips identity; do
  Mod=$(echo "$mod" | sed 's/./\u&/')
  for kind in Unit Integration; do
    proj="tests/$mod/Travel.Modules.$Mod.Tests.$kind"
    dotnet new classlib -o "$proj" --framework net10.0 --no-restore
    rm "$proj/Class1.cs"
  done
done
```

- [ ] **Step 2: Configure each test project's `.csproj`** — example for `flights/Tests.Unit`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.v3.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Verify.XunitV3" />
    <PackageReference Include="Shouldly" />
    <ProjectReference Include="..\..\..\modules\flights\Travel.Modules.Flights.Core\Travel.Modules.Flights.Core.csproj" />
    <ProjectReference Include="..\..\..\modules\flights\Travel.Modules.Flights.Application\Travel.Modules.Flights.Application.csproj" />
  </ItemGroup>
</Project>
```

For `Tests.Integration` projects, additionally reference `Travel.Shared.TestInfrastructure`:

```xml
<ProjectReference Include="..\..\..\shared\dotnet\Travel.Shared.TestInfrastructure\Travel.Shared.TestInfrastructure.csproj" />
<ProjectReference Include="..\..\..\modules\flights\Travel.Modules.Flights.Infrastructure\Travel.Modules.Flights.Infrastructure.csproj" />
```

Repeat the pattern for hotels/rail/trips/identity, swapping the module name in the project references.

- [ ] **Step 3: Add all 10 projects to solution**

```bash
for mod in flights hotels rail trips identity; do
  Mod=$(echo "$mod" | sed 's/./\u&/')
  for kind in Unit Integration; do
    dotnet sln Travel.sln add "tests/$mod/Travel.Modules.$Mod.Tests.$kind/Travel.Modules.$Mod.Tests.$kind.csproj"
  done
done
```

- [ ] **Step 4: Build full solution**

```bash
dotnet build Travel.sln
```
Expected: all projects build (no tests yet).

- [ ] **Step 5: Commit**

```bash
git add tests/ Travel.sln
git commit -m "test: add per-module test project skeletons (Unit + Integration × 5 modules)"
```

---

## Task 33: Create cross-cutting test project skeletons (Contract, AiEvals)

- [ ] **Step 1: Create**

```bash
for kind in Contract AiEvals; do
  proj="tests/Travel.Tests.$kind"
  dotnet new classlib -o "$proj" --framework net10.0 --no-restore
  rm "$proj/Class1.cs"
done
```

- [ ] **Step 2: Configure `.csproj`** — minimal test SDK references only (no Pact / eval framework yet):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.v3.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
</Project>
```

Add a placeholder file in each `tests/Travel.Tests.Contract/Placeholder.cs`:

```csharp
namespace Travel.Tests.Contract;

// Empty — Pact contract tests are added in Subproject 5 (AI service core)
internal static class Placeholder { }
```

Same pattern for `AiEvals/Placeholder.cs` (note "added in Subproject 1 (Flights)").

- [ ] **Step 3: Add to solution**

```bash
dotnet sln Travel.sln add tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj
dotnet sln Travel.sln add tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj
```

- [ ] **Step 4: Commit**

```bash
git add tests/Travel.Tests.Contract tests/Travel.Tests.AiEvals Travel.sln
git commit -m "test: add Travel.Tests.Contract and Travel.Tests.AiEvals skeletons"
```

---

# Phase 8 — Vertical Slice (E2E baseline)

## Task 34: Add EF DbContext for the host (just for the SELECT version() query)

**Files:**
- Create: `apps/Travel.Host/Persistence/HostDbContext.cs`

- [ ] **Step 1: Write the DbContext**

```csharp
using Microsoft.EntityFrameworkCore;

namespace Travel.Host.Persistence;

public sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options)
{
    public async Task<string> GetServerVersionAsync(CancellationToken ct)
    {
        // Use raw SQL since we don't have any model-mapped entities yet.
        await using var conn = Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT current_setting('server_version')";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString() ?? "unknown";
    }
}
```

- [ ] **Step 2: Register in `Travel.Host/Program.cs`** — add after `builder.AddServiceDefaults()`. Snake_case convention is wired via the official `EFCore.NamingConventions` package:

```csharp
builder.AddNpgsqlDbContext<HostDbContext>("travel", configureDbContextOptions: opts =>
{
    opts.UseSnakeCaseNamingConvention(); // PostgreSQL convention via EFCore.NamingConventions package
});
```

This uses Aspire's integration helper that pulls connection string from `Travel.AppHost`.

- [ ] **Step 3: Build**

```bash
dotnet build apps/Travel.Host
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add apps/Travel.Host/Persistence apps/Travel.Host/Program.cs
git commit -m "feat(host): add HostDbContext with GetServerVersionAsync"
```

---

## Task 35: TDD — write Alba integration test for `GET /api/status`

**Files:**
- Create: `tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj`
- Create: `tests/Travel.Host.Tests.Integration/StatusEndpointTests.cs`

> Decision: `StatusEndpoint` lives in `apps/Travel.Host/Features/Status/`. We use **Alba** (official JasperFx testing companion for Wolverine.Http) — it spins up the real `Program.cs` pipeline (routing, model binding, source-generated handlers, DI, ProblemDetails), then issues real HTTP requests. This is the idiomatic test for a WolverineFx.Http endpoint — testing the static endpoint method directly bypasses the whole framework.

- [ ] **Step 1: Create `tests/Travel.Host.Tests.Integration/` project**

```bash
dotnet new classlib -o tests/Travel.Host.Tests.Integration --framework net10.0 --no-restore
rm tests/Travel.Host.Tests.Integration/Class1.cs
```

`.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.v3.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="Alba" />
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
    <ProjectReference Include="..\..\apps\Travel.Host\Travel.Host.csproj" />
    <ProjectReference Include="..\..\apps\Travel.AppHost\Travel.AppHost.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.TestInfrastructure\Travel.Shared.TestInfrastructure.csproj" />
  </ItemGroup>
</Project>
```

> `Program.cs` in `Travel.Host` must expose its entry as `public partial class Program;` at the bottom of the file so Alba/WebApplicationFactory can reference it generically. Add this in Task 36.

Add to solution:

```bash
dotnet sln Travel.sln add tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj
```

- [ ] **Step 2: Write the failing Alba test** — `tests/Travel.Host.Tests.Integration/StatusEndpointTests.cs`:

```csharp
using Alba;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Host.Features.Status;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "Integration")]
public class StatusEndpointTests : IntegrationTestBase
{
    [Fact]
    public async Task Get_status_returns_200_with_postgres_version_and_db_ok()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-15T10:00:00Z"));

        await using var host = await AlbaHost.For<Program>(builder =>
        {
            // Inject the Testcontainers connection string BEFORE Program.cs runs AddNpgsqlDbContext.
            // The Aspire client integration resolves ConnectionStrings:travel from IConfiguration —
            // overriding it here makes the EF DbContext point at our test container.
            builder.UseSetting("ConnectionStrings:travel", ConnectionString);

            // Disable Aspire's OTel exporter in tests (no OTLP endpoint running).
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");

            builder.ConfigureServices(services =>
            {
                // Replace the system TimeProvider with a fake so we can assert exact timestamps.
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(fakeTime);
            });
        });

        var result = await host.Scenario(_ =>
        {
            _.Get.Url("/api/status");
            _.StatusCodeShouldBeOk();
            _.ContentTypeShouldBe("application/json; charset=utf-8");
        });

        var response = result.ReadAsJson<StatusResponse>();
        response.ShouldNotBeNull();
        response!.Db.ShouldBe("ok");
        response.Version.ShouldNotBeNullOrEmpty();
        response.Timestamp.ShouldBe(fakeTime.GetUtcNow());
    }
}
```

- [ ] **Step 3: Run — expect compile failure (StatusEndpoint doesn't exist yet)**

```bash
dotnet test tests/Travel.Host.Tests.Integration --filter Category=Integration
```
Expected: FAIL with "StatusEndpoint / StatusResponse not found".

- [ ] **Step 4: Commit (test only, before implementation)**

```bash
git add tests/Travel.Host.Tests.Integration Travel.sln
git commit -m "test(host): add failing Alba integration test for GET /api/status"
```

---

## Task 36: Implement `StatusResponse` + WolverineFx.Http endpoint (with `TimeProvider`)

**Files:**
- Create: `apps/Travel.Host/Features/Status/StatusResponse.cs`
- Create: `apps/Travel.Host/Features/Status/StatusEndpoint.cs`
- Modify: `apps/Travel.Host/Program.cs` — expose `Program` partial class for tests, register `TimeProvider`

> WolverineFx.Http pattern: an endpoint is a static method decorated with `[WolverineGet]`/`[WolverinePost]`. The method **returns** the typed response (no side-effect via `Send.X()` like FastEndpoints). For cascading, return a tuple. A source generator emits endpoint registration code at build time. **Time is injected via `TimeProvider` (BCL, .NET 8+)** — never `DateTimeOffset.UtcNow` inline; tests substitute `FakeTimeProvider`.

- [ ] **Step 1: Write `StatusResponse.cs`**

```csharp
namespace Travel.Host.Features.Status;

public sealed record StatusResponse(string Version, string Db, DateTimeOffset Timestamp);
```

- [ ] **Step 2: Write `StatusEndpoint.cs`** — WolverineFx.Http style with injected `TimeProvider`

```csharp
using Travel.Host.Persistence;
using Wolverine.Http;

namespace Travel.Host.Features.Status;

public static class StatusEndpoint
{
    [WolverineGet("/api/status")]
    public static async Task<StatusResponse> GetAsync(
        HostDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var version = await db.GetServerVersionAsync(ct);
        return new StatusResponse(version, "ok", clock.GetUtcNow());
    }
}
```

- [ ] **Step 3: Update `apps/Travel.Host/Program.cs`** — register `TimeProvider.System` and expose `Program` for Alba

Add into the services-registration section (after `builder.AddServiceDefaults();`):

```csharp
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
```

And at the very bottom of `Program.cs` (after `await app.RunAsync();`):

```csharp
public partial class Program;
```

This makes the top-level statements' implicit `Program` class accessible to `AlbaHost.For<Program>()`.

- [ ] **Step 4: Run the Alba integration test from Task 35**

```bash
dotnet test tests/Travel.Host.Tests.Integration --filter Category=Integration
```
Expected: PASS — real HTTP request → Wolverine.Http source-generated handler → EF Core → Testcontainers Postgres; `FakeTimeProvider` gives a deterministic timestamp assertion.

- [ ] **Step 5: Verify host builds and starts**

```bash
dotnet build apps/Travel.Host
```
Expected: build succeeds. WolverineFx.Http source generator emits endpoint registration code at compile time.

- [ ] **Step 6: Manual smoke test via aspire**

```bash
dotnet run --project apps/Travel.AppHost
```

In the Aspire dashboard, find the `host` resource URL, then:

```bash
curl http://localhost:<host-port>/api/status
```
Expected: `{"version":"17.x","db":"ok","timestamp":"..."}`

- [ ] **Step 7: Commit**

```bash
git add apps/Travel.Host/Features apps/Travel.Host/Program.cs tests/Travel.Host.Tests.Integration
git commit -m "feat(host): add GET /api/status vertical slice (WolverineFx.Http + TimeProvider + Alba)"
```

---

## Task 36b: Add end-to-end smoke test via `DistributedApplicationTestingBuilder`

**Files:**
- Create: `tests/Travel.Host.Tests.Integration/AspireStackSmokeTests.cs`

> Rationale: Alba tests verify the host's HTTP pipeline in isolation (fast — ~1-3s with shared Postgres container). This single test boots the **entire Aspire AppHost** (all real containers — Postgres, Redis, NATS, Keycloak, Mailpit) and hits the live `host` endpoint via its actual URL. It proves the full Aspire wiring: connection-string discovery, container readiness, OTel propagation, real network. Slow (~30-90s) — keep it as one focused smoke test gated by category, not a per-feature pattern.

- [ ] **Step 1: Write the smoke test**

```csharp
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "AspireSmoke")]
public class AspireStackSmokeTests
{
    [Fact(Timeout = 180_000)]
    public async Task Full_stack_boots_and_status_endpoint_responds()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Travel_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Wait until the host resource reports healthy (Aspire health probe)
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("host", TimeSpan.FromSeconds(120));

        var http = app.CreateHttpClient("host");
        var response = await http.GetAsync("/api/status");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"db\":\"ok\"");
    }
}
```

- [ ] **Step 2: Run** — note: gated by `Category=AspireSmoke`, runs only in CI on PR (60-90s per build).

```bash
dotnet test tests/Travel.Host.Tests.Integration --filter Category=AspireSmoke
```
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Travel.Host.Tests.Integration/AspireStackSmokeTests.cs
git commit -m "test(host): add full-stack Aspire smoke test (DistributedApplicationTestingBuilder)"
```

---

## Task 37: Add `getStatus()` to api-client

**Files:**
- Modify: `shared/ts/api-client/src/index.ts`
- Create: `shared/ts/api-client/src/status.client.ts`

- [ ] **Step 1: Write `status.client.ts`**

```typescript
export interface StatusResponse {
  version: string;
  db: string;
  timestamp: string;
}

export async function getStatus(baseUrl: string): Promise<StatusResponse> {
  const response = await fetch(`${baseUrl}/api/status`);
  if (!response.ok) throw new Error(`Status request failed: ${response.status}`);
  return (await response.json()) as StatusResponse;
}
```

- [ ] **Step 2: Re-export from `index.ts`**

```typescript
export * from './status.client';
```

- [ ] **Step 3: Commit**

```bash
git add shared/ts/api-client/src
git commit -m "feat(api-client): add getStatus()"
```

---

## Task 38: Build Angular `StatusPageComponent`

**Files:**
- Create: `apps/web/src/app/status/status-page.component.ts`
- Modify: `apps/web/src/app/app.routes.ts`

- [ ] **Step 1: Write the component**

```typescript
import { Component, ChangeDetectionStrategy, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';

@Component({
  selector: 'app-status-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="p-8">
      <h1 class="text-2xl font-bold">System Status</h1>
      @if (status.isLoading()) {
        <p>Loading…</p>
      } @else if (status.error()) {
        <p class="text-red-600">Error: {{ status.error()!.message }}</p>
      } @else if (status.value(); as s) {
        <ul class="mt-4 space-y-2">
          <li>db: {{ s.db }}</li>
          <li>version: {{ s.version }}</li>
          <li>timestamp: {{ s.timestamp }}</li>
        </ul>
      }
    </main>
  `,
})
export class StatusPageComponent {
  status = httpResource<{ version: string; db: string; timestamp: string }>(
    () => '/api/status',
  );
}
```

- [ ] **Step 2: Add route** — edit `apps/web/src/app/app.routes.ts`:

```typescript
import { Routes } from '@angular/router';

export const appRoutes: Routes = [
  {
    path: 'status',
    loadComponent: () => import('./status/status-page.component').then(m => m.StatusPageComponent),
  },
  { path: '', redirectTo: 'status', pathMatch: 'full' },
];
```

- [ ] **Step 3: Configure proxy for /api/** — create `apps/web/proxy.conf.json`:

```json
{
  "/api": { "target": "http://localhost:5000", "secure": false, "changeOrigin": true }
}
```

Update `apps/web/project.json` `serve` target to use the proxy:

```json
"serve": {
  "options": {
    "proxyConfig": "apps/web/proxy.conf.json"
  }
}
```

- [ ] **Step 4: Run dev server, verify in browser**

In one terminal: `dotnet run --project apps/Travel.AppHost`  
In another:      `npx nx serve web`

Open `http://localhost:4200/status` — expect to see "db: ok" and a Postgres version.

- [ ] **Step 5: Commit**

```bash
git add apps/web
git commit -m "feat(web): add StatusPage with httpResource (vertical slice UI)"
```

---

## Task 39: Set up Playwright E2E project

**Files:**
- Create: `tests/travel-e2e/` Playwright NX project

- [ ] **Step 1: Generate Playwright project via NX**

```bash
npx nx g @nx/playwright:configuration travel-e2e \
  --directory=tests/travel-e2e \
  --project=web \
  --no-interactive
```

- [ ] **Step 2: Configure `tests/travel-e2e/playwright.config.ts`**

```typescript
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  reporter: process.env['CI'] ? 'github' : 'list',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
```

- [ ] **Step 3: Commit**

```bash
git add tests/travel-e2e nx.json package.json package-lock.json
git commit -m "test(e2e): add Playwright E2E project"
```

---

## Task 40: Write `health.spec.ts` E2E test (TDD)

**Files:**
- Create: `tests/travel-e2e/specs/health.spec.ts`

- [ ] **Step 1: Write the test**

```typescript
import { test, expect } from '@playwright/test';

test.describe('System status', () => {
  test('status page shows db ok', async ({ page }) => {
    await page.goto('/status');
    await expect(page.getByText(/db: ok/)).toBeVisible({ timeout: 10000 });
  });
});
```

- [ ] **Step 2: Run E2E (with stack running)**

In one terminal: `dotnet run --project apps/Travel.AppHost`  
In another:      `npx nx serve web`  
In a third:      `npx nx e2e travel-e2e`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/travel-e2e/specs/health.spec.ts
git commit -m "test(e2e): add health.spec for vertical slice"
```

---

# Phase 9 — CI Pipeline

## Task 41: Create `.github/workflows/ci.yml`

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: CI

on:
  push:
    branches: ['**']
  pull_request:
    branches: [master]

env:
  NX_BASE: ${{ github.event.pull_request.base.sha || 'origin/master' }}
  NX_HEAD: HEAD
  NX_CLOUD_ACCESS_TOKEN: ${{ secrets.NX_CLOUD_ACCESS_TOKEN }}

jobs:
  lint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - run: npm ci
      - run: npx biome ci .
      - run: dotnet tool install -g csharpier
      - run: dotnet csharpier --check .

  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key:  nuget-${{ hashFiles('**/*.csproj','Directory.Packages.props') }}
      - uses: actions/cache@v4
        with:
          path: .nx/cache
          key:  nx-${{ hashFiles('nx.json','package-lock.json') }}
      - run: npm ci
      - run: npx nx affected -t build --base=$NX_BASE --head=$NX_HEAD

  test-unit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - run: npm ci
      - run: npx nx affected -t test --base=$NX_BASE --head=$NX_HEAD

  test-arch:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - run: dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture --logger "trx;LogFileName=arch.trx"

  test-aspire-smoke:
    needs: [build, test-unit]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - run: dotnet test tests/Travel.Host.Tests.Integration --filter Category=AspireSmoke --logger "trx;LogFileName=aspire-smoke.trx"

  test-e2e:
    needs: [build, test-unit, test-arch, test-aspire-smoke]
    if: github.event_name == 'pull_request' && github.base_ref == 'master'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - run: npm ci
      - run: npx playwright install --with-deps chromium
      # Start AppHost in background; capture PID for cleanup
      - name: Start Aspire AppHost
        run: |
          dotnet run --project apps/Travel.AppHost > apphost.log 2>&1 &
          echo "APPHOST_PID=$!" >> $GITHUB_ENV
      # Wait for the host endpoint via the vertical-slice probe (60×2s = 120s max)
      - name: Wait for /api/status
        run: |
          for i in {1..60}; do
            if curl --silent --fail --max-time 2 http://localhost:5000/api/status > /dev/null; then
              echo "Host is up (attempt $i)"; exit 0
            fi
            sleep 2
          done
          echo "Host did not respond in 120s"; tail -200 apphost.log; exit 1
      # Start Angular dev server and wait for it
      - name: Start Angular and wait for :4200
        run: |
          npx nx serve web > web.log 2>&1 &
          for i in {1..40}; do
            if curl --silent --fail --max-time 2 http://localhost:4200/ > /dev/null; then
              echo "Web is up (attempt $i)"; exit 0
            fi
            sleep 2
          done
          echo "Web did not respond in 80s"; tail -200 web.log; exit 1
      - run: npx nx e2e travel-e2e
      - name: Cleanup
        if: always()
        run: |
          if [ -n "${APPHOST_PID:-}" ]; then kill $APPHOST_PID || true; fi
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add main CI workflow (lint/build/test/arch/e2e)"
```

---

## Task 42: Create `.github/workflows/deploy.yml`

**Files:**
- Create: `.github/workflows/deploy.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: Deploy

on:
  push:
    branches: [master]
    tags: ['v*']
  workflow_dispatch:

jobs:
  publish-images:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - name: Build images
        run: |
          dotnet publish apps/Travel.Host -c Release --os linux --arch x64 /t:PublishContainer \
            -p:ContainerRepository=ghcr.io/${{ github.repository_owner }}/travel-host \
            -p:ContainerImageTag=${{ github.sha }}
          dotnet publish apps/Travel.AI -c Release --os linux --arch x64 /t:PublishContainer \
            -p:ContainerRepository=ghcr.io/${{ github.repository_owner }}/travel-ai \
            -p:ContainerImageTag=${{ github.sha }}
      - name: Tag latest
        run: |
          docker tag ghcr.io/${{ github.repository_owner }}/travel-host:${{ github.sha }} ghcr.io/${{ github.repository_owner }}/travel-host:latest
          docker tag ghcr.io/${{ github.repository_owner }}/travel-ai:${{ github.sha }}   ghcr.io/${{ github.repository_owner }}/travel-ai:latest
          docker push --all-tags ghcr.io/${{ github.repository_owner }}/travel-host
          docker push --all-tags ghcr.io/${{ github.repository_owner }}/travel-ai
```

> NOTE: VPS SSH-deploy step is intentionally omitted — wired up only when the user has a VPS ready and provides SSH key as repo secret. Commit message documents this.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/deploy.yml
git commit -m "ci: add deploy workflow (image publish to GHCR; SSH-deploy step deferred)"
```

---

# Phase 10 — Devcontainer

## Task 43: Create devcontainer config

**Files:**
- Create: `.devcontainer/devcontainer.json`

- [ ] **Step 1: Write `.devcontainer/devcontainer.json`** — see spec section 5 for the exact JSON content. Copy verbatim.

- [ ] **Step 2: Verify (locally if Docker is available)**

```bash
# In VS Code: Ctrl+Shift+P → "Dev Containers: Reopen in Container"
# Wait for postCreateCommand to finish.
# Inside the container:
dotnet --version  # 10.0.x
node --version    # v22.x
docker info       # confirms Docker-outside-of-Docker works
```

If Docker isn't available locally, skip the verification — the config will be tested when CI builds an image.

- [ ] **Step 3: Commit**

```bash
git add .devcontainer
git commit -m "chore: add VS Code devcontainer config"
```

---

# Phase 11 — ADR Set (12 ADRs)

> NOTE: Each ADR is its own short doc (Context / Decision / Alternatives / Consequences / Out of Scope / References). Spec section 6 lists titles and key decisions. Use the standard template in spec section 7.3 (adr-writer agent prompt) as the structural reference.

## Task 44: Write 12 architecturally-relevant ADRs

**Files:**
- Create: `docs/adr/0001-modular-monolith.md` through `0012-maf-as-primary-agent-runtime.md` (12 files)

> **Why 12 and not 20:** ADRs are for **architectural** decisions — items where reasonable people would disagree, where the choice has long-term irreversibility cost, and where future readers need the *why*. Tooling selections (Renovate, NX Cloud tier, Lefthook, OSS templates) are operational conventions, not architecture; they go into `docs/conventions/` as short notes. Sub-feature decisions (notifications channels, payments strategy, UI library specifics) are deferred to the subproject where they actually get implemented. This filters out cargo-cult template-fill and keeps each remaining ADR carrying real weight.

- [ ] **Step 1: Write all 12 ADRs**

For each ADR in spec section 6, create the corresponding `docs/adr/NNNN-{title}.md` file. Each ADR follows the format below — fill in the specific Context, Decision, Alternatives Considered, Consequences, Out of Scope, and References per the spec's "Key decision" column and the concept doc (`docs/superpowers/specs/2026-05-03-travel-platform-concept.md`):

```markdown
# NNNN. Title

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context
[1-2 paragraphs explaining what's making this decision necessary now. Pull from the concept doc and Foundation spec.]

## Decision
[The specific decision in one paragraph.]

## Alternatives Considered

### Option A: [name]
[Description + why rejected]

### Option B: [name]
[Description + why rejected]

## Consequences

### Positive
- [benefits]

### Negative / Trade-offs
- [costs and risks — every real decision has at least one]

### Neutral
- [observations]

## Out of Scope
[What this ADR explicitly does NOT decide]

## References
- Concept doc: docs/superpowers/specs/2026-05-03-travel-platform-concept.md
- Foundation spec: docs/superpowers/specs/2026-05-04-foundation-design.md
- [external links]
```

ADRs to write (titles, brief decisions — fill in Alternatives + Consequences from concept doc / Foundation spec context):

1. **`0001-modular-monolith.md`** — Travel.Host as modular monolith, not microservices. Alts: full microservices, single monolith without modules. Consequences: split-readiness without ops cost; risk: discipline required (mitigated by ArchUnitNET tests in Phase 6).
2. **`0002-ai-as-extracted-service.md`** — Travel.AI in separate process. Alts: AI in monolith, fully separate repo. Consequences: deploy cadence flexibility; cost: cross-process complexity. **Must address:** does Travel.AI share the Postgres schema with Travel.Host, or is it read-only? Answer in the ADR — this is load-bearing.
3. **`0003-wolverine-marten-stack.md`** — Critter Stack (Wolverine + Marten + WolverineFx.Http). Alts: MediatR + Dapper (MediatR went commercial July 2025), MassTransit (commercial Q1 2026). Consequences: single MIT stack from JasperFx; risk: smaller community than legacy stack.
4. **`0004-nx-monorepo-tooling.md`** — NX 22 + `@nx/dotnet`. Alts: Cake + npm scripts, Bazel, separate FE/BE repos. Consequences: unified tooling; risk: `@nx/dotnet` is recent (verify GA status before relying on it).
5. **`0005-frontend-stack.md`** — Angular 21 + Signals + httpResource + NgRx SignalStore + Tailwind v4 + PrimeNG unstyled (zoneless change detection). Alts: React + Next.js, Vue + Nuxt. Includes UI-library selection rationale (PrimeNG over Material/Ant for unstyled flexibility with Tailwind).
6. **`0006-testing-strategy.md`** — Seven-layer test approach: Shouldly assertions, Alba for HTTP integration tests, `DistributedApplicationTestingBuilder` for full-stack smoke, ArchUnitNET for boundary enforcement, Verify for snapshot, Testcontainers for DB. Note: FluentAssertions 8.0 went commercial Jan 2025 → Shouldly chosen.
7. **`0007-storage-strategy-marten-ef-coexistence.md`** — Marten owns `mt_*` (event sourcing for booking lifecycle), EF Core owns module schemas, single PostgreSQL instance. Alts: separate DBs per ORM, single ORM (ES everything or EF everything).
8. **`0008-result-pattern-error-or.md`** — ErrorOr (Amichai Mantinband) over custom Result\<T\>, OneOf, FluentResults, CSharpFunctionalExtensions, LanguageExt. Decision drivers: built-in HTTP error taxonomy, multiple-errors native, recognizable author in .NET clean architecture. Note: not exposed via global using; imported locally where needed.
9. **`0009-http-endpoints-wolverine.md`** — WolverineFx.Http over FastEndpoints / custom IEndpoint / plain Minimal APIs. Decision drivers: typed return value, source-generated handlers, native Wolverine integration, ProblemDetails + cascading messages. Includes the `Travel.Shared.Web` vs `Travel.Shared.Infrastructure` split rationale (keep AspNetCore out of Domain/Application transitive closure).
10. **`0010-keycloak-identity.md`** — Keycloak self-hosted. Alts: Auth0, custom IdentityServer, ASP.NET Identity.
11. **`0011-ai-eval-strategy.md`** — Own minimal eval framework in .NET. Alts: Promptfoo (acquired by OpenAI March 2026 — conflict of interest evaluating non-OpenAI models), Arize, LangSmith. Educational value for the showcase.
12. **`0012-maf-as-primary-agent-runtime.md`** — MAF 1.0 GA primary, custom Travel Advisor only (educational). Alts: SK directly, fully custom, fully MAF including Travel Advisor. *(Verify MAF GA date before writing — referenced as April 2026.)*

**Decisions NOT promoted to ADR** (live in `docs/conventions/` as short notes — convention, not architecture):
- Lefthook + commitlint + commitizen (developer tooling)
- Renovate config (dependency-management policy)
- NX Cloud Hobby tier (CI cost optimization)
- OSS polish (LICENSE/CoC/etc — repository hygiene, not architecture)

**Decisions deferred to their owning subproject:**
- Notifications channels (email + SSE) → Subproject 1 when first user-visible notification ships
- Payments strategy → Subproject 1 (Flights booking flow)

- [ ] **Step 2: Commit (one commit per ADR recommended for clean blame, but batch is acceptable)**

```bash
git add docs/adr/
git commit -m "docs: add Foundation ADR set (12 architecturally-relevant decisions)"
```

---

# Phase 11.5 — Developer Tooling Polish

> Phase tasks set up the polyglot developer experience and OSS hygiene that distinguish a production-grade showcase repo. These slot in before AI-Harness because Lefthook + commitlint hooks should be active before AI-harness commits start landing.

## Task 44a: Connect NX Cloud free tier (Hobby plan)

**Files:**
- Modify: `nx.json` (NX Cloud will inject `nxCloudId`)

- [ ] **Step 1: Connect**

```bash
npx nx connect
```

This opens a browser, prompts you to sign in to nx.app with GitHub, picks the workspace, and updates `nx.json` with the cloud config. **Do NOT commit any access tokens** — Nx Cloud uses workspace-level tokens written to env files (`.env.local`).

- [ ] **Step 2: Verify**

```bash
npx nx affected -t build --base=HEAD~1
```

Watch the dashboard at nx.app — task records should appear. Check the workspace usage page to confirm Hobby tier active.

- [ ] **Step 3: Add CI integration token to GitHub Actions secrets**

Get an `NX_CLOUD_ACCESS_TOKEN` from the workspace settings page. In the GitHub repo: Settings → Secrets and variables → Actions → New repository secret. Name: `NX_CLOUD_ACCESS_TOKEN`, value: the token.

> The env reference `NX_CLOUD_ACCESS_TOKEN: ${{ secrets.NX_CLOUD_ACCESS_TOKEN }}` is already declared at the top of `.github/workflows/ci.yml` (Task 41). No further workflow edit needed.

- [ ] **Step 4: Commit**

```bash
git add nx.json
git commit -m "ci(nx-cloud): connect NX Cloud Hobby plan for distributed cache + DTE"
```

---

## Task 44b: Set up Lefthook git-hooks

**Files:**
- Create: `lefthook.yml`
- Modify: `package.json` (add lefthook dev dependency + install script)

- [ ] **Step 1: Install Lefthook**

```bash
npm install -D lefthook
```

- [ ] **Step 2: Write `lefthook.yml`**

```yaml
pre-commit:
  parallel: true
  commands:
    csharpier:
      glob: "*.cs"
      run: dotnet csharpier {staged_files}
      stage_fixed: true
    biome:
      glob: "*.{ts,tsx,js,jsx,json}"
      run: npx biome format --write {staged_files} && npx biome check --write {staged_files}
      stage_fixed: true

commit-msg:
  commands:
    commitlint:
      run: npx commitlint --edit {1}

pre-push:
  commands:
    arch-tests:
      glob: "modules/**/*.cs"
      run: dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture --no-build
```

- [ ] **Step 3: Add `prepare` script to `package.json`**

```json
"scripts": {
  "prepare": "lefthook install"
}
```

Then run:

```bash
npm install  # triggers prepare → installs hooks into .git/hooks/
```

- [ ] **Step 4: Verify**

```bash
echo "test" > test.cs
git add test.cs
git commit -m "test"  # should fail commitlint (not Conventional Commits format)
rm test.cs
git reset HEAD test.cs
```

- [ ] **Step 5: Commit**

```bash
git add lefthook.yml package.json package-lock.json
git commit -m "chore(tooling): add Lefthook for pre-commit/commit-msg/pre-push hooks"
```

---

## Task 44c: Set up commitlint + commitizen for Conventional Commits

**Files:**
- Create: `commitlint.config.mjs`
- Modify: `package.json`

- [ ] **Step 1: Install**

```bash
npm install -D @commitlint/cli @commitlint/config-conventional commitizen cz-conventional-changelog
```

- [ ] **Step 2: Write `commitlint.config.mjs`**

```javascript
export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'header-max-length':  [2, 'always', 100],
    'body-max-line-length': [1, 'always', 120],
    'scope-enum': [2, 'always', [
      // module scopes
      'flights', 'hotels', 'rail', 'trips', 'identity',
      // app scopes
      'host', 'ai', 'web', 'aspire',
      // shared scopes
      'shared', 'api-client', 'ui-kit',
      // cross-cutting
      'ci', 'tooling', 'docs', 'deps', 'arch', 'test', 'infra',
    ]],
  },
};
```

- [ ] **Step 3: Configure commitizen** — add to `package.json`:

```json
"config": {
  "commitizen": { "path": "cz-conventional-changelog" }
},
"scripts": {
  "commit": "cz"
}
```

- [ ] **Step 4: Test**

```bash
echo "test" >> .gitignore
git add .gitignore
npx cz   # interactive Conventional Commits prompt
git reset HEAD .gitignore
git checkout .gitignore
```

- [ ] **Step 5: Commit**

```bash
git add commitlint.config.mjs package.json package-lock.json
git commit -m "chore(tooling): add commitlint + commitizen for Conventional Commits"
```

---

## Task 44d: Configure Renovate

**Files:**
- Create: `renovate.json`

- [ ] **Step 1: Write `renovate.json`**

```jsonc
{
  "$schema": "https://docs.renovatebot.com/renovate-schema.json",
  "extends": [
    "config:recommended",
    ":semanticCommits",
    ":dependencyDashboard",
    "schedule:earlyMondays"
  ],
  "labels": ["dependencies"],
  "prConcurrentLimit": 5,
  "packageRules": [
    {
      "matchPackageNames": ["/^Aspire\\./"],
      "groupName": ".NET Aspire packages"
    },
    {
      "matchPackageNames": ["/^WolverineFx/", "Marten"],
      "groupName": "Critter Stack (Wolverine + Marten)"
    },
    {
      "matchPackageNames": ["/^Microsoft\\.EntityFrameworkCore/", "/^Npgsql/", "EFCore.NamingConventions"],
      "groupName": "EF Core + Npgsql"
    },
    {
      "matchPackageNames": ["/^@angular\\//", "/^@nx\\//"],
      "groupName": "Angular + NX"
    },
    {
      "matchPackageNames": ["/^xunit/", "/^Microsoft\\.NET\\.Test/", "/^Verify/", "/^Testcontainers/", "Alba", "Shouldly"],
      "groupName": "Testing libraries"
    },
    {
      "matchUpdateTypes": ["patch"],
      "automerge": true,
      "labels": ["dependencies", "automerge"]
    }
  ],
  "vulnerabilityAlerts": { "labels": ["security"], "automerge": false }
}
```

- [ ] **Step 2: Install Renovate GitHub App**

Go to https://github.com/apps/renovate → click "Install" → select the travel-agency repo → grant access. Renovate will create an onboarding PR within ~10 minutes.

- [ ] **Step 3: Commit**

```bash
git add renovate.json
git commit -m "chore(deps): add Renovate config (grouped updates, semantic commits, weekly schedule)"
```

---

## Task 44e: OSS polish — LICENSE, CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, GitHub templates

**Files:**
- Create: `LICENSE`
- Create: `CONTRIBUTING.md`
- Create: `CODE_OF_CONDUCT.md`
- Create: `SECURITY.md`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`
- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `.github/ISSUE_TEMPLATE/feature_request.yml`
- Create: `.github/ISSUE_TEMPLATE/config.yml`
- Create: `.github/FUNDING.yml`

- [ ] **Step 1: Write `LICENSE`** (MIT, current year)

```
MIT License

Copyright (c) 2026 Vladimir Sinyavskiy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Write `CODE_OF_CONDUCT.md`** — Contributor Covenant 2.1 (copy from `https://www.contributor-covenant.org/version/2/1/code_of_conduct.txt`, replace contact email with the project's email)

- [ ] **Step 3: Write `CONTRIBUTING.md`**

```markdown
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
```

- [ ] **Step 4: Write `SECURITY.md`**

```markdown
# Security Policy

## Reporting vulnerabilities

If you discover a security vulnerability, please email [SECURITY_CONTACT_EMAIL] instead of opening a public issue. Include:
- Description of the vulnerability
- Steps to reproduce
- Affected components
- Suggested fix (if any)

You'll receive a response within 7 days. Once the issue is confirmed, we'll work on a fix and coordinate a disclosure timeline.

## Scope

This is a showcase / educational project. Bring-Your-Own-Keys means most secrets stay on the user's machine. Vulnerabilities of interest:
- Code execution paths from user input
- Auth bypass in identity flows
- Improper handling of API keys / secrets in logs
- Vulnerable dependencies (auto-monitored via Renovate + GitHub security advisories)
```

- [ ] **Step 5: Write `.github/PULL_REQUEST_TEMPLATE.md`**

```markdown
## Summary
<!-- 1-3 bullets explaining what this PR does and why -->

## Type
- [ ] feat
- [ ] fix
- [ ] refactor
- [ ] docs
- [ ] test
- [ ] chore

## Scope
<!-- module / app / shared / cross-cutting -->

## Test plan
- [ ] Unit tests pass
- [ ] Integration tests pass (if touched I/O code)
- [ ] Architecture tests pass
- [ ] Manual smoke test of affected feature

## Related
<!-- ADRs, issues, specs -->

## Notes for reviewer
<!-- Anything non-obvious -->
```

- [ ] **Step 6: Write `.github/ISSUE_TEMPLATE/bug_report.yml`**

```yaml
name: Bug report
description: Report a defect in the Travel platform
labels: [bug]
body:
  - type: textarea
    id: description
    attributes: { label: What happened?, description: Clear description of the bug }
    validations: { required: true }
  - type: textarea
    id: reproduction
    attributes: { label: Steps to reproduce, description: Numbered steps the maintainer can follow }
    validations: { required: true }
  - type: textarea
    id: expected
    attributes: { label: Expected behavior }
    validations: { required: true }
  - type: input
    id: env
    attributes: { label: Environment, description: ".NET version, Node version, OS" }
```

- [ ] **Step 7: Write `.github/ISSUE_TEMPLATE/feature_request.yml`**

```yaml
name: Feature request
description: Propose a new feature or enhancement
labels: [enhancement]
body:
  - type: textarea
    id: motivation
    attributes: { label: Motivation, description: What problem does this solve? }
    validations: { required: true }
  - type: textarea
    id: proposal
    attributes: { label: Proposal, description: What you'd like to see }
    validations: { required: true }
  - type: textarea
    id: alternatives
    attributes: { label: Alternatives considered }
```

- [ ] **Step 8: Write `.github/ISSUE_TEMPLATE/config.yml`**

```yaml
blank_issues_enabled: false
contact_links:
  - name: Security vulnerability
    url: https://github.com/svasorcery/travel-agency/security/policy
    about: Report security issues privately, not via public issues.
```

- [ ] **Step 9: Write `.github/FUNDING.yml`** (optional — uncomment when you want to accept sponsorship)

```yaml
# github: svasorcery
# ko_fi: yourname
# custom: https://yoursite.com/donate
```

- [ ] **Step 10: Commit**

```bash
git add LICENSE CONTRIBUTING.md CODE_OF_CONDUCT.md SECURITY.md .github/
git commit -m "docs: add OSS polish (LICENSE, contributing, code of conduct, security, GitHub templates)"
```

---

## Task 44f: Write rich `README.md`

**Files:**
- Replace: `README.md`

- [ ] **Step 1: Write the README** — structure:

```markdown
# Travel Platform

> Production-grade travel booking and trip-planning platform showcasing modern .NET + Angular + AI-augmented development practices.

[![CI](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml/badge.svg)](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Angular](https://img.shields.io/badge/Angular-21-DD0031)](https://angular.dev)

## What this is

Public showcase project demonstrating:
- DDD modular monolith with Wolverine + Marten + WolverineFx.Http
- Extracted AI service using Microsoft Agent Framework (MAF) 1.0
- Angular 21 + Signals + httpResource + NgRx SignalStore + Tailwind v4
- AI-augmented development with custom Claude Code agents, slash commands, and hooks
- Honest BYO-keys with graceful degradation across providers

Implementation roadmap:
- [x] **Subproject 0 — Foundation** (current): scaffold, AI-harness, vertical slice
- [ ] **Subproject 1 — Flights flagship**: end-to-end booking, NL-search, explainable ranking
- [ ] **Subproject 2 — Hotels**: multi-supplier search with dedup
- [ ] **Subproject 3 — Rail**: read-only multi-source schedules
- [ ] **Subproject 4 — Trip Planning**: AI-orchestrated multi-day itineraries
- [ ] **Subproject 5 — AI service core**: own eval framework, MAF deep-dive

## Quick start

### Prerequisites
- Docker (Desktop / Engine / Podman)
- Optional: VS Code with Dev Containers extension

### Run locally

```bash
git clone https://github.com/svasorcery/travel-agency
cd travel-agency
# Option A: Dev container (recommended) — open in VS Code, "Reopen in Container"
# Option B: native — install .NET 10 SDK + Node 22

npm ci
dotnet restore
dotnet run --project apps/Travel.AppHost
# In another terminal:
npx nx serve web
```

Open `http://localhost:4200/status` — you should see `db: ok` and a Postgres version.

The Aspire dashboard at `https://localhost:17002` shows all running resources.

## Architecture

(insert C4 context diagram or ASCII overview here)

See [CLAUDE.md](CLAUDE.md) for the architectural map and conventions, and [docs/adr/](docs/adr/) for all architectural decisions.

## Stack

| Layer | Choice | Why |
|---|---|---|
| Backend | .NET 10 + Aspire 13 + Critter Stack (Wolverine + Marten + WolverineFx.Http) | MIT-only after MediatR/MassTransit went commercial |
| Storage | PostgreSQL 17 + pgvector + Marten ES + EF Core 10 | Polyglot persistence on one database |
| Frontend | Angular 21 + Signals + Tailwind v4 + PrimeNG unstyled | Modern Angular with full SSR |
| Messaging | NATS JetStream + Wolverine outbox | In-process and cross-process |
| AI | MAF 1.0 + Claude (via Anthropic NuGet) | Production-ready agent framework |
| Tooling | NX 22 + Biome + CSharpier + Lefthook + commitlint + Renovate | Polyglot monorepo |

## Bring Your Own Keys

The project is designed for graceful degradation — providers without keys are simply not loaded, and the UI / logs document the missing source. To run with full functionality, see [docs/byo-keys.md](docs/byo-keys.md) (TBD — will be created in Subproject 1 when first external provider is wired in).

| Provider | Required for | Sandbox available? | Production from RU? |
|---|---|---|---|
| Anthropic API | All AI features | ✅ self-service | ⚠️ non-RU card |
| Duffel (Flights/Stays/Cars) | Flight bookings | ✅ self-service | ❌ KYC blocked |
| Travelpayouts | Flight deeplinks (RU content) | ✅ no auth | ✅ |
| Yandex.Rasp | Rail schedules | ✅ email-confirmed | ✅ |
| Keycloak (self-hosted) | Identity | ✅ Aspire-hosted | ✅ |

## Companion content

- Blog series: link TBD
- AI-augmented development sessions: [docs/ai-conversations/](docs/ai-conversations/)
- Architecture decisions: [docs/adr/](docs/adr/)
- Specs and plans: [docs/superpowers/](docs/superpowers/)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE).
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: rewrite README for Foundation showcase"
```

---

## Task 44g: Add Storybook 10 for component catalog

**Files:**
- Create: Storybook config in `apps/web/.storybook/`
- Create: One example story for the StatusPage component

- [ ] **Step 1: Add Storybook to apps/web**

```bash
npx nx g @nx/angular:storybook-configuration web --no-interactive
```

This generates `.storybook/main.ts`, `.storybook/preview.ts`, and Storybook target in `apps/web/project.json`.

- [ ] **Step 2: Verify Storybook 10 is used**

Check `package.json` — `@storybook/angular` should be `^10.x`. If not (NX scaffolds older), upgrade:

```bash
npx storybook@latest upgrade
```

- [ ] **Step 3: Add an example story** — create `apps/web/src/app/status/status-page.component.stories.ts`:

```typescript
import type { Meta, StoryObj } from '@storybook/angular';
import { StatusPageComponent } from './status-page.component';

const meta: Meta<StatusPageComponent> = {
  title: 'Pages/StatusPage',
  component: StatusPageComponent,
};
export default meta;

type Story = StoryObj<StatusPageComponent>;

export const Default: Story = {};
```

- [ ] **Step 4: Run Storybook**

```bash
npx nx storybook web
```

Expected: Storybook UI at `http://localhost:4400`, StatusPage story renders.

- [ ] **Step 5: Commit**

```bash
git add apps/web .storybook package.json package-lock.json
git commit -m "feat(web): add Storybook 10 for component catalog"
```

---

# Phase 12 — AI-Harness

## Task 45: Write root `CLAUDE.md` (rich)

**Files:**
- Create: `CLAUDE.md` (root)

- [ ] **Step 1: Write the CLAUDE.md**

Use the structure from spec section 7.1 — 9 sections. Substitute real content based on the spec and concept docs. Final file should be ~150-200 lines and cover:

1. What this project is (one paragraph)
2. Architecture map (ASCII diagram of Travel.Host modules + Travel.AI + Aspire connection)
3. Stack quick reference (one line per tech)
4. Module map table
5. Code conventions (namespaces, handlers, value objects, domain events, exceptions)
6. Forbidden practices (cross-module imports, business logic in Infrastructure, catch-and-swallow)
7. How to run (`aspire run`, `nx serve web`, tests)
8. AI-harness summary (5 agents + 5 commands)
9. Links (ADRs, specs, per-module CLAUDE.md, concept doc)

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add rich root CLAUDE.md"
```

---

## Task 46: Verify per-module CLAUDE.md files

These were already written in Tasks 15-19. Verify all 6 exist:

- [ ] **Step 1: Check files exist**

```bash
ls modules/*/CLAUDE.md
```
Expected: `flights/CLAUDE.md`, `hotels/CLAUDE.md`, `rail/CLAUDE.md`, `trips/CLAUDE.md`, `identity/CLAUDE.md`. Plus a `shared/CLAUDE.md` for the shared dotnet projects:

- [ ] **Step 2: Add `shared/CLAUDE.md`**

```markdown
# Shared infrastructure

**Status:** Foundation-implemented

## What lives here
- `Travel.Shared.Abstractions`       — `IDomainEvent`, `IModuleAssemblyMarker` (no package deps)
- `Travel.Shared.Domain`             — `AggregateRoot<TId>`, `Entity<TId>` (identity equality via `class`)
- `Travel.Shared.Infrastructure`     — `IInitializer` module bootstrap pattern (no AspNetCore deps)
- `Travel.Shared.Web`                — `ErrorOrExtensions` (ErrorOr → ProblemDetails). Only `apps/*` and `Modules.*.Api` may reference this.
- `Travel.Shared.TestInfrastructure` — `IntegrationTestBase` with Testcontainers PostgreSQL fixture

## Conventions
- **No business logic.** Only abstractions and infrastructure primitives.
- **No module dependencies.** Any module may depend on shared; shared may not depend on any module.
- **Value objects are plain C# `record` types.** No base class — `record` already provides structural equality. If an architecture test ever needs to enforce "VO lives in `*/ValueObjects/*` namespace", add a marker interface then; do not pre-introduce inheritance.
- **Entities use `class`, not `record`.** Identity equality (by `Id`) is fundamentally different from value equality — `record` is the wrong primitive for entities.
- **Time is always injected via `TimeProvider`.** No `DateTime.UtcNow` / `DateTimeOffset.UtcNow` calls in production code. Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
- **Result type is `ErrorOr<T>`.** Imported locally (`using ErrorOr;`), not via global using — keeps the coupling visible.
```

Place at `shared/CLAUDE.md` (one level above `dotnet/` and `ts/`).

- [ ] **Step 3: Commit**

```bash
git add shared/CLAUDE.md
git commit -m "docs: add shared/CLAUDE.md"
```

---

## Task 47: Write 5 custom agents

**Files:**
- Create: `.claude/agents/domain-modeler.md`
- Create: `.claude/agents/adr-writer.md`
- Create: `.claude/agents/test-author.md`
- Create: `.claude/agents/migration-author.md`
- Create: `.claude/agents/integration-mapper.md`

- [ ] **Step 1: Copy agent prompts from spec section 7.3**

Each agent's full system prompt is in `docs/superpowers/specs/2026-05-04-foundation-design.md` section 7.3. Copy each verbatim into the corresponding `.claude/agents/{name}.md` file. Frontmatter (`---` block with `name` and `description`) is included in the spec — do not duplicate it.

- [ ] **Step 2: Verify all 5 files exist**

```bash
ls .claude/agents/
```
Expected: 5 markdown files.

- [ ] **Step 3: Commit**

```bash
git add .claude/agents
git commit -m "feat(ai-harness): add 5 custom agents (domain-modeler, adr-writer, test-author, migration-author, integration-mapper)"
```

---

## Task 48: Write 5 slash commands

**Files:**
- Create: `.claude/commands/spec.md`
- Create: `.claude/commands/adr.md`
- Create: `.claude/commands/explore-domain.md`
- Create: `.claude/commands/test-this.md`
- Create: `.claude/commands/integration-from-openapi.md`

- [ ] **Step 1: Copy command bodies from spec section 7.4**

Each command's full body is in spec section 7.4. Copy each verbatim into `.claude/commands/{name}.md`.

- [ ] **Step 2: Verify**

```bash
ls .claude/commands/
```
Expected: 5 markdown files.

- [ ] **Step 3: Commit**

```bash
git add .claude/commands
git commit -m "feat(ai-harness): add 5 slash commands (/spec /adr /explore-domain /test-this /integration-from-openapi)"
```

---

## Task 49: Configure hooks in `.claude/settings.json`

**Files:**
- Create: `.claude/settings.json`

- [ ] **Step 1: Write `.claude/settings.json`** — copy verbatim from spec section 7.5.

- [ ] **Step 2: Verify hooks fire** — manually trigger by writing a `.cs` file via Claude Code:
  - Edit any `.cs` file via Claude.
  - Confirm CSharpier reformats it (if installed): `dotnet csharpier --version`. If not installed: `dotnet tool install -g csharpier`.

- [ ] **Step 3: Commit**

```bash
git add .claude/settings.json
git commit -m "feat(ai-harness): add Claude Code hooks (CSharpier + Biome on save, arch reminder)"
```

---

## Task 50: Add `prompts/` and `docs/blog-template.md`

**Files:**
- Create: `prompts/v1/.gitkeep` (empty placeholder so the folder is tracked)
- Create: `docs/blog-template.md`

- [ ] **Step 1: Create the prompts placeholder**

```bash
touch prompts/v1/.gitkeep
```

- [ ] **Step 2: Write `docs/blog-template.md`**

```markdown
# {Title}

**Подпроект:** {N}  
**Milestone (если применимо):** {M1 / M2 / M3}  
**Дата:** YYYY-MM-DD  
**Связанная(ые) спека:** [link]

## Что построили

[1 параграф: что в этом куске работы пользователю/читателю стало доступно — продуктовый эффект, не имплементация.]

## Ключевые архитектурные решения

[2-4 решения, на каждое — 1-2 предложения. Ссылка на ADR.]

## Самое интересное в коде

[Один-три кодовых артефакта с разбором — что необычного / поучительного, почему именно так.]

```{language}
// код
```

## AI-assisted development моменты

[Что в этом куске работы делалось с активным AI — кратко, какой агент/команда. Если есть стенограмма в `docs/ai-conversations/` — ссылка.]

## Что не работает / что отложено

[Tier 3 / out-of-scope / known issues с прицелом на будущее.]

## Ссылки

- Спек: 
- ADR (новые): 
- Концепт (если затрагивает): 
```

- [ ] **Step 3: Commit**

```bash
git add prompts docs/blog-template.md
git commit -m "docs: add prompts/v1 placeholder and blog-template.md"
```

---

# Phase 13 — Final Verification

## Task 51: Smoke test full stack end-to-end

- [ ] **Step 1: Build everything**

```bash
dotnet build Travel.sln
npm ci
npx nx run-many -t build
```
Expected: all builds succeed.

- [ ] **Step 2: Run all tests**

```bash
dotnet test Travel.sln --filter "Category=Architecture|Category=Integration"
npx nx run-many -t test
```
Expected: all tests pass.

- [ ] **Step 3: Aspire smoke test**

In one terminal: `dotnet run --project apps/Travel.AppHost`  
In another:      `npx nx serve web`

Open `http://localhost:4200/status` — confirm db: ok renders.

- [ ] **Step 4: E2E smoke**

```bash
npx nx e2e travel-e2e
```
Expected: PASS.

- [ ] **Step 5: Commit a marker (empty commit)**

```bash
git commit --allow-empty -m "chore: Foundation verified end-to-end"
```

---

## Task 52: Push and verify CI green

- [ ] **Step 1: Push branch**

```bash
git push -u origin dev
```

- [ ] **Step 2: Open GitHub repo, watch the CI workflow**

Expected: all jobs green (lint, build, test-unit, test-arch). E2E runs only on PR to master.

- [ ] **Step 3: Open a PR `dev → master`** — this triggers `test-e2e` as well.

```bash
gh pr create --title "Foundation (Subproject 0)" \
  --body "$(cat <<'EOF'
## Summary
- Full Foundation scaffold per docs/superpowers/specs/2026-05-04-foundation-design.md
- NX 22 monorepo (TS + .NET 10), Aspire AppHost with full infrastructure stack
- All 12 ADRs, AI-harness (5 agents + 5 commands + hooks), CI pipeline, devcontainer
- Vertical slice: GET /api/status (Angular → Wolverine → EF → PostgreSQL)

## Test plan
- [x] dotnet build Travel.sln green
- [x] dotnet test (unit + architecture + integration) green
- [x] aspire run brings up full stack
- [x] http://localhost:4200/status renders "db: ok"
- [x] Playwright E2E green
- [ ] CI green on PR
EOF
)"
```

- [ ] **Step 4: After CI passes — merge.**

---

## Self-Review Checklist (run after writing the plan)

This is a checklist for the plan author (you, before handing off):

- [ ] **Spec coverage:** every spec section (2-9) has at least one task implementing it.
- [ ] **No placeholders:** scan for "TODO", "TBD" in *plan tasks* (the spec itself contains "TBD" for module CLAUDE.md content — that's intentional and propagates into the skeleton files).
- [ ] **Type consistency:** `StatusResponse`, `StatusEndpoint` referenced consistently throughout (no leftover `GetStatusQuery` / `GetStatusQueryHandler` mediator-style names — WolverineFx.Http is the chosen pattern, no separate handler class needed). `HostDbContext` consistent. No leftover references to removed types (`ValueObject` base, `PostgresNamingConvention`, `Http/ErrorOrExtensions` in `Shared.Infrastructure`).
- [ ] **Path consistency:** all paths use the flattened layout (no `apps/host/`, only `apps/Travel.Host/`; no `modules/flights/core/`, only `modules/flights/Travel.Modules.Flights.Core/`).
- [ ] **TDD where it makes sense:** vertical slice (Task 35-36) uses test-first. Architecture tests (Tasks 25-27) verify pre-existing structure (test passes immediately because boundaries are correct). Pure scaffolding (Aspire setup, project creation) has no meaningful test, validated by build + manual run.

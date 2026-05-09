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
├── .github/workflows/{ci.yml, deploy.yml}
├── apps/{Travel.Host, Travel.AI, Travel.AppHost, Travel.ServiceDefaults, web}/
├── modules/{flights,hotels,rail,trips,identity}/
├── shared/{dotnet/{Abstractions,Domain,Infrastructure,TestInfrastructure},ts/{ui-kit,api-client}}/
├── infra/{docker,keycloak}/
├── docs/{adr,superpowers/{specs,plans},ai-conversations,blog-template.md}
├── prompts/v1/
├── tests/{<module>,Architecture,Contract,AiEvals,travel-e2e}/
├── CLAUDE.md, nx.json, package.json, biome.json, .editorconfig, Travel.sln
```

---

# Phase 1 — Repository Wipe & Base Configuration

## Task 1: Wipe existing source, preserve git history

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
    "version": "10.0.100",
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
    <PackageVersion Include="Roslynator.Analyzers" Version="4.13.0" />

    <!-- Aspire -->
    <PackageVersion Include="Aspire.Hosting.AppHost" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.Redis" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.NATS" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.Keycloak" Version="9.0.0" />

    <!-- Wolverine + Marten -->
    <PackageVersion Include="WolverineFx" Version="3.0.0" />
    <PackageVersion Include="WolverineFx.Marten" Version="3.0.0" />
    <PackageVersion Include="WolverineFx.Postgres" Version="3.0.0" />
    <PackageVersion Include="Marten" Version="7.30.0" />

    <!-- EF Core -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />

    <!-- Tests -->
    <PackageVersion Include="xunit.v3" Version="1.0.0" />
    <PackageVersion Include="xunit.v3.runner.visualstudio" Version="1.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
    <PackageVersion Include="Verify.Xunit" Version="26.0.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageVersion Include="ArchUnitNET.xUnitV3" Version="0.13.0" />
    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
</Project>
```

> NOTE on versions: pin to latest stable as of 2026-05. If a package version listed above does not exist when implementing, replace with the latest stable in the same major track and document in commit message.

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

## Task 5: Create `Travel.Shared.Abstractions` (Result\<T\>, marker interfaces)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Abstractions/Travel.Shared.Abstractions.csproj`
- Create: `shared/dotnet/Travel.Shared.Abstractions/Result.cs`
- Create: `shared/dotnet/Travel.Shared.Abstractions/IDomainEvent.cs`
- Create: `shared/dotnet/Travel.Shared.Abstractions/IModuleAssemblyMarker.cs`

- [ ] **Step 1: Create the `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.Abstractions --framework net10.0 --no-restore
```

Edit the generated `.csproj` to remove `<TargetFramework>` (inherited from Directory.Build.props):

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

Delete the auto-generated `Class1.cs`.

- [ ] **Step 2: Write `Result.cs`**

```csharp
namespace Travel.Shared.Abstractions;

public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static Error NotFound(string what)  => new("not_found",  $"{what} not found");
    public static Error Validation(string msg) => new("validation", msg);
    public static Error Conflict(string msg)   => new("conflict",   msg);
}

public readonly struct Result<T>
{
    public T?     Value { get; }
    public Error  Error { get; }
    public bool   IsSuccess => Error == Error.None;

    private Result(T value)        { Value = value; Error = Error.None; }
    private Result(Error error)    { Value = default; Error = error; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error e) => new(e);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error e) => Failure(e);
}
```

- [ ] **Step 3: Write `IDomainEvent.cs`**

```csharp
namespace Travel.Shared.Abstractions;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
```

- [ ] **Step 4: Write `IModuleAssemblyMarker.cs`**

```csharp
namespace Travel.Shared.Abstractions;

/// Marker interface used by module assemblies to expose themselves to scanning
/// (Wolverine handler discovery, ArchUnit boundary tests).
public interface IModuleAssemblyMarker { }
```

- [ ] **Step 5: Add to solution**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Abstractions/Travel.Shared.Abstractions.csproj
```

- [ ] **Step 6: Verify build**

```bash
dotnet build shared/dotnet/Travel.Shared.Abstractions
```
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add shared/dotnet/Travel.Shared.Abstractions Travel.sln
git commit -m "feat(shared): add Travel.Shared.Abstractions (Result, IDomainEvent)"
```

---

## Task 6: Create `Travel.Shared.Domain` (base aggregate / entity types)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Domain/Travel.Shared.Domain.csproj`
- Create: `shared/dotnet/Travel.Shared.Domain/AggregateRoot.cs`
- Create: `shared/dotnet/Travel.Shared.Domain/Entity.cs`
- Create: `shared/dotnet/Travel.Shared.Domain/ValueObject.cs`

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

- [ ] **Step 3: Write `Entity.cs`**

```csharp
namespace Travel.Shared.Domain;

public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && Id.Equals(other.Id);

    public override int GetHashCode() => Id.GetHashCode();
}
```

- [ ] **Step 4: Write `ValueObject.cs`**

```csharp
namespace Travel.Shared.Domain;

public abstract record ValueObject;
```

- [ ] **Step 5: Add to solution and build**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Domain/Travel.Shared.Domain.csproj
dotnet build shared/dotnet/Travel.Shared.Domain
```
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add shared/dotnet/Travel.Shared.Domain Travel.sln
git commit -m "feat(shared): add Travel.Shared.Domain (AggregateRoot, Entity, ValueObject)"
```

---

## Task 7: Create `Travel.Shared.Infrastructure` (REPR endpoint framework)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Travel.Shared.Infrastructure.csproj`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Endpoints/IEndpoint.cs`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Endpoints/EndpointExtensions.cs`

- [ ] **Step 1: Create `.csproj`**

```bash
dotnet new classlib -o shared/dotnet/Travel.Shared.Infrastructure --framework net10.0 --no-restore
rm shared/dotnet/Travel.Shared.Infrastructure/Class1.cs
```

Edit `Travel.Shared.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Travel.Shared.Abstractions\Travel.Shared.Abstractions.csproj" />
    <ProjectReference Include="..\Travel.Shared.Domain\Travel.Shared.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write `IEndpoint.cs`**

```csharp
using Microsoft.AspNetCore.Routing;

namespace Travel.Shared.Infrastructure.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

- [ ] **Step 3: Write `EndpointExtensions.cs`**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Travel.Shared.Infrastructure.Endpoints;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, params Assembly[] assemblies)
    {
        var endpointTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpoint).IsAssignableFrom(t));

        foreach (var type in endpointTypes)
            services.AddSingleton(typeof(IEndpoint), type);

        return services;
    }

    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.ServiceProvider.GetServices<IEndpoint>();
        foreach (var endpoint in endpoints)
            endpoint.MapEndpoint(app);
        return app;
    }
}
```

- [ ] **Step 4: Add to solution and build**

```bash
dotnet sln Travel.sln add shared/dotnet/Travel.Shared.Infrastructure/Travel.Shared.Infrastructure.csproj
dotnet build shared/dotnet/Travel.Shared.Infrastructure
```
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add shared/dotnet/Travel.Shared.Infrastructure Travel.sln
git commit -m "feat(shared): add REPR endpoint framework in Travel.Shared.Infrastructure"
```

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

- [ ] **Step 2: Edit `Travel.AppHost.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsAspireHost>true</IsAspireHost>
    <UserSecretsId>travel-apphost</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.NATS" />
    <PackageReference Include="Aspire.Hosting.Keycloak" />
  </ItemGroup>
</Project>
```

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
    <PackageReference Include="WolverineFx" />
    <PackageReference Include="WolverineFx.Postgres" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write `Program.cs`** (skeleton — full vertical slice wiring comes in Phase 9)

```csharp
using Travel.Shared.Infrastructure.Endpoints;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine();

builder.Services.AddEndpoints(typeof(Program).Assembly);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapEndpoints();

await app.RunAsync();
```

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
- [ ] **Step 4**: Write `modules/hotels/CLAUDE.md` skeleton (status: каркас, bounded context: "Поиск отелей через мульти-supplier архитектуру + один happy-path booking"; details TBD в подпроекте 2).
- [ ] **Step 5**: Add to solution, build, commit:

```bash
git commit -m "feat(hotels): add module skeleton"
```

---

## Task 17: Create `rail` module skeleton

Identical pattern. Substitute `rail` / `Rail`. CLAUDE.md says: bounded context "Read-only поиск железнодорожного транспорта (Yandex.Rasp + DB open data); без букинга", TBD в подпроекте 3.

- [ ] **Step 1-5**: Same as Task 16. Commit:

```bash
git commit -m "feat(rail): add module skeleton"
```

---

## Task 18: Create `trips` module skeleton

Identical pattern. Substitute `trips` / `Trips`. CLAUDE.md says: bounded context "Trip planning — композит над Flights/Hotels/Rail + AI-генерация маршрутов", TBD в подпроекте 4.

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
    <PackageReference Include="ArchUnitNET.xUnitV3" />
    <PackageReference Include="FluentAssertions" />

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
    <PackageReference Include="Verify.Xunit" />
    <PackageReference Include="FluentAssertions" />
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

- [ ] **Step 2: Register in `Travel.Host/Program.cs`** — add after `builder.AddServiceDefaults()`:

```csharp
builder.AddNpgsqlDbContext<HostDbContext>("travel"); // Aspire connection string name
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

## Task 35: TDD — write integration test for `GetStatusQueryHandler`

**Files:**
- Create: `tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj`
- Create: `tests/Travel.Host.Tests.Integration/GetStatusQueryHandlerTests.cs`

> Decision: GetStatusQuery + handler live directly in `Travel.Host` (not in a module — it's host scaffolding for the vertical slice). Tests get their own project `tests/Travel.Host.Tests.Integration/` next to the per-module test folders.

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
    <PackageReference Include="FluentAssertions" />
    <ProjectReference Include="..\..\apps\Travel.Host\Travel.Host.csproj" />
    <ProjectReference Include="..\..\shared\dotnet\Travel.Shared.TestInfrastructure\Travel.Shared.TestInfrastructure.csproj" />
  </ItemGroup>
</Project>
```

Add to solution:

```bash
dotnet sln Travel.sln add tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj
```

- [ ] **Step 2: Write the failing test** — `tests/Travel.Host.Tests.Integration/GetStatusQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Travel.Host.Features.Status;
using Travel.Host.Persistence;
using Travel.Shared.TestInfrastructure;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "Integration")]
public class GetStatusQueryHandlerTests : IntegrationTestBase
{
    [Fact]
    public async Task Handler_returns_postgres_version_and_db_ok()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<HostDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddSingleton<GetStatusQueryHandler>();

        await using var sp = services.BuildServiceProvider();
        var handler = sp.GetRequiredService<GetStatusQueryHandler>();

        // Act
        var response = await handler.Handle(new GetStatusQuery(), CancellationToken.None);

        // Assert
        response.Db.Should().Be("ok");
        response.Version.Should().NotBeNullOrEmpty();
        response.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 3: Run the test — expect compile failure (handler doesn't exist)**

```bash
dotnet test tests/Travel.Host.Tests.Integration --filter Category=Integration
```
Expected: FAIL with "GetStatusQuery / GetStatusQueryHandler not found".

- [ ] **Step 4: Commit (test only, before implementation)**

```bash
git add tests/Travel.Host.Tests.Integration Travel.sln
git commit -m "test(host): add failing test for GetStatusQueryHandler"
```

---

## Task 36: Implement `GetStatusQuery` + handler + endpoint

**Files:**
- Create: `apps/Travel.Host/Features/Status/GetStatusQuery.cs`
- Create: `apps/Travel.Host/Features/Status/StatusResponse.cs`
- Create: `apps/Travel.Host/Features/Status/GetStatusQueryHandler.cs`
- Create: `apps/Travel.Host/Features/Status/StatusEndpoint.cs`

- [ ] **Step 1: Write `GetStatusQuery.cs`**

```csharp
namespace Travel.Host.Features.Status;

public sealed record GetStatusQuery();
```

- [ ] **Step 2: Write `StatusResponse.cs`**

```csharp
namespace Travel.Host.Features.Status;

public sealed record StatusResponse(string Version, string Db, DateTimeOffset Timestamp);
```

- [ ] **Step 3: Write `GetStatusQueryHandler.cs`**

```csharp
using Travel.Host.Persistence;

namespace Travel.Host.Features.Status;

public sealed class GetStatusQueryHandler(HostDbContext db)
{
    public async Task<StatusResponse> Handle(GetStatusQuery _, CancellationToken ct)
    {
        var version = await db.GetServerVersionAsync(ct);
        return new StatusResponse(version, "ok", DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 4: Run the integration test**

```bash
dotnet test tests/Travel.Host.Tests.Integration --filter Category=Integration
```
Expected: PASS.

- [ ] **Step 5: Write `StatusEndpoint.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Travel.Shared.Infrastructure.Endpoints;
using Wolverine;

namespace Travel.Host.Features.Status;

public sealed class StatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", async (IMessageBus bus, CancellationToken ct) =>
        {
            var response = await bus.InvokeAsync<StatusResponse>(new GetStatusQuery(), ct);
            return Results.Ok(response);
        });
    }
}
```

- [ ] **Step 6: Verify host builds and starts**

```bash
dotnet build apps/Travel.Host
```
Expected: build succeeds.

- [ ] **Step 7: Manual smoke test via aspire**

```bash
dotnet run --project apps/Travel.AppHost
```

In the Aspire dashboard, find the `host` resource URL, then:

```bash
curl http://localhost:<host-port>/api/status
```
Expected: `{"version":"17.x","db":"ok","timestamp":"..."}`

- [ ] **Step 8: Commit**

```bash
git add apps/Travel.Host/Features
git commit -m "feat(host): add GET /api/status vertical slice (Wolverine + EF + REPR)"
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

jobs:
  setup:
    runs-on: ubuntu-latest
    outputs:
      affected-dotnet: ${{ steps.affected.outputs.dotnet }}
      affected-ts:     ${{ steps.affected.outputs.ts }}
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: npm ci
      - id: affected
        run: |
          npx nx show projects --affected --base=$NX_BASE --head=$NX_HEAD --type=app,lib > affected.txt
          cat affected.txt
          echo "dotnet=$(grep -E '(Travel\.|Travel-)' affected.txt | tr '\n' ' ')" >> $GITHUB_OUTPUT
          echo "ts=$(grep -v -E '(Travel\.)' affected.txt | tr '\n' ' ')" >> $GITHUB_OUTPUT

  lint:
    needs: setup
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: npm ci
      - run: npx biome ci .
      - run: dotnet tool install -g csharpier
      - run: dotnet csharpier --check .

  build:
    needs: setup
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
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
    needs: setup
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: npm ci
      - run: npx nx affected -t test --base=$NX_BASE --head=$NX_HEAD

  test-arch:
    needs: setup
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture --logger "trx;LogFileName=arch.trx"

  test-e2e:
    needs: [build, test-unit, test-arch]
    if: github.event_name == 'pull_request' && github.base_ref == 'master'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm' }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: npm ci
      - run: npx playwright install --with-deps chromium
      - run: dotnet run --project apps/Travel.AppHost &
      - run: sleep 30  # Give Aspire time to start
      - run: npx nx serve web &
      - run: sleep 15
      - run: npx nx e2e travel-e2e
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
        with: { dotnet-version: '10.0.x' }
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

# Phase 11 — ADR Set (14 ADRs)

> NOTE: Each ADR is its own short doc (Context / Decision / Alternatives / Consequences / Out of Scope / References). Spec section 6 lists titles and key decisions. Use the standard template in spec section 7.3 (adr-writer agent prompt) as the structural reference.

## Task 44: Write all 14 ADRs in batch

**Files:**
- Create: `docs/adr/0001-modular-monolith.md` through `0020-maf-as-primary-agent-runtime.md` (14 files, with gaps as documented in spec)

- [ ] **Step 1: Write all 14 ADRs**

For each ADR in spec section 6, create the corresponding `docs/adr/NNNN-{title}.md` file. Each ADR follows the format below — fill in the specific Context, Decision, Alternatives Considered, Consequences, Out of Scope, and References per the spec's "Ключевое решение" column and the concept doc (`docs/superpowers/specs/2026-05-03-travel-platform-concept.md`):

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

ADRs to write (titles, brief decisions — fill in Alternatives + Consequences from concept doc context):

1. **`0001-modular-monolith.md`** — Travel.Host as modular monolith, not microservices. Alts: full microservices, single monolith without modules. Consequences: split-readiness without ops cost; risk: discipline required to keep modules separated.
2. **`0002-ai-as-extracted-service.md`** — Travel.AI in separate process. Alts: AI in monolith, fully separate repo. Consequences: deploy cadence flexibility; cost: cross-process complexity.
3. **`0003-wolverine-marten-stack.md`** — Wolverine + Marten over MediatR + Dapper. Alts: MediatR + Dapper, MassTransit + EF. Consequences: same author / seamless integration; risk: smaller community than MediatR.
4. **`0004-nx-monorepo-tooling.md`** — NX 22 + `@nx/dotnet`. Alts: Cake + npm scripts, Bazel, separate FE/BE repos. Consequences: unified tooling; risk: learning curve.
5. **`0005-frontend-stack.md`** — Angular 21 + Signals + httpResource + NgRx SignalStore + Tailwind v4 + PrimeNG unstyled. Alts: React + Next.js, Vue + Nuxt. Consequences: opinionated full-stack; risk: fewer talent matches.
6. **`0006-testing-strategy.md`** — seven-layer test approach. Alts: heavy integration / no unit, BDD-only. Consequences: catch issues at the right level; cost: setup overhead per test type.
7. **`0007-marten-ef-coexistence.md`** — Marten owns `mt_*`, EF owns module schemas, same PostgreSQL. Alts: separate DBs per ORM, single ORM. Consequences: polyglot persistence demonstration; risk: schema-naming discipline required.
8. **`0010-keycloak-identity.md`** — Keycloak self-hosted. Alts: Auth0, custom IdentityServer, ASP.NET Identity. Consequences: production-grade out of box; cost: operations footprint.
9. **`0011-notifications-channels.md`** — email + SSE. Alts: WebSocket-only, polling. Consequences: lightweight server-push; cost: SSE has no client→server channel.
10. **`0012-payments-strategy.md`** — Duffel test wallet via `IPaymentGateway`. Alts: Stripe sandbox, custom gateway. Consequences: matches Duffel sandbox-only constraint; cost: production payments require Tier-3 work.
11. **`0014-storage-strategy.md`** — Marten for ES on booking lifecycle, EF for everything else. Alts: ES everything, EF everything. Consequences: ES where history is the domain; cost: developers must understand both.
12. **`0015-ui-library-selection.md`** — PrimeNG unstyled. Alts: Material, Ant Design, custom-only. Consequences: comprehensive coverage with Tailwind styling control; cost: theme tuning effort.
13. **`0019-ai-eval-strategy.md`** — own minimal eval framework in .NET. Alts: Promptfoo (now OpenAI), Arize, build-on-LangSmith. Consequences: independence from OpenAI ecosystem; cost: maintenance overhead.
14. **`0020-maf-as-primary-agent-runtime.md`** — MAF 1.0 GA primary, custom Travel Advisor only. Alts: SK directly, fully custom, fully MAF including Travel Advisor. Consequences: leverage GA framework + 1 educational artifact; cost: pin only stable MAF APIs.

- [ ] **Step 2: Commit (one commit per ADR for clean blame, or batch — your choice)**

```bash
git add docs/adr/
git commit -m "docs: add Foundation ADR set (14 ADRs covering core architecture)"
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
- `Travel.Shared.Abstractions`     — Result<T>, IDomainEvent, IModuleAssemblyMarker
- `Travel.Shared.Domain`           — AggregateRoot<TId>, Entity<TId>, ValueObject base record
- `Travel.Shared.Infrastructure`   — REPR endpoint framework (IEndpoint, MapEndpoints extensions)
- `Travel.Shared.TestInfrastructure` — IntegrationTestBase with Testcontainers PostgreSQL fixture

## Conventions
- Никакая бизнес-логика. Только абстракции и инфраструктурные примитивы.
- Не зависит ни на один модуль. Любой модуль может зависеть на shared.
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
- All 14 ADRs, AI-harness (5 agents + 5 commands + hooks), CI pipeline, devcontainer
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
- [ ] **Type consistency:** `GetStatusQuery`, `StatusResponse`, `GetStatusQueryHandler`, `StatusEndpoint` all referenced consistently. `IEndpoint` interface name matches across infrastructure project and host registration. `HostDbContext` consistent.
- [ ] **Path consistency:** all paths use the flattened layout (no `apps/host/`, only `apps/Travel.Host/`; no `modules/flights/core/`, only `modules/flights/Travel.Modules.Flights.Core/`).
- [ ] **TDD where it makes sense:** vertical slice (Task 35-36) uses test-first. Architecture tests (Tasks 25-27) verify pre-existing structure (test passes immediately because boundaries are correct). Pure scaffolding (Aspire setup, project creation) has no meaningful test, validated by build + manual run.

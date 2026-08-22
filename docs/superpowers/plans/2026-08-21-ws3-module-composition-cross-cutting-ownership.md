# WS3 Module Composition and Cross-Cutting Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Travel.Host` compose only the enabled Flights and Identity `Api` facades while each module owns its internal wiring and provider policies, and the platform owns only process-wide hosting policy.

**Architecture:** `Travel.Modules.Flights.Api.Composition.FlightsModule` and `Travel.Modules.Identity.Api.Composition.IdentityModule` become the only module entry points visible to `Travel.Host`. Host creates Marten, Wolverine, the HTTP pipeline, and endpoint mapping once; module facades contribute mappings, discovery, routes, authorization, middleware, telemetry, health, persistence, and internal adapters without creating competing global builders. The Duffel endpoint crosses into Application through a provider-neutral ingestion contract, while Infrastructure retains HMAC, supplier DTO, EF inbox/outbox, PostgreSQL deduplication, and provider resilience.

**Tech Stack:** .NET 10, ASP.NET Core, Aspire, WolverineFx 5.13, Marten 8.37, EF Core 10, Npgsql, Microsoft.Extensions.Http.Resilience/Polly, OpenTelemetry, ErrorOr, ArchUnitNET, xUnit v3, Shouldly, Alba, Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md`](../specs/2026-08-11-ai-harness-architecture-remediation-design.md), specifically D4-D6, D8 persistence parity, D11 Web baseline, WS3, data flow 4.1, the testing strategy, and acceptance criteria 4-7 and 19.

## Global Constraints

- Implement **WS3 only**. Do not introduce WS4 transition decisions, `ReconcileOrderReadModel`, projection checkpoints, rebuild commands, DLQ policy, or changes to booking/read-model consistency semantics.
- Do not pull WS5's complete all-module pair matrix, full layer matrix, README executable examples, or legacy cleanup into this change. WS3 adds only the targeted Host/facade/Shared guards needed to make its own ownership claims executable.
- Do not deploy, dispatch external CI, publish images/packages, mutate external systems, or touch production/shared data.
- Do not generate, edit, or apply EF migrations. The existing Flights migration and model snapshot must remain byte-for-byte unchanged, and the final guard rejects both tracked and untracked migration files.
- The implementation branch is `codex/ws3-module-composition`, created at `e807f7d3286e448d5de8677f85a15ed48b090cab`, the fetched `origin/dev` tip on 2026-08-21. Before implementation, re-fetch `origin/dev` and stop if this branch no longer has the user-requested fresh-base ancestry.
- `Travel.Host` may directly reference only the enabled module facades `Travel.Modules.Flights.Api` and `Travel.Modules.Identity.Api`. Hotels, Rail, and Trips remain source/test scaffolds and do not enter the runtime graph.
- Do not create a `Travel.Modules.*.Composition` assembly, a universal module framework, reflection-based module discovery, or a new `Platform.Hosting` project.
- Host creates exactly one Marten builder, one Wolverine builder, and one `MapWolverineEndpoints()` call. A module facade contributes configuration but never creates a second global builder or maps Wolverine endpoints.
- Host owns physical connection/resource names, Marten stream identity, NATS transport setup, Marten/Wolverine durability and schema-creation policy, Wolverine process-wide transaction/failure policy, global auth fallback, middleware phase order, and build/map/run.
- Flights owns its DbContext registration, module event/projection mappings, discovery/routes, `flights:book` policy, idempotency middleware, metrics/activity sources, health contributors, providers, and module initializers.
- Identity owns JWT/Keycloak options, authentication registration, idempotent claim normalization, and identity-related validation.
- The request pipeline remains `global exception handling -> authentication -> authorization -> Flights middleware -> one MapWolverineEndpoints call`.
- `Travel.Modules.Flights.Api.Endpoints`, `.Contracts`, and `.Middleware` must not depend on Infrastructure. Only `.Api.Composition` may use Infrastructure types.
- Global `ConfigureHttpClientDefaults` must not add retries. Internal clients may opt into a platform resilience profile; provider and health clients define their own explicit policies.
- `Travel.Shared.Infrastructure` remains non-web. `Travel.Shared.Web` contains HTTP result/error/identity helpers only and no module-specific or hosting/startup policy.
- Each production-code task follows RED-GREEN-REFACTOR. Characterization tests for already-correct behavior may start GREEN, but every new behavior or structural rule must be observed RED before implementation.
- Every task ends in a compiling, focused-test-green checkpoint. Do not create an intermediate commit that knowingly breaks Host or solution compilation.
- Commit commands below are review checkpoints, not authority. Run `git add`/`git commit` only after explicit commit authorization; otherwise leave the verified diff unstaged and report the checkpoint.

## Planning Baseline

- `dotnet test Travel.slnx --maxcpucount:1` exited 0 on the fresh base: 815 passed, 16 paid AI evals skipped, and empty scaffold projects reported no discoverable tests.
- `npm.cmd ci` exited 0 with no vulnerabilities; local Node 22.18.0 remains below Angular's declared 22.22.3 engine floor and is a warning, not a WS3 toolchain change.

---

### Task 1: Remove scaffold modules from the Host runtime graph

**Files:**
- Create: `tests/Travel.Tests.Architecture/Support/EvaluatedProjectReferences.cs`
- Create: `tests/Travel.Tests.Architecture/HostModuleProjectReferenceTests.cs`
- Modify: `tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs`
- Modify: `apps/Travel.Host/Travel.Host.csproj`
- Modify: `modules/hotels/Travel.Modules.Hotels.Api/Travel.Modules.Hotels.Api.csproj`
- Modify: `modules/rail/Travel.Modules.Rail.Api/Travel.Modules.Rail.Api.csproj`
- Modify: `modules/trips/Travel.Modules.Trips.Api/Travel.Modules.Trips.Api.csproj`
- Test: `tests/Travel.Tests.Architecture/HostModuleProjectReferenceTests.cs`

**Interfaces:**
- Produces: `EvaluatedProjectReferences.ForProjectAsync(projectPath, configuration)`, extracted from the existing fail-closed MSBuild evaluation in `IntegrationContractArchitectureTests`.
- Produces: no evaluated Debug/Release Host reference to Hotels, Rail, or Trips; no scaffold `Api -> Infrastructure` reference.
- Preserves temporarily: direct Flights/Identity Core/Application/Infrastructure references until Task 4 can replace their Host call sites and remove them without breaking compilation.

- [ ] **Step 1: Extract the evaluated project-reference reader with its failure tests**

Move the existing `dotnet msbuild -getItem:ProjectReference` launch, timeout, bounded-output, JSON validation, full-path normalization, and Debug/Release handling into `Support/EvaluatedProjectReferences.cs`. Keep `IntegrationContractArchitectureTests` green by consuming the shared helper. Reuse the imported-property/missing-import fixtures so conditional or imported references cannot bypass the guard.

- [ ] **Step 2: Write the failing scaffold-boundary tests**

```csharp
[Fact]
public async Task Host_does_not_reference_scaffold_modules()
{
    foreach (var configuration in new[] { "Debug", "Release" })
    {
        var references = await EvaluatedProjectReferences.ForProjectAsync(HostProject, configuration);
        references.ShouldNotContain(path =>
            path.Contains("Travel.Modules.Hotels.", StringComparison.Ordinal)
            || path.Contains("Travel.Modules.Rail.", StringComparison.Ordinal)
            || path.Contains("Travel.Modules.Trips.", StringComparison.Ordinal));
    }
}

[Theory]
[InlineData("hotels", "Hotels")]
[InlineData("rail", "Rail")]
[InlineData("trips", "Trips")]
public async Task Scaffold_Api_does_not_reference_Infrastructure(string folder, string module)
{
    var project = Project($"modules/{folder}/Travel.Modules.{module}.Api/Travel.Modules.{module}.Api.csproj");
    var references = await EvaluatedProjectReferences.ForProjectAsync(project, "Release");
    references.ShouldBeEmpty();
}
```

- [ ] **Step 3: Verify RED**

Run:

```powershell
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~HostModuleProjectReferenceTests|FullyQualifiedName~IntegrationContractArchitectureTests"
```

Expected: FAIL listing Hotels/Rail/Trips Host references and each scaffold Api's Infrastructure reference.

- [ ] **Step 4: Remove only scaffold runtime references**

Remove every Hotels/Rail/Trips project reference from `Travel.Host.csproj`. Remove Infrastructure references from the three empty scaffold Api projects. Keep Flights/Identity internal references until Task 4.

Do not remove scaffold projects from `Travel.slnx`, tests, CI inventory, or architecture-test inventory.

- [ ] **Step 5: Verify GREEN and a compiling checkpoint**

```powershell
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~HostModuleProjectReferenceTests|FullyQualifiedName~IntegrationContractArchitectureTests"
dotnet build apps/Travel.Host/Travel.Host.csproj --no-restore
```

Expected: both commands exit 0.

- [ ] **Step 6: Record the review checkpoint**

If commit authority exists:

```powershell
git add apps/Travel.Host/Travel.Host.csproj modules/hotels/Travel.Modules.Hotels.Api/Travel.Modules.Hotels.Api.csproj modules/rail/Travel.Modules.Rail.Api/Travel.Modules.Rail.Api.csproj modules/trips/Travel.Modules.Trips.Api/Travel.Modules.Trips.Api.csproj tests/Travel.Tests.Architecture
git commit -m "refactor(host): remove scaffold modules from runtime graph"
```

---

### Task 2: Establish platform Web/hosting defaults without global retry

**Files:**
- Create: `apps/Travel.ServiceDefaults/Web/PlatformExceptionHandler.cs`
- Create: `apps/Travel.ServiceDefaults/Web/PlatformProblemDetails.cs`
- Create: `apps/Travel.ServiceDefaults/Hosting/TestOnlyGuard.cs`
- Delete: `shared/dotnet/Travel.Shared.Web/TestOnlyGuard.cs`
- Create: `tests/Travel.Host.Tests.Integration/Web/PlatformWebBaselineTests.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj`
- Create: `tests/Travel.Host.Tests.Integration/Web/Verified/PlatformWebBaselineTests.OpenApi.verified.txt`
- Modify: `apps/Travel.ServiceDefaults/Extensions.cs`
- Modify: `apps/Travel.ServiceDefaults/Travel.ServiceDefaults.csproj`
- Modify: `shared/dotnet/Travel.Shared.Web/Travel.Shared.Web.csproj`
- Modify: `Directory.Packages.props`
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `apps/Travel.AI/Program.cs`
- Test: `tests/Travel.Host.Tests.Integration/Web/PlatformWebBaselineTests.cs`

**Interfaces:**
- Produces: `AddServiceDefaults()` registration for `TimeProvider.System`, ProblemDetails, OpenAPI, exception handling, OTel, health, and service discovery.
- Produces: `UsePlatformWebDefaults(WebApplication)`, which installs exception handling first and maps anonymous `/openapi/v1.json` only outside Production.
- Produces: `AddPlatformHttpResilience(IHttpClientBuilder)` as an explicit internal-client opt-in.
- Relocates: production `[TestOnly]` registration validation from `Travel.Shared.Web` to platform hosting.

- [ ] **Step 1: Write failing behavior tests**

Use minimal TestServer applications and real `IHttpClientFactory` chains. Tests must prove:

1. an unhandled exception returns RFC7807 status 500 with `traceId` and without exception text;
2. the full Development Host fallback policy still allows anonymous `/openapi/v1.json`, its normalized document matches a Verify snapshot, and Production does not map it;
3. a default client whose primary handler always returns 500 makes one attempt;
4. an `AddPlatformHttpResilience()` client makes the configured bounded attempt count;
5. `TimeProvider` resolves once from platform defaults and can be overridden in tests;
6. `TestOnlyGuard` throws in Production and is absent from the `Travel.Shared.Web` assembly.

Use a counting `HttpMessageHandler`; do not infer resilience registration from service-descriptor type names.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~PlatformWebBaselineTests"
```

Expected: FAIL because global defaults currently retry every client, no platform exception/OpenAPI baseline exists, and hosting policy still lives in Shared.Web.

- [ ] **Step 3: Implement platform ownership**

In `AddServiceDefaults()`:

```csharp
builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddProblemDetails(PlatformProblemDetails.Configure);
builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpClientDefaults(client => client.AddServiceDiscovery());
```

Add the explicit helper:

```csharp
public static IHttpClientBuilder AddPlatformHttpResilience(this IHttpClientBuilder client) =>
    client.AddStandardResilienceHandler();
```

`UsePlatformWebDefaults()` calls `UseExceptionHandler()` and maps OpenAPI with `.AllowAnonymous()` only when `!app.Environment.IsProduction()`. Pin/reference `Microsoft.AspNetCore.OpenApi` version `10.0.0`, add `Verify.XunitV3` to Host integration tests, and normalize nondeterministic server/trace fields before snapshot verification.

Move `TestOnlyGuard` to `Travel.ServiceDefaults.Hosting`. Add ServiceDefaults references to `Travel.Shared.Abstractions`; remove the now-unused Shared.Abstractions reference from Shared.Web.

- [ ] **Step 4: Update both processes without changing their runtime contracts**

Call `app.UsePlatformWebDefaults()` immediately after each `builder.Build()`. Remove duplicate Host/AI `TimeProvider.System` registrations. Update Host's guard import/call to the platform namespace. Preserve internal health listeners, startup validators, AI transport/model/ledger, and all current endpoint mappings.

- [ ] **Step 5: Verify GREEN**

```powershell
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~PlatformWebBaselineTests|FullyQualifiedName~HealthEndpointContractTests"
dotnet build apps/Travel.Host/Travel.Host.csproj --no-restore
dotnet build apps/Travel.AI/Travel.AI.csproj --no-restore
```

Expected: all commands exit 0.

- [ ] **Step 6: Record the review checkpoint**

If commit authority exists:

```powershell
git add Directory.Packages.props apps/Travel.ServiceDefaults apps/Travel.Host/Program.cs apps/Travel.AI/Program.cs shared/dotnet/Travel.Shared.Web tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj tests/Travel.Host.Tests.Integration/Web
git commit -m "feat(platform): standardize web and hosting defaults"
```

---

### Task 3: Move Identity composition and idempotent claim normalization behind `Identity.Api`

**Files:**
- Create: `modules/identity/Travel.Modules.Identity.Api/Composition/IdentityModule.cs`
- Create: `modules/identity/Travel.Modules.Identity.Infrastructure/Authentication/NormalizedIdentityClaimsTransformation.cs`
- Create: `modules/identity/Travel.Modules.Identity.Infrastructure/Properties/AssemblyInfo.cs`
- Modify: `modules/identity/Travel.Modules.Identity.Infrastructure/IdentityServiceCollectionExtensions.cs`
- Modify: `modules/identity/Travel.Modules.Identity.Api/Travel.Modules.Identity.Api.csproj`
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `tests/identity/Travel.Modules.Identity.Tests.Unit/Travel.Modules.Identity.Tests.Unit.csproj`
- Modify: `tests/identity/Travel.Modules.Identity.Tests.Unit/Configuration/KeycloakOptionsTests.cs`
- Create: `tests/identity/Travel.Modules.Identity.Tests.Unit/Authentication/NormalizedIdentityClaimsTransformationTests.cs`
- Test: the two Identity unit test files above.

**Interfaces:**
- Produces: `public static IHostApplicationBuilder AddIdentityModule(this IHostApplicationBuilder builder)` in `Travel.Modules.Identity.Api.Composition`.
- Internalizes: `IServiceCollection AddIdentityInfrastructure(IConfiguration, IHostEnvironment)` for the Api friend assembly; this matches `IHostApplicationBuilder.Environment`.
- Normalizes: a valid `sub` into `ClaimTypes.NameIdentifier` only when no conflicting identifier exists; `scope`/`scp` space-delimited values into distinct canonical `scope` claims.

- [ ] **Step 1: Write failing facade and normalization tests**

Update options tests to call the Api facade through a `HostApplicationBuilder`. Cover:

- valid `sub` becomes one canonical NameIdentifier;
- an existing equal NameIdentifier is not duplicated;
- repeated `TransformAsync` calls are idempotent;
- malformed, empty, or `Guid.Empty` identifiers do not create canonical identity;
- conflicting valid `sub` and NameIdentifier remain ambiguous and therefore fail closed in Task 7;
- `scope` and `scp` values are split, trimmed, deduplicated, and idempotent.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests/identity/Travel.Modules.Identity.Tests.Unit/Travel.Modules.Identity.Tests.Unit.csproj
```

Expected: compilation/test failure because the Api facade and transformation do not exist.

- [ ] **Step 3: Implement the facade and transformation**

```csharp
public static class IdentityModule
{
    public static IHostApplicationBuilder AddIdentityModule(this IHostApplicationBuilder builder)
    {
        builder.Services.AddIdentityInfrastructure(builder.Configuration, builder.Environment);
        return builder;
    }
}
```

Rename the Infrastructure extension to `AddIdentityInfrastructure(IConfiguration, IHostEnvironment)`, make it internal, friend only `Travel.Modules.Identity.Api`, and register the transformation. Switch Host immediately from the old Infrastructure extension to `builder.AddIdentityModule()`; keep the temporary Identity.Infrastructure project reference only until Task 4 removes all remaining internal references. Keep Keycloak validation and JwtBearer setup module-owned.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/identity/Travel.Modules.Identity.Tests.Unit/Travel.Modules.Identity.Tests.Unit.csproj
dotnet build apps/Travel.Host/Travel.Host.csproj --no-restore
```

Expected: options/normalization tests pass and Host compiles through the Identity.Api facade.

- [ ] **Step 5: Record the review checkpoint**

If commit authority exists:

```powershell
git add modules/identity apps/Travel.Host/Program.cs tests/identity
git commit -m "refactor(identity): expose api composition facade"
```

---

### Task 4: Create the Flights facade, unify persistence configuration, and preserve Host process policy

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Properties/AssemblyInfo.cs`
- Rename: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsModuleServiceCollectionExtensions.cs` -> `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Marten/BookingAggregateConfig.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContextConfiguration.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContextFactory.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Travel.Modules.Flights.Api.csproj`
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `apps/Travel.Host/Travel.Host.csproj`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsModuleRegistrationTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsOptionsValidationTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Providers/Duffel/DuffelClientResilienceTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Persistence/FlightsDbContextTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Marten/BookingAggregateMartenTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/QuoteOfferHandlerTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/OrderReadModelProjectorTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/HoldOfferHandlerTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/ConfirmOrderOutboxTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/ConfirmOrderHandlerTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/CancelOrderHandlerTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingConcurrencyTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookHandlerTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Outbox/WolverineOutboxFixture.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Initialization/DatabaseInitializationTests.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Flights/FlightsModuleWiringTests.cs`
- Create: `tests/Travel.Tests.Architecture/HostModuleTypeDependencyTests.cs`
- Create: `tests/Travel.Tests.Architecture/CompositionCallSiteTests.cs`
- Modify: `tests/Travel.Tests.Architecture/ArchitectureTestBase.cs`
- Modify: `tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj`
- Modify: `tests/Travel.Tests.Architecture/HostModuleProjectReferenceTests.cs`
- Modify: `tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs`
- Test: the unit, Host integration, and architecture files above.

**Public facade:**

```csharp
public static IHostApplicationBuilder AddFlightsModule(this IHostApplicationBuilder builder);
public static void ConfigureMarten(StoreOptions options);
public static void ConfigureWolverine(WolverineOptions options);
public static WebApplication UseFlightsModule(this WebApplication app);
```

**Internal contracts:**
- `AddFlightsInfrastructure(IServiceCollection, IConfiguration, IHostEnvironment)`;
- `FlightsDbContextConfiguration.Configure(DbContextOptionsBuilder)`;
- module-only Marten event/projection mapping helper.
Only `Travel.Modules.Flights.Api` is a friend of Flights Infrastructure. Every current direct test consumer found by `rg -n "FlightsDbContextConfiguration|ConfigureFlightsBooking\("` is listed above and must switch to the public facade or reflection-based design-time factory activation; do not add a test friend grant.

- [ ] **Step 1: Write characterization and failing ownership tests**

Add tests that first characterize current runtime/design-time EF model/schema/history parity. Then add RED structural/behavior tests:

1. evaluated Debug/Release Host module references equal only Flights.Api and Identity.Api;
2. the Host assembly is loaded and contains at least one class; every class that resides in `Travel.Host.dll`—including global-namespace `Program`—has no type dependency on module Core/Application/Infrastructure;
3. `ConfigureMarten` leaves a preselected `StreamIdentity.AsString` unchanged while still registering all Flights events/projection;
4. Host source has exactly one production invocation each of `AddMarten(`, `UseWolverine(`, and `MapWolverineEndpoints(`; module Api sources have none of those global builder/mapping calls;
5. runtime-style facade DbContext options and the actual design-time factory produce identical provider, snake-case model mapping, and `flights.__ef_migrations_history`;
6. registration/options/initializer tests call the new facade rather than the old Infrastructure extension.

Use `Classes().That().ResideInAssembly(hostAssembly)` and assert the selector is non-empty. Do not use `ResideInNamespaceMatching(...|Program)` or `WithoutRequiringPositiveResults()`.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~HostModule|FullyQualifiedName~CompositionCallSite"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~FlightsModuleRegistrationTests|FullyQualifiedName~FlightsOptionsValidationTests"
```

Expected: facade compilation is missing; Host evaluated references/imports violate the boundary; stream identity is still changed by the module helper.

- [ ] **Step 3: Internalize module helpers and unify EF configuration**

Rename the registration helper to internal `AddFlightsInfrastructure`. Make DbContext/Marten configuration helpers internal and friend only Flights.Api.

`FlightsDbContextConfiguration.Configure` applies the existing Npgsql migrations history and snake-case naming to an already selected provider. The design-time factory selects its connection string, then calls that function. The facade's Aspire callback calls the same function. Remove all other production occurrences of `UseSnakeCaseNamingConvention` and `MigrationsHistoryTable` for Flights; the architecture test must enforce this single source.

Remove `opts.Events.StreamIdentity = StreamIdentity.AsGuid` from `BookingAggregateConfig`; stream identity moves to Host.

- [ ] **Step 4: Add exact Flights.Api dependencies**

Keep the FrameworkReference and add direct project references to:

- `Travel.Modules.Flights.Infrastructure`;
- `Travel.Shared.Web`;
- `Travel.IntegrationContracts.AI`.

Add direct package references required by composition code:

- `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`;
- `Marten`;
- `OpenTelemetry.Extensions.Hosting`;
- `WolverineFx`;
- `WolverineFx.Http`;
- `WolverineFx.Nats`.

Keep `WolverineFx.EntityFrameworkCore` only until Task 5 removes the endpoint's EF outbox dependency.

- [ ] **Step 5: Implement `FlightsModule`**

`AddFlightsModule` registers FlightsDbContext through Aspire with `DisableRetry = true`, calls `AddFlightsInfrastructure`, contributes Flights meter/activity source, adds a `flights:book` policy that consumes the canonical normalized `scope` claim, and registers Wolverine HTTP services.

`ConfigureMarten` contributes only module events/projections. `ConfigureWolverine` contributes only Flights Application/Infrastructure/Api discovery and the NL-search NATS route. `UseFlightsModule` adds only `IdempotencyKeyMiddleware`.

- [ ] **Step 6: Preserve the complete Host process composition**

The resulting Host must retain all of the following, with only module internals removed:

```csharp
builder.AddServiceDefaults();
builder.AddInternalHealthEndpoints(5098);
builder.Services.AddHostConnectionOptions(builder.Configuration, builder.Environment);
builder.AddNpgsqlDbContext<HostDbContext>(/* existing DisableRetry + naming */);
builder.Services.AddRequiredTcpDependencyHealthCheck(/* postgres, nats, redis */);

builder.AddIdentityModule();
builder.AddFlightsModule();

builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("travel")!);
    options.AutoCreateSchemaObjects = AutoCreate.None;
    options.Events.StreamIdentity = StreamIdentity.AsGuid;
    FlightsModule.ConfigureMarten(options);
})
.UseLightweightSessions()
.IntegrateWithWolverine(integration => integration.AutoCreate = AutoCreate.None);

builder.Services.AddInitializer<WolverineMessageStoreInitializer>();
builder.Services.AddAppInitialization();

builder.Host.UseWolverine(options =>
{
    options.ApplicationAssembly = typeof(Program).Assembly;
    options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
    options.UseNats(natsUrl);
    options.Policies.AutoApplyTransactions();
    options.Policies.UseDurableLocalQueues();
    options.UseEntityFrameworkCoreTransactions();
    FlightsModule.ConfigureWolverine(options);
});
```

Keep the global authentication fallback, platform `TestOnlyGuard`, Health/Host option resolution, `IStartupValidator.Validate()`, and all existing endpoint/health exposure. The post-build order is:

```csharp
app.UsePlatformWebDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.UseFlightsModule();
app.MapDefaultEndpoints();
app.MapWolverineEndpoints();
```

- [ ] **Step 7: Remove final Host internal references and update every registration consumer**

Remove Flights/Identity Core/Application/Infrastructure project references from Host only after Program uses both Api facades. Update all old `AddFlightsModule(IServiceCollection,...)` consumers found by:

```powershell
rg -n "AddFlightsModule\(" apps tests modules --glob "*.cs"
```

The only production registration call is the Api facade. Update options tests, module registration tests, DatabaseInitializationTests, Duffel resilience setup, every booking/Marten/webhook/outbox fixture, and FlightsDbContextTests in this same checkpoint.

After edits run both inventories:

```powershell
rg -n "AddFlightsModule\(" apps modules --glob "*.cs"
rg -n "FlightsDbContextConfiguration|ConfigureFlightsBooking\(" apps modules tests --glob "*.cs"
```

Expected: the first command finds only the public facade declaration and Host call. The second finds no Host/test dependency on internal helpers; production matches are limited to internal declarations, their Infrastructure/factory implementation, and calls from Flights.Api.Composition.

Update the integration-contract evaluated allowlist to include Flights.Api as specified by D7.

- [ ] **Step 8: Verify GREEN and policy preservation**

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~Composition"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~FlightsModuleWiringTests|FullyQualifiedName~DatabaseInitializationTests|FullyQualifiedName~HealthEndpointContractTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~HostModule|FullyQualifiedName~CompositionCallSite|FullyQualifiedName~IntegrationContractArchitectureTests"
dotnet build apps/Travel.Host/Travel.Host.csproj --no-restore
```

Expected: all commands exit 0; Host has only two module Api references; Marten/Wolverine safety settings and initializer/health/startup gates remain active.

- [ ] **Step 9: Verify migration immutability**

```powershell
git diff --exit-code origin/dev -- modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Migrations
if (git ls-files --others --exclude-standard -- modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Migrations) { throw "Untracked migration file detected." }
```

- [ ] **Step 10: Record the review checkpoint**

If commit authority exists:

```powershell
git add apps/Travel.Host modules/flights/Travel.Modules.Flights.Api modules/flights/Travel.Modules.Flights.Infrastructure tests/flights/Travel.Modules.Flights.Tests.Unit/Composition tests/flights/Travel.Modules.Flights.Tests.Integration tests/Travel.Host.Tests.Integration tests/Travel.Tests.Architecture
git commit -m "refactor(host): compose flights through api facade"
```

---

### Task 5: Move Duffel webhook ingestion behind an Application port

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Application/Webhooks/WebhookIngestionRequest.cs`
- Create: `modules/flights/Travel.Modules.Flights.Application/Webhooks/WebhookIngestionOutcome.cs`
- Create: `modules/flights/Travel.Modules.Flights.Application/Webhooks/WebhookIngestionErrors.cs`
- Create: `modules/flights/Travel.Modules.Flights.Application/Webhooks/IWebhookIngestionPort.cs`
- Create: `modules/flights/Travel.Modules.Flights.Application/Webhooks/IWebhookIngestionService.cs`
- Create: `modules/flights/Travel.Modules.Flights.Application/Webhooks/WebhookIngestionService.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Webhooks/DuffelWebhookIngestionPort.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Endpoints/DuffelWebhookEndpoint.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Travel.Modules.Flights.Api.csproj`
- Create: `tests/flights/Travel.Modules.Flights.Tests.Unit/Webhooks/WebhookIngestionServiceTests.cs`
- Rewrite: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookEndpointTests.cs`
- Create: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookIngestionPortTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookEndpointOutboxTests.cs`
- Create: `tests/Travel.Tests.Architecture/ApiCompositionBoundaryTests.cs`
- Test: all four webhook/architecture test groups above.

**Interfaces:**

```csharp
public sealed record WebhookIngestionRequest(
    string Provider,
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, string> Headers);

public enum WebhookIngestionOutcome { Accepted, Duplicate }

public interface IWebhookIngestionPort
{
    Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
        WebhookIngestionRequest request,
        CancellationToken ct);
}

public interface IWebhookIngestionService
{
    Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
        WebhookIngestionRequest request,
        CancellationToken ct);
}
```

Infrastructure returns typed Application errors `Flights.Webhook.InvalidSignature` and `Flights.Webhook.InvalidPayload`; the service rejects unsupported providers before calling the port. The endpoint maps success/duplicate to 200 and typed errors through the shared RFC7807 mapping.

- [ ] **Step 1: Write RED Application and endpoint tests**

Use a recording fake port/service. Prove:

- provider-neutral payload bytes are unchanged;
- header snapshot uses `StringComparer.OrdinalIgnoreCase`;
- lowercase/mixed-case `X-Duffel-Signature` works;
- multiple signature values have an explicit reject contract;
- unsupported provider, invalid signature, and invalid JSON become typed errors;
- endpoint responses are RFC7807 for errors and contain no provider DTO/EF details.

- [ ] **Step 2: Write the RED Api boundary rule**

Use positive selectors for `Endpoints`, `Contracts`, and `Middleware`; each must have at least one selected type and no dependency on `Travel.Modules.Flights.Infrastructure.*`. Only `Travel.Modules.Flights.Api.Composition` is allowlisted.

- [ ] **Step 3: Verify RED**

```powershell
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~ApiCompositionBoundaryTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~WebhookIngestionServiceTests"
```

Expected: endpoint Infrastructure dependencies are reported and Application contracts are missing.

- [ ] **Step 4: Implement Application orchestration and the real Infrastructure port**

Move HMAC verification, Duffel DTO parsing, EF pre-check/insert, PostgreSQL 23505 handling, `IDbContextOutbox<FlightsDbContext>` publish/flush, metrics, timestamps, and provider logging into `DuffelWebhookIngestionPort`. Register port/service in Flights composition.

Do not change `DuffelWebhookHandler`, booking guards, projection calls, or domain events; those are WS4-owned semantics.

- [ ] **Step 5: Reduce the endpoint to HTTP transport mapping**

The endpoint reads raw bytes, snapshots headers into an ordinal-ignore-case dictionary, creates `WebhookIngestionRequest("duffel", ...)`, invokes `IWebhookIngestionService`, and maps the result. It contains no EF, Npgsql, provider DTO/verifier, DbContext/entity, or Wolverine EntityFrameworkCore imports.

Remove `WolverineFx.EntityFrameworkCore` from Flights.Api after verifying no other Api source uses it.

- [ ] **Step 6: Tie atomicity proof to the real port**

`DuffelWebhookIngestionPortTests` resolves the real port from a PostgreSQL/Wolverine host and proves:

1. signed new delivery inserts one inbox row and delivers one `ProcessDuffelWebhookCommand`;
2. concurrent duplicate deliveries return success, persist one row, and publish exactly once;
3. a non-unique EF failure injected with a test `SaveChangesInterceptor` after publish causes the real port to throw and leaves neither inbox nor outgoing outbox row.

Keep primitive outbox tests, but do not cite probe-only tests as proof of the new port.

- [ ] **Step 7: Verify GREEN**

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~WebhookIngestionServiceTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~DuffelWebhookEndpoint|FullyQualifiedName~DuffelWebhookIngestionPort|FullyQualifiedName~EfWolverineOutboxTests|FullyQualifiedName~OutboxCrashSafetyTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~ApiCompositionBoundaryTests"
```

Expected: all commands exit 0 and migrations remain unchanged.

- [ ] **Step 8: Record the review checkpoint**

If commit authority exists:

```powershell
git add modules/flights/Travel.Modules.Flights.Application/Webhooks modules/flights/Travel.Modules.Flights.Infrastructure/Webhooks modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs modules/flights/Travel.Modules.Flights.Api tests/flights/Travel.Modules.Flights.Tests.Unit/Webhooks tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks tests/Travel.Tests.Architecture/ApiCompositionBoundaryTests.cs
git commit -m "refactor(flights): hide webhook persistence behind application port"
```

---

### Task 6: Make every external HTTP policy and telemetry redaction owner explicit

**Files:**
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Telemetry/IHttpUrlRedactionContributor.cs`
- Create: `apps/Travel.ServiceDefaults/Telemetry/HttpUrlRedactionOptionsConfigurator.cs`
- Create: `apps/Travel.ServiceDefaults/Telemetry/HttpUrlRedactor.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Observability/TravelpayoutsUrlRedactionContributor.cs`
- Modify: `apps/Travel.ServiceDefaults/Extensions.cs`
- Modify: `apps/Travel.ServiceDefaults/Travel.ServiceDefaults.csproj`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Keycloak/KeycloakAdminOptions.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsOptionsValidationTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Providers/Duffel/DuffelClientResilienceTests.cs`
- Create: `tests/Travel.Host.Tests.Integration/Flights/FlightsHttpClientCompositionTests.cs`
- Create: `tests/Travel.Host.Tests.Integration/Observability/HttpUrlRedactionTests.cs`
- Create: `tests/Travel.Tests.Architecture/HttpResilienceOwnershipTests.cs`
- Test: the four test files above.

**Interfaces:**
- Produces: non-web shared `IHttpUrlRedactionContributor.SensitiveQueryParameterNames`.
- Produces: an internal platform OTel options configurator combining contributors without provider names in ServiceDefaults.
- Flights contributes Travelpayouts `token`.
- Provider contracts remain: Duffel 3 retries + timeout + circuit breaker; Travelpayouts 3 retries + timeout + circuit breaker; Frankfurter 2 retries + timeout; configured Keycloak token/admin explicit retry + timeout + circuit breaker; disabled/absent Keycloak remains valid; health clients make one bounded attempt.

- [ ] **Step 1: Write full Host-graph behavior tests**

Boot production-equivalent platform defaults plus Flights facade. Replace primary handlers with deterministic counting handlers and assert exact attempt counts for:

- `DuffelClient`;
- `TravelpayoutsClient`;
- `FrankfurterClient`;
- Keycloak token named client;
- Keycloak admin named client;
- `duffel-health`;
- `travelpayouts-health`.

Health clients must make exactly one attempt. Add options tests proving completely absent Keycloak Admin configuration still validates and registers no failing client behavior. This test lives in Host integration so it uses real `AddServiceDefaults()`, not a reconstructed global registration.

- [ ] **Step 2: Write the RED ownership and OTel tests**

Add a source/architecture guard that fails while `RemoveAllResilienceHandlers` or `ReplaceGlobalResilience` exists in production code and permits `AddStandardResilienceHandler` only inside `AddPlatformHttpResilience`.

Capture a real outbound HTTP `Activity` from the registered OTel instrumentation. Assert `url.full` redacts configured query keys case-insensitively, never includes the secret, retains permitted non-sensitive values, and composes multiple contributors. Test the registered behavior rather than making `HttpUrlRedactor` public.

- [ ] **Step 3: Verify RED**

```powershell
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~FlightsHttpClientCompositionTests|FullyQualifiedName~HttpUrlRedactionTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~HttpResilienceOwnershipTests"
```

Expected: Keycloak lacks a policy, ServiceDefaults hard-codes `token`, and obsolete global-suppression calls violate the ownership guard. Existing provider attempt counts are characterization assertions and may already pass.

- [ ] **Step 4: Implement provider-owned policies**

Delete `ReplaceGlobalResilience` and every `RemoveAllResilienceHandlers` call. Keep existing Duffel/Travelpayouts/Frankfurter pipelines. Give `KeycloakAdminOptions.TimeoutSeconds` a positive default and validate it only when Keycloak Admin is configured; add explicit pipelines to both configured Keycloak named clients. Keep health clients retry-free with explicit timeouts.

- [ ] **Step 5: Implement configurable redaction**

Add a ServiceDefaults project reference to `Travel.Shared.Infrastructure`. `HttpUrlRedactionOptionsConfigurator` receives all contributors and configures `HttpClientTraceInstrumentationOptions`; `HttpUrlRedactor` remains internal. Flights registers `TravelpayoutsUrlRedactionContributor`. No provider-specific query name remains in ServiceDefaults.

- [ ] **Step 6: Verify GREEN**

```powershell
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~FlightsHttpClientCompositionTests|FullyQualifiedName~HttpUrlRedactionTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~DuffelClientResilienceTests|FullyQualifiedName~FlightsHealthCheckTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~FlightsOptionsValidationTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~HttpResilienceOwnershipTests"
```

Expected: all commands exit 0 and the production source guard finds no global-suppression API.

- [ ] **Step 7: Record the review checkpoint**

If commit authority exists:

```powershell
git add shared/dotnet/Travel.Shared.Infrastructure apps/Travel.ServiceDefaults modules/flights/Travel.Modules.Flights.Infrastructure tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsOptionsValidationTests.cs tests/Travel.Host.Tests.Integration/Flights/FlightsHttpClientCompositionTests.cs tests/Travel.Host.Tests.Integration/Observability tests/Travel.Tests.Architecture/HttpResilienceOwnershipTests.cs
git commit -m "refactor(http): assign resilience and redaction ownership"
```

---

### Task 7: Fail closed on identity, unify expected ProblemDetails, and enforce Shared charters

**Files:**
- Modify: `shared/dotnet/Travel.Shared.Web/ClaimsPrincipalExtensions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Middleware/IdempotencyKeyMiddleware.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Endpoints/CancelOrderEndpoint.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Endpoints/ConfirmOrderEndpoint.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Endpoints/GetOrderEndpoint.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Endpoints/ListOrdersEndpoint.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Api/Endpoints/OrderEventsSseEndpoint.cs`
- Create: `tests/flights/Travel.Modules.Flights.Tests.Unit/Web/ClaimsPrincipalExtensionsTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Idempotency/IdempotencyKeyMiddlewareTests.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Flights/FlightsApiFixture.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Flights/FlightsEndpointsHttpTests.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Web/PlatformWebBaselineTests.cs`
- Create: `tests/Travel.Tests.Architecture/SharedCharterTests.cs`
- Test: all files above.

**Interfaces:**
- Replaces: `Guid GetUserId(ClaimsPrincipal)` returning `Guid.Empty`.
- Produces: `bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)`.
- Fail-closed rules: every NameIdentifier/`sub` value must parse to one identical non-empty GUID; missing, malformed, empty, or conflicting values return false.
- Expected HTTP failures use RFC7807 with the same trace-id customization as unhandled failures.

- [ ] **Step 1: Write RED identity and middleware-order tests**

Cover valid mapped/unmapped identity, repeated equal claims, malformed values, `Guid.Empty`, and conflicting valid IDs. Add HTTP tests proving:

- malformed authenticated identity returns RFC7807 401;
- authentication/authorization reject anonymous or invalid callers before idempotency storage and message dispatch;
- SSE does not query the registry for invalid identity;
- valid canonical scopes still authorize `flights:book`.

Update the lean fixture to register the same Identity claims transformation or an exact test equivalent; do not rely on a test-only identity shape that Production never emits.

- [ ] **Step 2: Write RED expected-error ProblemDetails tests**

Update `IdempotencyKeyMiddlewareTests` and platform baseline tests so missing key (400), in-flight/conflict (409), invalid identity (401), and a representative `ErrorOr` endpoint failure all assert:

- `application/problem+json`;
- RFC7807 `status`, `title`, `type`, and `detail`;
- `traceId`;
- structured `errors` where applicable;
- no exception, secret, or PII.

This replaces the middleware's current ad-hoc anonymous JSON objects.

- [ ] **Step 3: Write targeted Shared charter tests**

Use `EvaluatedProjectReferences` across Debug/Release and IL references to assert:

- no `Travel.Shared.*` project references a module;
- Shared.Infrastructure has no ASP.NET Core framework/type dependency;
- Shared.Web has no module, ServiceDefaults, hosting/startup, or `TestOnlyGuard` dependency;
- all selectors for the currently populated Shared projects are non-empty.

Do not expand this task into WS5's complete all-module pair matrix.

- [ ] **Step 4: Verify RED**

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~ClaimsPrincipalExtensionsTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~IdempotencyKeyMiddlewareTests"
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~FlightsEndpointsHttpTests|FullyQualifiedName~PlatformWebBaselineTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~SharedCharterTests"
```

Expected: identity tests expose `Guid.Empty`; middleware ProblemDetails tests expose ad-hoc JSON. Shared charter tests should become GREEN from Task 2's guard relocation; if already GREEN, treat them as characterization/enforcement rather than inventing a violation.

- [ ] **Step 5: Implement fail-closed consumers and RFC7807 middleware**

Implement `TryGetUserId` with the explicit ambiguity rules. Replace all `GetUserId()` consumers. Endpoints and SSE stop before Application/registry calls on false. Middleware stops before store/next on false.

Use `ErrorOrExtensions.ToProblemDetails()` plus the registered `IProblemDetailsService`/Problem result execution for idempotency errors; preserve existing error codes and HTTP statuses.

- [ ] **Step 6: Verify GREEN**

Run the four focused commands again, then:

```powershell
rg -n "GetUserId\(|Guid\.Empty|WriteAsJsonAsync\(new" modules/flights/Travel.Modules.Flights.Api shared/dotnet/Travel.Shared.Web
```

Expected: tests pass and no empty-GUID or ad-hoc expected-error response remains in the targeted Web/API path.

- [ ] **Step 7: Record the review checkpoint**

If commit authority exists:

```powershell
git add shared/dotnet/Travel.Shared.Web modules/flights/Travel.Modules.Flights.Api tests/flights/Travel.Modules.Flights.Tests.Unit/Web tests/flights/Travel.Modules.Flights.Tests.Integration/Idempotency tests/Travel.Host.Tests.Integration/Flights tests/Travel.Host.Tests.Integration/Web tests/Travel.Tests.Architecture/SharedCharterTests.cs
git commit -m "fix(web): fail closed on identity and standardize errors"
```

---

### Task 8: Record the ownership decision and run the WS3 gate

**Files:**
- Create: `docs/adr/0023-module-api-facades-and-cross-cutting-ownership.md`
- Modify: `docs/adr/0001-modular-monolith.md`
- Modify: `docs/adr/0009-http-endpoints-wolverine.md`
- Modify: `docs/adr/0018-duffel-webhook-inbox-outbox.md`
- Test: all changed test projects and repository gates.

**Interfaces:**
- Produces: accepted ADR 0023 defining module facade, process builder, middleware order, cross-cutting owner table, resilience owner, webhook boundary, and the condition for a future separate composition assembly.
- Preserves historical ADR context; amendments point forward rather than rewriting May 2026 decisions.

- [ ] **Step 1: Invoke the repository `adr` skill and draft ADR 0023**

ADR 0023 states:

- `Api.Composition` is the sole Host-visible module surface;
- Host creates global Marten/Wolverine builders and maps endpoints once;
- only enabled modules are in the runtime graph;
- `Api.Endpoints/Contracts/Middleware` cannot use Infrastructure;
- platform/Host/Identity/Flights/integration-contract/Shared ownership matches D5;
- provider resilience is adapter-owned and platform retry is opt-in;
- a separate composition assembly requires a new non-HTTP/multi-host need and ADR;
- webhook atomicity remains ADR 0018 behavior while EF/provider implementation is hidden behind the Application port.

- [ ] **Step 2: Add narrow amendment pointers**

Add dated notes to ADR 0001, ADR 0009, and ADR 0018 pointing to ADR 0023. Do not rewrite historical alternatives or imply the facade existed in May 2026.

- [ ] **Step 3: Run formatting and focused gates**

```powershell
dotnet csharpier format .
dotnet csharpier check .
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj
dotnet test tests/identity/Travel.Modules.Identity.Tests.Unit/Travel.Modules.Identity.Tests.Unit.csproj
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj
```

Expected: all commands exit 0. Docker-dependent failures remain explicitly unverified; do not replace them with mocks and claim integration proof.

- [ ] **Step 4: Run the full source-ready gate**

```powershell
dotnet build Travel.slnx --no-restore
dotnet test Travel.slnx --maxcpucount:1 --no-restore
npm.cmd run check:dotnet-inventory
npm.cmd run check:ai-harness
git diff --check
git diff --exit-code origin/dev -- modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Migrations
if (git ls-files --others --exclude-standard -- modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Migrations) { throw "Untracked migration file detected." }
```

Expected: build/tests/inventory/harness/diff guards exit 0; paid AI evals may remain skipped by their existing contract; no tracked or untracked migration exists.

- [ ] **Step 5: Audit the final ownership boundary**

```powershell
rg -n "Travel\.Modules\..*\.(Core|Application|Infrastructure)" apps/Travel.Host
rg -n "AddMarten\(|UseWolverine\(|MapWolverineEndpoints\(" apps/Travel.Host modules/flights/Travel.Modules.Flights.Api modules/identity/Travel.Modules.Identity.Api --glob "*.cs"
rg -n "Travel\.Modules\.Flights\.Infrastructure" modules/flights/Travel.Modules.Flights.Api/Endpoints modules/flights/Travel.Modules.Flights.Api/Contracts modules/flights/Travel.Modules.Flights.Api/Middleware --glob "*.cs"
git status --short
```

Expected:

- no Host module-internal import/type dependency;
- exactly one global builder/mapping call site, in Host;
- no non-composition Api dependency on Infrastructure;
- only intended source/docs/tests changes, with no migration, deployment, generated secret, or build artifact.

- [ ] **Step 6: Record the final review checkpoint**

If commit authority exists:

```powershell
git add docs/adr
git commit -m "docs(adr): record module composition ownership"
```

Report separately:

- source-ready evidence;
- integration-proven evidence, including real-port Docker-backed webhook/outbox results;
- live/deployment status as **not attempted**;
- exact residual warnings, including the Node engine warning if frontend tooling was invoked;
- next gate: user review of the WS3 diff before push/PR or WS4.

## Plan Self-Review

- D4: Tasks 1, 4, and 5 cover evaluated enabled-module references, complete process-policy preservation, Host-global stream identity, positive Host-assembly dependency selection, one builder/mapping owner, middleware hook, and non-composition Api isolation.
- D5: Tasks 2, 3, 4, 6, and 7 cover TimeProvider, Web/hosting defaults, Identity, Flights, integration contracts, and Shared charters; `TestOnlyGuard` no longer violates Shared.Web ownership.
- D6: Tasks 2 and 6 cover no global retry, explicit internal opt-in, all provider/health clients, no suppression workaround, and registered OTel redaction.
- D8 persistence parity is implemented inside Task 4 through the actual facade and design-time factory, without migration generation/application.
- D11 expected/unhandled ProblemDetails, OpenAPI generation, and fail-closed identity are covered by Tasks 2 and 7.
- WS4 domain/projection behavior, WS5 full matrices/truthfulness cleanup, deployment, external mutation, and migrations remain outside this plan.

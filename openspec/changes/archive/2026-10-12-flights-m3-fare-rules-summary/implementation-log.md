# Implementation ledger — plan: openspec/changes/flights-m3-fare-rules-summary/tasks.md

Base:163e3d9b467259518a11e6c6c423aa0e3f79a513. Worktree managed, detached HEAD. User «го» authorizes code/tests implementation; no stage/commit/push/PR/CI dispatch/sync/archive/deploy/real API.

## Pre-flight interfaces

| Producer -> consumer | Contract and check |
|---|---|
| 2.1 ->3.1/3.2/4.1 | Provider-neutral immutable evidence; wire DTO stay Infrastructure; ordinal remapping only at quote |
| 2.2 ->4.2/4.3/6.3 | Frozen snapshot in both quote events; no legacy upcast/replay hash recomputation |
| 3.1/3.2 ->3.3/4.1 | Unknown !=false/0; canonical identity excludes fetch/render metadata |
| 4.1/4.2 ->5.x/6.x | Leaf claim catalog and additive DTO; strict ID/scope coverage, no passenger/supplier identity in AI |
| 5.x ->7.4 | Default-off, bounded one attempt, unknown accounting is not zero; no paid run |
| 6.x ->7.1/7.2 | Fictional demo plus existing ordinary CI; no finance initiated by summary |
| 7.3 ->7.5 | Publication/CI receipt requires later authority; not marked complete from local tests |

Ruling: keep ledger in the canonical change, not a parallel feature plan; use pinned OpenSpec tracking rather than generic Superpowers scripts that assume a different plan format. Commits and external gates remain forbidden. Risk if wrong: bookkeeping mismatch, not product authority.

## Task1.1 startup audit

Full Flights.Unit includes tests/fixtures/dotnet/FlightPiiTestRing.cs with AssemblyFixture and keyring constructor; prohibited locally regardless of category filter. Travel.AI.Tests includes Program-host/PostgreSQL classes; prohibited as a full local project. No ModuleInitializer/AssemblyFixture/StartupHook found in production Flights/shared/Travel.AI source.

Safe local runner: ignored out/fare-rules-safe-tests/FareRules.SafeTests.csproj links only new FareRules/*.cs tests, not the original Unit project or its linked fixture. It references production libraries, does not invoke their Program, hosts, initializers, DB, keys or external providers. Test entry is xUnit generated entry only; existing pinned packages/Directory.Build.props apply. Tests use fictional inline JSON and direct pure/fake boundaries. Windows .NET commands run sequentially. Separate CI executes heavy/persisted suites later.

Task1.1: audit complete, runner execution subject to checking each newly linked test's startup/static/fixture behavior.

## Implementation checkpoints

2026-10-12: RED tests written for omitted/invalid allowed, baggage quantity and informational marketing label; required cabin JSON remains strict. Expected breaks: DTO default false/0 and early optional-field JsonException.

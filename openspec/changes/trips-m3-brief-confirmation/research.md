# Research: Trips M3 planning evidence

Date: 2026-10-27. Scope: read-only repository/public-document research and planning only.

## Provenance

- Repository: D:/_Projects/_github/travel-agency.
- `git fetch origin dev` succeeded before worktree creation. Fetched origin/dev: `35953fd6c8feabf16acd7fb944201711e3d8c5fa`; equal to the historical user reference by observation, not substitution.
- Native app worktree creation used that exact fetched SHA. Worktree: C:/Users/Vladimir_sva/.codex/worktrees/trips-m3-brief-proposal/travel-agency.
- New branch: `codex/trips-m3-brief-confirmation`. Before writing: clean porcelain, HEAD equal to base and successful `git merge-base --is-ancestor <base> HEAD`. Original dev checkout was not switched or edited.
- Pinned launcher in this worktree is `node tools/openspec/run.mjs`. Missing tooling was restored with `npm.cmd ci --prefix tools/openspec --ignore-scripts --offline --no-audit --no-fund` (70 cached packages). Launcher child spawning required sandbox escalation; no launcher changes/bypass.
- Root/module AGENTS, README and completion review were read before branch creation. The clean worktree starts at the same verified source. OpenSpec context resolves this worktree and schema `spec-driven`; initial active change list was empty.
- Read main specs in full: trip-plan-workspace, trip-itinerary-candidates, trip-rail-intentions, trip-data-package-maintenance, trips-local-verification; also flights-fare-rules-summary for the existing bounded inference contract. Current specs remain untouched.
- No credentials, secrets, billing/account settings, model endpoint, existing demo process/storage or human-review material was accessed or changed.

## Observed code and consequences

Paths below are relative to this worktree and base SHA.

| Evidence | Observed behavior | M3 consequence |
| --- | --- | --- |
| modules/trips/AGENTS.md; modules/trips/README.md; docs/architecture/trips-completion-review.md | M1/M2/M2.2 source/CI acceptance and separate pending human/target/rollout gates | Planning/extraction cannot close those gates |
| modules/trips/Travel.Modules.Trips.Core/TripBrief.cs:5,39 | One destination, optional origin, dates, adults, currency, optional budget, interests, pace; ErrorOr factory | No new brief schema; model output is never a domain brief by itself |
| modules/trips/Travel.Modules.Trips.Core/TripValues.cs:26,68,99 | 1–14 inclusive days, 1–9 adults; catalogue1 includes Moscow, SPb, Kaliningrad; IANA zones; exact minor units | Extraction geography is catalogue scope, not walking coverage; never bump catalogue |
| same PlanningMoney.Create | Integer or exactly two fractional digits; 0..999999999.99; RUB/EUR/USD | README prose must not be read as permission for floating point or arbitrary one-digit fractional input |
| modules/trips/Travel.Modules.Trips.Core/TripPlan.cs; Application/TripPlanService.cs; Infrastructure/Persistence/TripPlanStore.cs | Non-destructive date/city/currency changes, owner access, protected snapshots and CAS | No extraction persistence or bypass of existing Apply/Save |
| apps/web/src/app/trips/trips-page.component.ts:93,239,254,274 | briefDraft differs from calendar buffer; applyBrief changes local calendar; submit may apply then Create/Save | Explicit M3 transfer must write briefDraft only and use type=button; never call submit/applyBrief |
| apps/web/src/app/trips/trips-state.service.ts:195,374–406,419,641 | Opaque saved-foundation tickets, new-form defaults SPb/1adult/RUB/[]/balanced with blank dates, non-destructive changeBrief and guarded mutations | Reuse epoch pattern; distinguish retained defaults from extracted wishes and never borrow a missing date from the form |
| modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs; TripsResponsePolicyMiddleware.cs | M1 POST shape guard would reject an extraction body; disabled-before-auth and no-store use route classification | Add precise extraction route classification before M1 body parsing; preserve casing/trailing-slash guards |
| modules/trips/Travel.Modules.Trips.Api/Composition/TripsModule.cs | Default-off Trips and non-Production itinerary flag; readiness/store/protection registration; no AI seam yet | Separate M3 flag/readiness; requests need identity, not stored plan/package dependency |
| apps/Travel.AI/AGENTS.md; Program.cs:60,103 | Separate process; direct Anthropic via IChatClient; Core NATS queue-group request/reply; schema-init gate | Add one bounded feature-specific extractor, no shared AI core/framework |
| apps/Travel.AI/NlSearch/NlSearchOptions.cs:5; NlSearchExtractor.cs | Flights currently pins claude-opus-4-7; bounded prompt/schema accounting; UTC-today anchoring | Existing Flights model/settings/today are not a Trips provider or timezone decision |
| modules/flights/Travel.Modules.Flights.Application/Handlers/NlSearch/NlSearchHandler.cs | Parse then dispatch Flights search | Do not reuse this orchestration: Trips extraction never initiates route/search |
| apps/Travel.AI/FareSummary/FareSummaryChatClient.cs; FareSummaryExtractor.cs; FareSummaryDeliveryPolicy.cs | Feature-specific lazy IChatClient, MaxRetries=0, separate admission, model/pricing blank by default, returned ModelId/usage checks and scoped discard policy | Useful bounded pattern, not an approved model/budget; don't couple Trips to Flights types |
| apps/Travel.AI/NlSearch/NlSearchAiHandler.cs; Persistence/Entities/CostLedgerEntry.cs | Pre-dispatch Admitted row and nullable usage/cost exist; unique message/correlation is not call dedup | Reuse metadata-only ledger without text/owner/trip/hash; reserve unknown spend |
| apps/Travel.Host/Program.cs:138–146,177–179 | Global durable local queues/transactions; Trips response policy before authentication | Prove Core NATS payload avoids durable storage and error/deadletter logs through real Host tests |
| shared/dotnet/Travel.IntegrationContracts.AI; shared/AGENTS.md | Versioned leaf transport contracts, no module internals | Separate TripsBriefExtraction version1 identities; provider DTOs stay in Infrastructure |
| tests/Travel.Host.Tests.Integration/NlSearch/NlSearchTransportTests.cs; tests/Travel.AI.Tests; tests/Travel.Tests.Contract; tests/Travel.Tests.Architecture | Existing real-host/disposable NATS with fake IChatClient, shape and boundary patterns | Add scoped proofs here, but fake chat never proves model quality |
| tests/trips; tests/travel-e2e/trips-demo; trips-full-stack; shared/ts/api-client/src/trips* | Existing owner/CAS/privacy, strict decoders, two-city source/runtime regressions | Preserve all existing assertions; separate new M3 fictional fixtures/profile from retained demo |

The observation paths denote current facts; all new names/contracts in design.md are proposals. No production execution was used to infer readiness.

## Options considered

1. Deterministic-only alias/date parser: cheapest and private, useful as validator/manual baseline, but limited paraphrase recognition. Retained as validation, not advertised as an alternate AI fallback.
2. One feature-specific extractor in current Travel.AI using existing Anthropic SDK/IChatClient: smallest integration and comparable to fare summary. Recommended architecture; exact model/provider, price and transmission permission unresolved.
3. Local model: can keep provider boundary local if separately accepted; hardware, serving, latency, Russian quality and total running cost are unmeasured. No new local server/client in this change by assumption.
4. New hosted provider: possible later IChatClient capability, but adds credentials, wire integration and data-processing review; not needed for the first source slice.
5. Generic AI platform or model-generated route: no retained consumer requiring it, outside scope.

## Public provider research (no account/API access)

Fetched 2026-10-27. Public advertised prices are comparison inputs, not a selected deployment price, account entitlement, model availability or authorized spend.

- [Anthropic pricing](https://platform.claude.com/docs/en/about-claude/pricing): standard synchronous uncached Haiku4.5 input/output USD1/5 per million tokens; Sonnet4.6 USD3/15. At 4096 input +1024 output tokens, arithmetic upper comparison is USD0.009216 vs USD0.027648/request; 48 one-pass cases USD0.442368 vs USD1.327104, before separate warmups/extra attempts/taxes. Do not use Batch/cache discounts for this private synchronous path.
- [API retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention) and [commercial retention](https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data): provider processing/retention depends on product, model, feature and agreement. App no-store does not establish provider zero-retention; verify the applicable model/account terms before separately authorizing private text. This research made no account setting or entitlement check.

No provider is configured by this proposal. No inference, pricing verification via billing, quality/latency benchmark, demo rebuild or activation took place.

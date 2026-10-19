# Hotels M2 implementation report

Final status2026-10-19:25/25 source/local tasks completed and personally reviewed; PR55 merged into dev (5a0fb7a36de6101170c4b676415e616890b4bfa6), PR CI17/17 and post-merge CI16/16 passed. M1 archived after confirming its existing sync; M2 deltas synced to main and M2 archived. [Archive verification](archive-verification.md) and [completion review](../../../../docs/architecture/hotels-completion-review.md) record the current outcome. Earlier sections below remain chronological history. Load, real supplier and target/deployment acceptance remain separate.

Historical first-stage source authorization: owner2026-10-19, code and autonomous tests only. No supplier calls, credentials/certificates, migrations, DB/real Host, load, CI/publication or deploy. Main-agent review is a separate required step. HEAD `75dfba532c0f321286630e3d04fbea70347903a7`; no branch change/stage/commit/push.

## Historical implementation slice

Normalized source namespace, v2 admission metadata in existing ReservationState/StateJson, explicit whole-stay price evidence and structured condition records are being implemented. Legacy accepted StayTerms are unchanged. This is partial work, not a completed task1.2 or M2 source acceptance; descriptor consumers/admission guard remain to implement.

## Test receipts

- RED: `dotnet test tests/hotels/Travel.Modules.Hotels.Tests.Unit/Travel.Modules.Hotels.Tests.Unit.csproj --no-restore -m:1 /nodeReuse:false --disable-build-servers --filter FullyQualifiedName~HotelComparisonTests`, exit1, missing new HotelObligation/HotelPriceEvidence types (CS0246).
- First GREEN attempt: same command, exit1, test-only CS8602 on unchecked Deserialize result; corrected with explicit null-forgiving assertion. No passing result claimed yet.
- Initial sandbox attempt without `-m:1` returned a silent running session9775. Build lane coordination with main agent now requires explicit lane availability; no further concurrent build runs. Escalated single-worker invocation completed normally. Tooling failure is not a product test result.

## Evidence limits

Original M1 memory result remains failed16/17, workingSetRecovery=false, passed=false, exit1. No M2 waiver. No implementation checklist checkbox is completed by this progress note.

## Verified incremental receipts (2026-10-19)

All dotnet commands below use explicit project `.csproj`, `--no-restore -m:1 /nodeReuse:false --disable-build-servers`, escalation for local tooling, and a standalone fixture boundary. No database collection fixture was selected.

| Slice | RED | Latest GREEN |
|---|---|---|
| normalized money/source/StateJson, truth table, conservative pairing/ranking and admission guard | missing model/types; test analyzer fixes recorded | Hotels Unit48/48, exit0 |
| private context counted with owning cache entry, 4096UTF8 bound, public JSON redaction |2/2 failed on private byte accounting/oversized acceptance | Hotels Unit50/50, exit0 |
| controlled parallel source overlap/caller cancellation/retained aggregate permits |2/2 failed | Hotels Unit52/52, exit0 |
| explicit recognized legacy context when provider gains descriptor | missing interface CS0246 | Hotels Unit53/53, exit0 |
| source-party max50 before claim while general M1 name contract remains100 | missing source-party interface CS0246 | Hotels Unit54/54, exit0 |
| Hotelbeds Availability RECHECK→CheckRate→one Booking, no tax feature, unapproved actual source | missing adapter types CS0234/CS0246; compile/analyzer corrections | standalone HotelbedsProviderTests3/3, exit0 |
| provider-key foreground/recovery isolation |1/1 failed (HBX permit refused while Duffel active) | combined owned HBX/capacity/Duffel protocol49/49, exit0 |
| independent-review HBX scope/status/correlation findings | own adversarial tests3failed/3passed | combined owned HBX/capacity/Duffel protocol52/52, exit0 |

HBX fixes validated: Search envelope/CheckRate source dates must match requested scope; unknown booking status produces no positive facts; contradictory clientReference is unattributable Unknown; source expiry remains null unless supplied, local freshness bound is explicit metadata. New purchase source-party support runs before capacity/claim. Stage IDs do not become rateKey parsing or idempotency claims. Fictional HttpClient requires an explicitly marked owned handler and cannot be interchanged with ordinary transport.

## Official wire provenance

Official Booking API OpenAPI3.0 YAML: `https://d2gsjv9lq0esbh.cloudfront.net/open-api-documentation/OpenAPI-Hotel-BookingAPI-3.0.yaml`; SHA256 `D566F132A95BE1B8E9642D85B33438F9EC39A104030D842E8CD6623E46210DE6`, last-modified2026-10-16T08:06:03Z, retrieved by main agent2026-10-19 from the API-reference Redoc initializer. It is public documentation, not a supplier API call. JSON object names `rooms` follow schema properties and official workflow/Postman; publisher required arrays incorrectly say `room`. mTLS endpoint follows current mutual-authentication policy rather than older schema servers. ApiRateCheckRate omits taxes, but official tax-breakdown narrative explicitly documents taxes inside rates for Availability/CheckRates/Booking; fresh CheckRate fictional tax fixture follows that documented shape. Missing taxes never inherits previous evidence or becomes zero.

Consumed fields: Availability `stay.checkIn/checkOut`, `occupancies.rooms/adults/children`, hotel codes/geolocation; CheckRate `rooms[].rateKey`; Booking `holder.name/surname` (max50), `rooms[].rateKey/paxes` (AD without fabricated age), `clientReference` (max20); response hotel code/room code/rate scope, exact net string and booking totalNet number, source currency, typed board and cancellation offset/amount, tax allIncluded/included/actual amount/currency. Key remains opaque. Auth: API key + SHA256(key+secret+epoch-seconds), TimeProvider-based. Fictional fixtures contain only project-owned hotel/guest/key content.

## Historical checkpoint before the later source receipts

No task checkbox is complete yet. Remaining implementation includes full Api.Composition/mTLS options transport consumers and two-source approval guard, complete aggregate rates/group selectors and schemaVersion2 public contract/local token issuance, production matching/current-evidence/quarantine and quote mapping freshness, Duffel M2 metadata, typed client/OpenAPI/UI, in-memory ordinary HTTP/browser harness, controlled-clock90/140/145/10 tests, full durable/recovery/legacy/adversarial coverage, formatting and source documentation. Main-agent independent review has started but task7.3 is not marked. Source-ready, CI, browser/load/supplier/target acceptance remain unestablished.

## Later source and review receipts

- Whole-pair guard: explicit own-HBX-approved/global-pair-unapproved case RED missing constructor→GREEN; actual HBX now consumes the module approval boundary too. M2-disabled composition registers only the existing Duffel source. Certificate selection moved from Api.Composition into Hotelbeds Infrastructure and is not opened when disabled/fictional/unapproved.
- Private transport account is separate from the public source alias in normalized persisted binding. Duffel retains OrganisationId for its existing row/webhook correlation. Duffel rate metadata now carries v2 binding so mapping cannot disappear during DU preparation. Dedicated end-to-end row/account and DU mapping replacement proofs are still required.
- Main-agent independent mapping admission review:5 failures reproduced (missing evidence/reviewer/fingerprint, other-profile, duplicate IDs exception), fixed using common reviewed-evidence validation; full Unit64/64 passed.
- Main-agent independent aggregate review:4 failures reproduced (100groups/200leaves, combined entry bytes, expired source isolation, conflicting same-source key), fixed with stable whole-group bounds/byte reduction, explicit source Limited reasons and conflict quarantine; full Unit69/69 passed.
- Immutable cache mutation test RED1failure→GREEN Unit65/65 at that point. Internal reviewed-map sentinel reviewer/evidence public serialization test RED1failure→GREEN full Unit70/70. Whole-entry private byte accounting includes opaque handles, normalized binding and private reviewed-map records; public JSON ignores handles/bindings/map audit records.
- Typed source cancellation now distinguishes Penalty/From for HBX from Refund/Before for Duffel; HBX display states the cancellation penalty explicitly. Official Hotels glossary establishes NRF non-refundable and PAQ package semantics. NOR/missing policy remains unknown; unsupported package remains non-purchasable. Own NRF/package tests plus independent malformed-boolean/duplicate-money review produced actual RED failures. Latest combined standalone HBX/Duffel/guard subset67/67 passed,0skipped, exit0; these classes contain no DB fixture.
- Retained private result source metadata test RED1failure→GREEN. Public source is the saved original alias/provider/environment, never a private transport account/context.
- TS decoder targeted tests:4/4 passed after RED missing-v2 and missing-source-count assertions. Typed price, structured conditions (including offset, penalty/refund/boundary) and ranking explanations are decoded; unrecognized private fields are not projected.
- Angular component tests: source-flow resource continuity RED1/11→GREEN11/11; one grouped card/two scoped currencies RED1/12→GREEN12/12. UI retains draft resource on repeated search and latches dispatch before possible lost reply; no fresh buy/source/search recovery after uncertain submission. Further dedicated lost-reply/source-change/browser proofs remain required. Review caption now names supplier debit and unknown retail; fixed scopes are translated. Latest caption/status refinements still await rerun.

The preceding receipts are incremental history, not final acceptance. The implemented owned in-memory harness now exercises real Core/Application and both wire adapters through public HTTP handlers, with ephemeral encryption and a JSON-cloning CAS store. No database or supplier socket is involved.

## Source-freeze preparation receipts (2026-10-19)

- Unit:79/79 passed before final narrow drain/transport additions. Source read bounds/permutation:5/5 passed, covering Hotelbeds201-rate permutation and Duffel cap permutation plus Prepare normalization deadline. HTTP90/140/application145/completion10 controlled clocks:4/4 passed.
- CAS, legacy serialized M1, known-reference and public HTTP subsets:17/17 passed (exit0). Includes exactly one source Create under coordinated contenders, replay after serialized state, changed account/key/mapping refusal, anonymous/foreign-owner guards, Unknown and explicit HBX400 validation response without resend, three bounded known-reference reads across scopes, draft refusal versus admitted drain after M2-off for both sources.
- Aggregate fake-clock tests:2/2 passed (exit0): already-completed source after17s cannot publish/cache; late success after15s timeout cannot change the frozen result.
- R9 transport guard:6/6 passed (exit0), including M2-off fictional Duffel with ordinary HttpClient:0 Create/Read HTTP and NewPurchaseAllowed=false. Matching owned transport drain is covered by known-reference tests.
- Actual Wolverine/OpenAPI standalone:1/1 passed (exit0), receipt `tests/Travel.Host.Tests.Integration/TestResults/hotels-m2-openapi.trx`. Actual Hotels discovery and AddOpenApi/TestServer, no Host Program/module registration/DB/provider. Public v2 selectors/groups/statuses/accepted typed facts present; private context/binding/reviewer/evidence absent.
- Architecture helper RED NRE was reproduced for compiler-generated dependency with null Assembly; only null-safe access plus existing namespace fallback changed. LayerMatrix11/11 passed; full Architecture fresh main-agent rerun remains required, historical181/182 failure retained.
- Browser4/4 passed and was personally repeated by main agent before latest final changes. Final helper restart, privacy console/URL capture and fresh browser repetition remain required. Angular13/13 and API decoder4/4 intermediate passed; main-agent full TS233/233 also intermediate.

HBX non-2xx responses including400 INVALID_DATA remain Unknown: public documentation does not prove no creation/financial effect. No positive no-creation inference or resend is introduced. Checked-in M1 serialized fixture omits M2 fields; accepted terms are not rewritten with new defaults. Existing DB-backed HTTP test selectors now consume issued tokens, but those tests were not run under this authorization.

Pending independent acceptance: main-agent fresh final Unit/standalone integration/OpenAPI/Architecture/TS/Angular/browser receipts and task7.3; ordinary relational/Host/CI, load, actual supplier/mTLS/credentials/target/deployment gates. No migration generated/applied, external API called, real certificate/key provisioned, load run, commit/push/publication performed. M1 failed16/17 workingSetRecovery remains failed and is not a waiver.

## Final independent autonomous receipts (2026-10-19)

Main agent personally repeated the frozen source checks and reported these final results:

| Check | Final result |
|---|---|
| Hotels Unit |79/79 passed |
| Hotels standalone Integration (all except DB-backed HotelHttpTests and HotelDurablePurchaseTests) |114/114 passed |
| Architecture, including retained forbidden-layer detection and null-Assembly regression |183/183 passed |
| Isolated actual Wolverine/OpenAPI |1/1 passed |
| TypeScript API client |233/233 passed |
| Angular Hotels |14/14 passed |
| Owned localhost headless browser |4/4 passed |
| CSharpier |53 files, exit0 |
| Biome |11 files, exit0;7 warnings/162 infos retained |

The114 autonomous integration cases include the final captured External-profile request-contract test: ordinary HttpClient with an owned terminal handler, exact signature from injected TimeProvider epoch and changed next-second signature, documented string latitude/longitude/radius, no sockets or certificate-store access. The final browser uses console/request-URL/storage/SSR privacy checks; actual purchase bodies carry the explicitly entered fictional guest data. Main-agent personal review is complete:22/25; only tasks6.1–6.3 remain open.

Tasks6.1–6.3 have substantial autonomous coverage but remain pending for relational CAS/commit/crash durability and actual-store restart, ordinary Host Duffel ingress/inbox isolation, and physical persistence encryption/retention/key-outage privacy acceptance. No in-memory receipt substitutes for these gates. Task7.3 and final verification conclusion belong to the main agent. Full ordinary CI/Host/DB, load, external supplier/mTLS certification and target/deployment acceptance were not run and remain separate. The original red Architecture181/182 receipt is retained alongside the final green result. Historical M1 memory16/17 remains failed.

Cleanup: only owned helper sessions33784 (Angular) and84166 (explicit m2-memory fixture) were stopped through their own terminal input. Both sessions completed; subsequent `Get-NetTCPConnection -State Listen` inspection found no listeners on loopback ports4202/5111. No unrelated process was stopped. Historical implementer reconciliation before main-agent task7.3:21 completed/4 pending; `git diff --check` exit0.

## Subsequent authorized local relational/Host acceptance

The owner separately authorized own disposable Docker PostgreSQL, existing migrations and ephemeral keys. A new `gpt-6.1-sol` worker added the focused actual Host/SQL/process fixtures; the main agent personally reviewed them and requested stronger concurrent-save, transactional-work, durable-effect-marker and crypto-purpose assertions. No production behavior, migration files or dependency pins/locks changed in this stage.

The worker's corrected focused cases passed6/6 and existing PostgreSQL durable/HTTP regressions11/11. The main agent's full Hotels Host run passed44/45, with only the stale public OpenAPI snapshot failing. Structural review established six approved typed response schemas, selector alternatives and31 newly reachable public schemas; paths/security and private-field exclusion remained intact. After updating only the expected snapshot, the main agent's snapshot/schema checks passed2/2; the source-only shared-wire regression passed114/114. The original red run remains red and is retained. Full commands, source freezes and limitations are in verification.md/relational-verification.md; the final status is25/25 source/local tasks, without supplier, CI, load or rollout proof.

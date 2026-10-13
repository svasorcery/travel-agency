# Scenario to source test map

2026-10-13. All38scenarios in the delta spec have test source and source/ordinaryCIacceptance. Local means observed controlled source checks; CI accepted refers to exact-head PR46CI38357138705, all15ordinary jobs including mandatoryE2E. Postmerge evidence is in [verification](verification.md). No fixture implies target grants, keys, supplier/payment/model acceptance.

Abbreviations link to exact sources: [Unit workflow](../../../../tests/support/Travel.Modules.Support.Tests.Unit/SupportWorkflowTests.cs), [validation](../../../../tests/support/Travel.Modules.Support.Tests.Unit/SupportValidationTests.cs), [HTTP](../../../../tests/Travel.Host.Tests.Integration/Support/SupportHttpTests.cs), [body](../../../../tests/Travel.Host.Tests.Integration/Support/SupportBodyGuardTests.cs), [Wolverine/OpenAPI](../../../../tests/Travel.Host.Tests.Integration/Support/SupportWolverineOpenApiTests.cs), [EF](../../../../tests/support/Travel.Modules.Support.Tests.Integration/SupportPersistenceTests.cs), [protection](../../../../tests/support/Travel.Modules.Support.Tests.Integration/SupportTextProtectionTests.cs), [issuer](../../../../tests/Travel.Host.Tests.Integration/Support/SupportOperatorIssuanceTests.cs), [source ownership](../../../../tests/Travel.Tests.Contract/FlightsSupportOrderAccessTests.cs), [Marten](../../../../tests/flights/Travel.Modules.Flights.Tests.Integration/Ancillaries/FlightsSupportOrderAccessIntegrationTests.cs), [UI state](../../../../apps/web/src/app/support/support-state.service.spec.ts), [auth](../../../../apps/web/src/app/support/support-auth.service.spec.ts), [component](../../../../apps/web/src/app/support/support-page.component.spec.ts), [browser](../../../../tests/travel-e2e/demo/support-workflow.spec.ts), [decoder](../../../../shared/ts/api-client/src/support.decoder.spec.ts), [routes](../../../../apps/web/src/app/app.routes.spec.ts).

| Scenario | Test source and observed boundary |
| --- | --- |
| Foreign case or message | Unit foreign-owner-before-decrypt; HTTP foreign customer; local |
| Anonymous or invalid identity | HTTP signed signature/issuer/audience/subject/lifetime matrix; local; real Host auth prefix CI accepted in run38357138705 |
| Booking permission alone | HTTP operator scope+azp matrix, auth booking compatibility; local |
| Authorized reply | Unit actor/state transitions; HTTP and all12generated Wolverine handlers; browser lifecycle; local |
| Actual operator and non-operator token issuance | Issuer actual PKCE/role/customer/non-operator cases; CI accepted in run38357138705 |
| No order supplied | Unit creation zero lookup; browser unlinked lifecycle; local |
| Foreign order or unavailable lookup | Unit no-commit/reserved-error/storage-unknown cases, source ownership, HTTP and explicit unlinked browser; local |
| Held order before read model appears | Source ownership read-only routing/version/terminal matrix local; actual committed Marten without EF projection CI accepted in run38357138705 |
| Invalid body or conflicting properties | Validation scalars/UTF16 plus HTTP/body malformed/duplicate/unknown/32KiB/no echo; local |
| HTML and model instructions | Unit exact text, HTTP DTO strings, browser plain markup/no foreign API traffic; local |
| Creation commit fails | Unit protection/store failure no success; EF atomic rollback/uniqueness CI accepted in run38357138705 |
| Successful commit and lost reply | Unit retained replay, Node lost response, browser direct receipt; local; EF persistence CI accepted in run38357138705 |
| Reply and resolve | Unit lifecycle and competing reply/resolve, browser lifecycle; local; EF CAS race CI accepted in run38357138705 |
| Customer resumes a waiting conversation | Unit lifecycle, browser operator→customer; local |
| Resolved conversation | Unit before/at-expiry/reopen and browser resolve/reopen/expired; local |
| Duplicate create or reply | Unit concurrent identical creates/retained reply identity; HTTP replay, Node replay; local; EF uniqueness CI accepted in run38357138705 |
| Identity reused for different content | Unit changed title/body/link/kind/actor/revision, Node changed replay; local; EF namespace rollback CI accepted in run38357138705 |
| Reply races with resolution | Unit two-writer CAS local; actual EF race CI accepted in run38357138705 |
| Lost reply beyond the displayed message page | Unit20+keyset/direct receipt and browser25+lost-reply/newer27snapshot; local |
| Negative or unavailable observation while a write may run | Node early receipt404, browser generic503/early404/negative receipt unknown; local |
| Another actor or action kind | Unit namespace/provenance guard-before-receipt; HTTP wrong actor; local |
| Customer alleges a refund or imminent departure | Unit creation defaults and triage no-op/validation; browser zero booking/model traffic; local |
| Operator corrects category | Unit lifecycle/old-new receipt audit, HTTP triage and browser manual Ticketing/High; local |
| New write without available protection | Unit fake protection failure; actual provider no ordinary bootstrap CI accepted in run38357138705 |
| Wrong binding or lost read key | Unit retained revision/owner guards local; actual provider tamper/all bindings/missing key CI accepted in run38357138705 |
| Persisted ciphertext after restart or key rotation | EF persisted title AND bodies/restart/rotation/new writer/read-key loss/metadata-only receipt regression; CI accepted in run38357138705 |
| Unauthorized response and diagnostics | Lean HTTP no-store/no-echo/signed JWT matrix local; real Host prefix and health fixture CI accepted in run38357138705 |
| More messages or a failed queue read | Unit keysets/failed reads, decoder and browser queue/paging local; real unreadable persisted page CI accepted in run38357138705 |
| Deadline and interrupted retention pass | Unit deadline recheck after await local; actual EF pass bounds/restart/no-decrypt physical deletion CI accepted in run38357138705 |
| Reopen versus deletion | Unit at-expiry and CAS local; actual EF stale candidate/multiple workers/restart CI accepted in run38357138705 |
| Unresolved booking and repeated support reads | Flights auth/order page tests and full Flights/Support browser regression retain unknown confirmation; local |
| Classifier unavailable | Source no classifier registration/dependency; every S1 Unit/browser runs without Laya/model; local |
| Late old identity response | UI state/auth generations and stale403/old receipt tests; local |
| Unknown reply and reload | UI state deferred/nav/reload plus browser reload-no-resend; local |
| Private server rendering and unsafe auth return | Explicit Client route tests and auth callback allowlist; local |
| Permission lost without identity change | UI current401/403/capability refresh/stale403 tests, browser private text erase and retained barrier; local |
| Fixture and CI success | Inventory source lanes and local receipts; PR38357138705all15includingE2E and merge01f4afb/postmerge38358538463all14; receipt in verification |
| Plan ready | Historical planning review plus current independent whole-source dispositions; publication/runtime gates retained |

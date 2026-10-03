# Flights M2.3: safe booking and an explicit adult passenger party

**Status:** approved by the user on 2026-10-03 subject to avoiding overengineering. A source-backed scope review narrowed M2.3a below; execution is authorized through the existing delivery cycle. Real integrations/local schema apply/deployment remain excluded.

**Chat objective:** finish all of Flights M2 through reviewed increments. M2.1 ranking and M2.2 protected PII are merged, including PR #29 follow-up. M2.3 completion is not completion of M2; M2.4 saved travelers and M2.5 multi-leg/open-jaw follow below.

**Fresh evidence:** `git fetch origin dev` on 2026-10-03 returned `02356dfc5010172548bb45938bc1bf29deae6dcd`. The prior checkpoint is its ancestor; primary dev is clean and equal to origin/dev, with no unpublished commits. Post-merge [CI 37062596428](https://github.com/svasorcery/travel-agency/actions/runs/37062596428) completed successfully. New managed worktree `C:\Users\Vladimir_sva\.codex\worktrees\flights-m23-design\travel-agency` starts at that exact SHA; no archived worktree was restored.

## 1. Product scope and delivery order

A user searches for 1–9 adults, checks a whole-party price, supplies fictional details for each explicit quoted slot, holds one complete offer and confirms the accepted group total. One order is one party on one one-way or mirrored return itinerary. No per-passenger partial success.

Two deliveries are proposed because source review found prerequisites beyond array cardinality:

1. **M2.3a — booking safety:** owner-scoped in-memory hold barrier, explicit quote acceptance, accepted-total confirmation, and truthful provider outcomes. Preserve the single-passenger happy path.
2. **M2.3b — adult party:** search count, supplier/local passenger bindings, protected party snapshots, V3 hold, read-model count, repeated passenger form and fictional demonstration.

Both have their own implementation plan and complete delivery cycle. A is a prerequisite for B, not an unrelated B5 redesign. Source-only schema changes are explicit approval items; no local database apply is authorized.

Then **M2.4** adds owner-only saved-traveler CRUD in Flights EF and opt-in prefill; **M2.5** adds ordered 1–4 requested legs and explicit journey kind. Their designs can proceed in parallel once the party contract is fixed; implementation must retain separate ownership of shared Contracts, booking UI, composition and EF snapshot. Each still needs a concrete approved design/plan before code.

Fixed boundaries: fictional data only; no real supplier/payment/Anthropic/paid API calls or paid AI-evals; no local Host/AppHost/schema application, key provisioning or deployment. Tokens, PII drafts and browser operation state remain in memory. Backend JWT/owner/scope stays intact; fake auth stays in isolated demo/tests. No passports, children/infants, ancillaries, SSE, Support, user refunds, real cancellation recovery or OpenSpec/M3.

## 2. Source findings that drive the design

Paths are repository-relative; F = `modules/flights/Travel.Modules.Flights`, U = `tests/flights/Travel.Modules.Flights.Tests.Unit`, I = `tests/flights/Travel.Modules.Flights.Tests.Integration`.

| Evidence | Consequence |
| --- | --- |
| F.Core `ValueObjects/SearchCriteria.cs`; F.Api `Endpoints/HoldOfferEndpoint.cs` enforce 1; TS `flights-search.types.ts` literal 1 and `flights-booking.types.ts` singleton tuple | Expand every layer, not just the HTTP array |
| F.Core `BookableOffer`, `OfferQuoted`, `OfferReQuoted` have no passenger binding | Persist authoritative quote revision and slots before accepting PII |
| Duffel search always sends one adult; hold has no passenger `id`/title, silently maps unspecified gender to male | Add a real ACL contract and pre-HTTP validation, no guessed supplier identity/gender |
| Segment mapper reads `Passengers[0]`; baggage aggregates Max across every passenger/segment | Validate cabin consistency and label baggage scope honestly |
| Travelpayouts request and deeplink are singleton; no proof of a group total | Skip this provider for count >1 before HTTP; never multiply a reference fare |
| `flight-booking-panel.component.ts` owns/discards hold attempt; late responses lack owner/epoch guards; search changes destroy checkout | Hold evidence must survive SPA component destruction, with one unresolved hold per tab |
| Later hold 400/401/403/expiry may clear an earlier unknown attempt | A later failure is not proof that the first write failed |
| `flights-page.component.ts:beginBooking` lacks internal acceptance guard; source:null re-quotes do not compare old route | Enforce acceptance in code and retain previous non-PII quote for comparison |
| F.Application `ConfirmOrderHandler.cs` captures wallet before provider; errors append Cancelled without supplier cancellation | Remove false terminal state; classify effects explicitly |
| F.Infrastructure `DuffelFlightBookingProvider.cs` pays fresh provider price and uses `/air/orders/{id}/payments` | Compare accepted money; use documented payment endpoint/body; validate receipt |
| Test wallet only caches authorize references in memory; Capture/Refund are not durable idempotency contracts | Ordinary confirm replay is unsafe across failures/restarts |
| ADR 0024 and `OrderQueriesTests.Metadata_reads_do_not_select_passenger_json` exclude PII JSON from normal SQL | Store passenger count in an explicit column, not itinerary or PII JSON |
| The 16 KiB cap exists only in Node demo; production middleware calls EnableBuffering/unbounded CopyToAsync | Introduce a production bounded in-memory read before hashing/model binding; prevent oversized PII from spilling to disk |

M2.2 V1/V2 replay, private certificate key ring, safe exceptions, encrypted inbox and owner-prefix validation remain the foundation. Historical plaintext is not rewritten.

## 3. Scope ruling after conditional approval

The original A draft overreached into M1 confirmation-workflow architecture. For the explicitly fictional demonstration, new Started/Stopped events, a ConfirmationState column, a permanent server barrier and a hold-result endpoint are not necessary to deliver M2. They are removed, not hidden in another abstraction. No new recovery/compensation engine is introduced.

Keep the direct correctness requirements: accepted money and valid payment receipt, no fabricated cancellation/confirmation/refund from uncertainty, owner-scoped tab-memory no-repeat protection, explicit quote acceptance and bounded in-memory request bodies. B remains the actual party feature. Source count migration remains necessary to show group size without reading PII.

**Residual boundary:** tab-memory protection does not coordinate other clients or survive reload/server restart. The server still has external-effect-before-commit gaps and only a TestOnly in-memory wallet. Do not claim exactly-once financial effects, use reload as a way to unlock an unknown operation, or call this live payment readiness. The accepted B5 scope already made this distinction; durable financial coordination/recovery is a separate future design.

## 4. M2.3a — bounded input, hold and quote safety

Introduce a 16 KiB production cap for booking mutation bodies before EnableBuffering, hashing, model binding or store calls. Check Content-Length and chunked/unknown length; read at most limit+1 bytes into memory, never spill to disk, then provide a rewindable memory stream for valid input. Oversize returns safe 413 without store/provider calls. Keep method/path/raw-byte hashing identical. Route case/trailing slash cannot bypass protection; do not normalize historical hash/store identities. The Node demo already has this cap; production currently does not.

Extend existing FlightOrderOperationsService with one active hold attempt. Draft fields remain component-local; dispatch freezes raw serialized body/key/aggregate/owner/epoch. SPA navigation, criteria edits or a new aggregate cannot bypass pending/unknown hold. Suppress late responses after owner/session change. Explicit logout clears PII and invalidates completions; transient auth hides data without converting unknown into rejection. Erase retained PII at the existing 24-hour horizon while keeping the current-tab unresolved barrier.

After a potentially sent hold or confirm becomes unknown, no new write is sent, including with the same HTTP key. Existing owner GET may show observed order state, but Held/404/terminal GET alone does not prove the original body/key outcome or clear its uncertainty. An attributable late successful response may resolve its own attempt. No new receipt endpoint, hash-query contract, durable attempt table or recovery UI.

A first proved pre-effect validation failure may release an attempt. A late error after unknown cannot. Remove the current supplier POST422→local OfferExpired ambiguity: supplier errors after possible hold submission produce a distinct HoldOutcomeUnknown; post-effect concurrency conflict is also unknown. Do not infer non-operation from status alone. Keep B5 cancellation behavior/retry unchanged; only hold/confirm write-retry after uncertainty is deliberately tightened.

Bind acceptance to the latest quote and enforce it in beginBooking/dispatch, not only the template. Compare prior non-PII route/money and B's party revision after re-quote, including source:null login refresh. Full OIDC redirect still requires fresh selection.

## 5. M2.3a — accepted-price and truthful confirmation

Keep ErrorOr-based provider ports; do not add an outcome hierarchy. Add read-only ValidateConfirmationAsync(providerOrderId, expectedTotal, ct) before wallet Authorize/Capture. It checks exact order ID, accepted amount/currency, explicit unpaid hold state and valid payment deadline. A preflight error causes no wallet calls or events. Provider ConfirmOrderAsync receives expectedTotal (replacing the unsupported supplier-idempotency assumption), checks again immediately before POST and sends only the accepted amount.

Duffel payment uses POST /air/payments with `{data:{order_id,payment:{type:"balance",amount,currency}}}`. Confirm only a well-formed receipt with nonempty ID, matching order_id, status=succeeded, balance type and exact amount/non-null currency. A 2xx pending/failed/cancelled/malformed/mismatched receipt is not success. Do not infer payment from awaiting_payment=false; expired unpaid holds also have that value. No receipt-recovery loop or live-provider claim.

Disable automatic Duffel mutation POST retries even if a key header is supplied. Retain bounded GET/HEAD retries and other-provider/auth policies. Correct comments that claimed durable financial idempotency from the test wallet/header. Documented payment500 can have an external effect.

On capture/provider uncertainty, leave the prior booking state Held, return distinct ConfirmationOutcomeUnknown and emit no OrderCancelled/OrderRefunded/OrderConfirmed. Do not automatically Refund or Cancel. The demo wallet is a simulation, so a new compensation workflow is not justified here. Known preflight mismatch returns safe OrderPriceChanged; a mismatch detected after local capture is treated conservatively as unknown by the handler. Do not expose raw error bodies/inner exceptions. Cancellation stays cancellation, with no compensating side effects.

Successful confirmation keeps the existing atomic PaymentAuthorized+OrderConfirmed+notification/reconcile commit and version semantics. No new domain event, permanent block, confirmation-state column, source-head read or projection change in A. External effects and commit remain non-atomic; this limitation is explicit, and the UI must not offer another hold/confirm write after an unknown result in its current tab. Backend auth/state/concurrency guards remain intact, but are not a global financial coordination guarantee.

## 6. M2.3b — passenger and quote model

- Product bound: 1–9 adults; each is at least 18 on the first actual departure's origin-local calendar date. This conservative product rule is not every airline's definition. Birth date still cannot be future today. Persist a separate normalized `FirstDepartureLocalDate` in new offer/binding facts, taken from the calendar portion of Duffel's origin-local `departing_at` before any UTC conversion. Do not derive it later from a server-local/UTC instant. Test real offset-less provider JSON, explicit offsets crossing UTC date, event roundtrip and leap-day behavior. This does not claim a complete itinerary timezone redesign.
- `BookingPassengerId`: local immutable ID for a slot; not a user/profile ID. `SupplierPassengerReference`: opaque Infrastructure-mapped reference. `TravelerProfileId`: future M2.4 ID, never a booking binding.
- `QuoteBinding`: new revision GUID plus ordered slots `{BookingPassengerId, SupplierPassengerReference, kind=adult}`. Each fresh quote is authoritative; count must match requested count, refs must be nonempty/unique and supported. Never zip unnamed passengers to supplier arrays.
- Add optional trailing `QuoteBinding` to `OfferQuoted` and `OfferReQuoted` for old JSON replay. Every new quote emits it. A successful re-quote always changes revision. If supplier ref set is identical, preserve local slot IDs/order by ref lookup; if it changes, replace binding and require fresh review/entry. A count change requires a new search/aggregate. Legacy quoted streams without binding require a fresh quote before hold; no invented IDs. Replaying a re-quote without binding clears any earlier binding instead of retaining stale slots; replay never generates IDs.
- Extend `BookableOffer` with optional normalized party/capability facts for historical deserialization; new quote/hold requires valid facts. `WithAmount`, serialization, cache validation and dedup preserve/include them.
- Keep old `PassengerInfo` unchanged for V1/V2 history. New party details wrap it with explicitly chosen title. Supported Duffel titles: `mr`, `ms`, `mrs`, `miss`, `dr`; wire gender only `m`/`f`. Never derive title from gender or map unspecified to male. Unsupported fields yield a safe slot/field error before HTTP. Each passenger supplies their own fictional contact details; owner contact is not copied implicitly. Product input bounds are given/family name 1–20 supported characters, email at most 254 characters, E.164 phone at most 16, date `YYYY-MM-DD`, and bounded title/gender enums. Supplier refs are opaque, ordinal and bounded (256 characters, no control/outer whitespace); no automatic transliteration/truncation. The final raw-byte 16 KiB cap remains authoritative. Redact newly introduced party/details ToString surfaces.

### HTTP compatibility

Retain existing routes to keep one auth/idempotency surface. `/search` accepts integer count 1–9. `/quote` adds expected `passengerCount` (missing means the legacy singleton request) and returns required binding revision, local slots/count and eligibility facts. `/hold` requires quote revision and explicit slot ID/title in every passenger. Missing binding returns `Flights.QuoteBindingRequired`; mismatched/stale binding returns a safe 409 before supplier effects. This is an intentional breaking requirement for **new legacy-client holds**, not a claim of transparent compatibility. Old GET/list/held-confirm/cancel history remains supported. New frontend fails closed on missing contract fields.

No optional field omission may downgrade strict checks. Clients never send supplier passenger refs. Existing successful idempotency replay keeps its raw-body/owner/route semantics. Deployment drains old in-flight/queued hold commands and updates client/server together; old protected singleton commands are not automatically converted into a party.

### Protected party and V3

Add `ProtectedPassengerPartySnapshot` and `IBookingPassengerPartyProtector`; do not overload the singular V2 type/purpose. Protect the entire ordered validated party before message bus dispatch. Bind purpose to `booking-party.v1`, aggregate, owner, quote revision and count. Plaintext contains slot IDs and details; handler checks exact membership against the stored quote after owner/state/revision guards and unprotect. All validation succeeds before one supplier hold invocation for the whole party. A supplier result after possible dispatch is not reclassified as a local pre-effect expiry/validation failure.

New `OfferHeldV3` records provider order, owner, quote revision, count, protected party snapshot and hold timestamps. It copies the exact validated command ciphertext; no encryption after supplier success. V1/V2 identities/Apply/purposes remain unchanged. Replay does not decrypt or reapply today's age rules. Prefix-owner scan, event registry, projector, reconcile/validate/reset and mixed-version tests all recognize V3 and existing booking events.

## 7. Search/provider/ranking contracts

Duffel request repeats adult descriptors for the exact count. Map root offer passenger IDs/type and segment `passenger_id`; reject malformed/duplicate/missing bindings. Require a supported adult party and consistent cabin representation; do not use the first passenger to hide a different cabin for another. No extra supplier passenger mutation call is needed.

Hold must be explicitly supported (`requires_instant_payment=false`) and identity documents must not be required; unknown capability is not an affirmative value. Unsupported offers cannot reach hold. Quote/UI shows a safe reason or blocks the action; do not fabricate instant-payment/passport support. Map each hold passenger with its exact persisted supplier ID, explicit title/gender/contact, and omit the obsolete guessed type field. Baggage remains an overview maximum per passenger/segment, never group allowance; update wording accordingly.

Travelpayouts is explicitly skipped before HTTP for count >1, because current source supplies singleton reference prices. Introduce a small provider-support decision and a separate `skippedProviders` result field with bounded reason codes; skipped is different from outage. Preserve it in cache and demo. All eligible providers failing is unavailable even when other providers were skipped; all providers being ineligible is a supported empty/unsupported result, not an outage. Do not multiply a fare or build a singleton deeplink for a group. This seam can later express unsupported multi-leg in M2.5.

Keep deterministic `price-first-v1` and group totals. Bump cache namespace/envelope version; old entries lacking party/capability facts are misses. Validate cached count/bindings against criteria, not only ranking evidence. Include party/capability facts in exact dedup compatibility, excluding new random local quote IDs. Do not change NL v1 shape or invoke paid AI; its existing count follows validated 1–9 rules only.

## 8. Read model and source migrations

One additive **source-only** schema change is approved for B: PassengerCount non-null default1, check1..9. Historic V1/V2 held orders were singular. Applier writes1 for V1/V2 and validated event count for V3; no ticket inference or event/ciphertext rewrite. Add count to OrderView, normal EF Select, HTTP/TS DTOs, equality/materialization/validation and UI. Do not read PassengerInfoJson or hide metadata in itinerary JSON. Equal-version incremental reconcile remains unchanged; Validate reports mismatch and exclusive reset recomputes count.

Migration source, designer/snapshot and offline SQL review require the approval that names these changes. No local database update, startup initializer, key provisioning or deployment. Existing CI applies migrations only to its disposable databases. Up is additive; Down drops derived columns but cannot make old binaries understand new events. For a separately authorized rollout: stop booking writes/consumers, apply the reviewed additive columns, deploy compatible readers and writers together, then reopen traffic. No new event writer runs against old schema/readers. Rollback after new events requires compatible code/explicit restore, never history rewriting.

## 9. UI/demo acceptance and failure states

Search exposes count 1–9 and labels prices for the whole group. Quote displays count and slot summary, compares all prior non-PII facts and requires acceptance of latest revision. Passenger form uses stable slot IDs, unique labels/fieldset/aria-describedby, focused slot-specific errors, and no add/remove of party inside checkout (change search instead). Title/gender unsupported errors are honest, with no PII echo.

Frozen attempts do not follow edited form data. Auth loss hides data; owner/session changes invalidate completions; pending/unknown hold cannot hand its result to the new owner. After successful hold/confirm, read-model lag is distinct from external outcome unknown. Current-tab unknown hold/confirm evidence blocks another write; observed GET data is not proof of that attempt. It is not a reason to invent Cancelled or Ticketed. Ticket numbers remain flat unless supplier mapping actually proves associations.

The fictional Node demo prices explicit party fixtures (not pretending real fares are linear), encodes count in offer refs, returns fixed slot/revision data, models hold raw-body/key conflicts and status resolution, and exercises A's conservative unknown-outcome UI. Keep its Authorization rejection and demo-only auth replacement. Check 9 passengers at the field limits fit the newly enforced production and existing demo 16 KiB cap, including UTF-8/chunked 413 behavior; never silently truncate fields.

## 10. Verifiable acceptance

| ID | Required evidence |
| --- | --- |
| S1 | Preflight mismatch causes zero wallet/payment effects; final comparison cannot pay changed money; valid matched succeeded receipt is required |
| S2 | Capture/provider error or malformed/sent result produces no false terminal booking events or automatic compensation; local token/key guards suppress repeated writes after unknown |
| S3 | Hold/confirm unknown survives SPA navigation/criteria change; late owner response ignored; GET does not prove original body/key; B5 cancel retry still works |
| S4 | Acceptance checked at dispatch; bounded Content-Length/chunked bodies preserve valid hashes and cannot reach disk buffering/store/provider on oversize |
| P1 | 1,2,9 adults succeed with exact group total/IDs; 0,10,fractional, underage, missing/duplicate/foreign/stale IDs fail before supplier hold |
| P2 | Re-quote same/new ref set and reordered supplier array preserve or explicitly replace binding; acceptance/PII entry cannot carry into wrong revision |
| P3 | Commands/events/projection/messages/logs contain no party PII sentinels; wrong owner/aggregate/revision/count/tamper fails; provider receives exact valid details |
| P4 | Real Marten V1/V2/V3 event replay, prefix checkpoint, suffix/equal version, reset and count metadata converge without keys |
| P5 | Cached cold/warm group totals/ranking/skips match; old payload is a miss; Travelpayouts count>1 makes zero HTTP calls |
| P6 | Fictional browser 2 adults one-way and 9 return, keyboard/360px, errors, unknowns, owner switch/logout; existing B3–B5 successful journeys retained |
| P7 | Schema/model/source review passes, GET/list SQL excludes PII column, old singleton history/default count correct; no paid checks/real providers/local schema execution |

Local checks: pure Core/crypto/provider-HTTP-message-handler unit; exact serialization-only BookingEventCompatibilityTests; lightweight FlightsApiFixture HTTP; TS/forms/service/decoder tests, fictional demo and architecture/build checks after fixture inspection. PostgreSQL/Marten/outbox/concurrency/rebuild/Host/Aspire tests remain CI-only. Test actual awaited cancellation exceptions, not synthesized assertion-helper exceptions.

## 11. Sources, self-review and approval gate

Primary provider references checked 2026-10-03: [adult search and passenger IDs](https://duffel.com/docs/guides/getting-started-with-flights), [offer hold/document capabilities and total price](https://duffel.com/docs/api/v2/offers), [order cardinality](https://duffel.com/docs/api/v2/orders), [hold/payment flow](https://duffel.com/docs/guides/holding-orders-and-paying-later), [response uncertainty](https://duffel.com/docs/api/overview/response-handling), [current payment status/receipt contract](https://duffel.com/docs/api/v2/payments). These are documentation reads, not supplier acceptance.

Official SDK pinned at `008ae662bb0d50692585a66c7d543d52fba90ed1`: [OrdersTypes](https://github.com/duffelhq/duffel-api-javascript/blob/008ae662bb0d50692585a66c7d543d52fba90ed1/src/booking/Orders/OrdersTypes.ts), [shared title/gender types](https://github.com/duffelhq/duffel-api-javascript/blob/008ae662bb0d50692585a66c7d543d52fba90ed1/src/types/shared.ts), [Payments](https://github.com/duffelhq/duffel-api-javascript/blob/008ae662bb0d50692585a66c7d543d52fba90ed1/src/booking/Payments/Payments.ts). No SDK dependency is added. The current payment API reference takes precedence over the older SDK shape, notably for `status` and `order_id`; API documentation explicitly warns against blindly retrying a 500 payment response. No verified supplier idempotency guarantee was found; safety does not rely on one. Existing short local/demo timeout settings are not proof of readiness for real supplier latency.

Self-review and independent scope check separated necessary M2 contracts from production-grade recovery. Removed durable Started/Stopped/ConfirmationState, the hold-result endpoint, global coordination and new compensation machinery. Retained body cap, truthful price/receipt/error handling, explicit party identity/privacy and current-tab protections. No application tests or implementation validation are claimed by this design record. The [A plan](../plans/2026-10-03-flights-m23a-booking-safety.md) and [B plan](../plans/2026-10-03-flights-m23b-multi-passenger.md) implement this reduced scope.

The user's conditional approval authorizes this non-overengineered A then B through implementation/tests/independent review/commit/push/PR green CI/merge/dev-sync/own cleanup. Only B needs PassengerCount migration sources/offline SQL review; no local schema apply. The chat objective remains all M2, with M2.4/M2.5 following as bounded reviewed increments. No real integrations, deployment, keys or M3 are authorized.

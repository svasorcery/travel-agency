# 0024. Protect new Flights passenger snapshots and webhook bodies

**Date:** 2026-10-02
**Status:** Accepted
**Deciders:** user approval of the M2.2 specification and implementation plan

## Context

The M1 one-passenger flow serialized `PassengerInfo` in the hold command, `OfferHeld` and EF projection. The inbox stored supplier JSON verbatim. Encrypting saved profiles alone would leave these copies exposed. The platform remains a fictional demonstration; protection of new writes does not retrospectively protect existing events or backups.

## Decision

Protect the validated HTTP passenger before message-bus dispatch. `HoldOfferCommand` carries an opaque Core `ProtectedPassengerSnapshot`; its Infrastructure protector binds format, booking and authenticated owner. After existing state/owner guards, the handler decrypts for the provider call and appends singular `OfferHeldV2` using the same ciphertext. Pure replay never decrypts. Legacy `OfferHeld` retains identity and behavior; a future passenger party requires a further event version.

Projection copies the protected envelope into existing `passenger_info_json`, validates owners across V1/V2 checkpoints, and preserves the legacy plaintext representation on rebuild. Normal Get/List SQL excludes that column. No schema migration or historical rewrite is required.

After signature validation and duplicate detection, inbox ingestion encrypts the exact original bytes in `travel.flights.webhook.v1` JSON. Inbox ID/source/event ID/type/provider order ID are purpose-bound to ciphertext. Infrastructure decodes normalized facts for Application. Diagnostics read routing metadata without keys; only authenticated decoding authorizes processing. Already-processed entries need no key. Missing keys fail before ingestion commit or leave processing unacknowledged; existing transient 1/5/30 second retry then DLQ and terminal-envelope policies apply.

Use a dedicated ASP.NET Core Data Protection provider, stable application name `Travel.Flights.Pii.v1`, durable filesystem key ring and certificate protection of key XML. It owns a private DI container and does not alter global auth services. Normal calls require a nonempty usable ring; explicit key-only initialization occurs before any normal Host/store registration. Retain old keys and private certificates; dependency health only proves availability for new writes. See [key operations](../operations/flights-pii-key-recovery.md).

Provider/SMTP failure boundaries retain safe categories without sensitive message/inner chains. Remove raw payment response and recipient/subject logging. Travel no longer persists quote intents for OIDC; after redirect/reload the user selects and quotes again. Tokens, passenger form and retry state stay in memory. The OIDC SDK's protocol state is outside the Travel intent-storage claim.

## Alternatives

- Saved-profile encryption only leaves event, command and inbox copies exposed.
- Encrypting V1 in place changes historic serializer semantics and breaks replay; bulk history rewriting needs a separate retention/data decision.
- A passenger party in V2 couples this privacy slice to provider identity and count contracts before their design.
- A paid KMS or custom cryptographic implementation is unnecessary for the approved free demo. Data Protection supplies authenticated encryption; this decision does not establish production custody or indefinite retention guarantees.

## Consequences and evidence

New PII writes fail closed with safe 503 errors when keys are absent. Search/quote/order metadata and projection remain usable without them. Rollback after new events/envelopes is asymmetric: old readers cannot safely resume; drain old writers/messages before upgrade and prefer a compatible forward repair. Do not convert legacy plaintext commands automatically.

The exact-body unkeyed SHA-256 idempotency fingerprint remains unchanged and may permit candidate correlation; identifiers and routing metadata remain visible. Process-memory compromise, historic backups and real production retention are outside this demonstration's protection claim.

Evidence: [approved design](../superpowers/specs/2026-10-02-flights-m22-pii-protection-design.md), [plan](../superpowers/plans/2026-10-02-flights-m22-pii-protection.md), [test/result boundaries](../superpowers/results/2026-10-02-flights-m22-pii-protection.md). Database/Marten/durable Host tests run in existing CI; local validation does not apply schema, provision real keys or contact suppliers.

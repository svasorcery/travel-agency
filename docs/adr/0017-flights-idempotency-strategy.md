# 0017. Flights Idempotency Strategy

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

> **Amended 2026-05-16** — Corrected column name `request_hash` → `body_hash` in the Neutral
> section to match the WS2 Task 2.6 implementation (`IdempotencyKeyEntity.BodyHash`).

> **Amended 2026-09-30** — Reconciled the documented wire contract with the implemented
> `IdempotencyKeyMiddleware`: clients generate UUID v4 keys and repeat exact request bytes,
> while the server currently accepts any parseable GUID and hashes method, route and raw body
> with SHA-256. Canonical JSON hashing and HMAC are not implemented. No middleware change is
> part of the B3 frontend slice.

## Context

The booking workflow exposes three mutating HTTP endpoints: `POST /api/flights/orders/hold`, `POST /api/flights/orders/confirm`, and `POST /api/flights/orders/{aggregateId:guid}/cancel`. These actions can trigger provider or payment side effects that must not be duplicated if the network drops mid-flight and the SPA or mobile client retries.

Two failure modes must be addressed:

1. **Duplicate mutation** — the client sends the same request twice (or the same request arrives twice due to a network retry). Without deduplication, a second `confirm` could attempt a second payment capture.
2. **Phantom success** — the server processed the request and returned 2xx, but the client never received the response. On retry the server must replay the original success response rather than returning 409 or re-executing the side effects.

The aggregate's `Guard*` methods handle mode (1) by making repeated transitions idempotent at the domain level (a second `confirm` on an already-confirmed stream returns `InvalidState`). But they do not solve mode (2): the client receives an error on retry even though the original request succeeded.

## Decision

All three mutating booking endpoints require a client-supplied `Idempotency-Key` header. Clients generate UUID v4 keys; the current middleware validates that the header parses as a GUID but does not enforce version 4.

The server maintains a `flights.idempotency_keys` table with columns `(key, user_id, route, body_hash, response_body, response_hash, response_status, created_at, expires_at)`.

The request pipeline applies the following logic:

1. **Missing key** — if the header is absent, return 400 immediately.
2. **Lookup** — find an existing row matching `(key, user_id, route)`.
3. **Cache hit — same bytes** — compute SHA-256 over UTF-8 `method + "\n" + path + "\n"` followed by the raw request-body bytes. If the stored `body_hash` matches, replay the stored `response_body` and `response_status` with an `Idempotency-Replay: true` header. No downstream handler is invoked. The in-flight row written at step 5 is updated to the completed response; only 2xx responses are cached (error responses are not stored and the key row is deleted on failure).
4. **Cache hit — different bytes** — return 409 Conflict (`Flights.IdempotencyConflict`). A semantically equivalent JSON body with different bytes is still a conflict; clients must retry the exact serialized body with the same key.
5. **Cache miss** — an in-flight placeholder row is written immediately (before handler invocation) to serialise concurrent duplicate requests. After the handler returns with a 2xx response, the row is updated with `response_body`, `response_status`, and `expires_at` (24-hour TTL). On non-2xx the placeholder row is deleted so the request can be retried.

The store exposes `PurgeExpiredAsync` for rows whose TTL has elapsed. This ADR does not claim that a background purge is currently scheduled; operational cleanup needs separate wiring and verification.

The key is scoped per `(key, user_id, route)` so keys cannot collide across tenants or across different endpoints.

## Alternatives Considered

### Option A: Rely solely on aggregate state guards

Let `BookingAggregate.Guard*` methods handle all duplicate requests. A repeated `confirm` on an already-confirmed stream returns `Flights.InvalidState` (409).

Rejected because: guards make a *repeated* operation fail gracefully, but they cannot give the client the *original success response* after a network retry. A client that lost the 200 response will treat the 409 as an error and may surface a confusing message to the user. Furthermore, `QuoteOfferHandler` creates a new stream on each call — it has no prior state to guard against, so a network-retried `quote` would produce a duplicate stream without any guard being triggered.

### Option B: Server-generated idempotency tokens

The server generates and returns an idempotency token in the first response; the client must supply this token in subsequent retries.

Rejected because: the client cannot correlate a retry to its original request without owning the key. If the server's initial response is lost, the client has no token to use on retry and must start over, defeating the purpose. Client-owned keys are the established industry pattern (Stripe, Adyen) precisely because they allow the client to generate the key before sending the request and use it unconditionally on all retries.

## Consequences

### Positive
- **Safe exact-body retries from SPA and mobile** — within the retained-key window, a client that lost a 200 response gets the original response back on retry without re-executing payment or provider calls.
- **Tenant-scoped keys** — `(key, user_id, route)` scoping prevents one user's key from interfering with another user's requests.
- **Time-bounded lookup** — the 24-hour TTL excludes expired rows from replay. Physical table cleanup depends on invoking the store's purge operation.
- **Defense in depth** — idempotency is checked before the aggregate guard, so the guard only runs when truly needed (first-time or expired-key requests).

### Negative / Trade-offs
- **One extra DB round-trip per mutating request** — the key lookup adds latency on the hot path. Expected to be sub-millisecond on an indexed lookup against a small table.
- **Additional schema surface** — `flights.idempotency_keys` must be migrated, monitored, and purged. The background purge job must be reliable.
- **24-hour window is a convention, not a guarantee** — a client that retries after 24 hours will receive a fresh execution rather than a replayed response. This is intentional and documented in the API contract.

### Neutral
- The `Idempotency-Key` header is only required on mutating endpoints; read endpoints such as `GET /api/flights/orders/{aggregateId}` do not participate in this scheme.
- The `body_hash` comparison uses raw bytes after the method/route prefix. JSON key order and whitespace are significant; the B2/B3 frontend retains the exact serialized body for an uncertain write.

## References

- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md` — aggregate state guards
- ADR 0018: `docs/adr/0018-duffel-webhook-inbox-outbox.md` — inbox/outbox for exactly-once webhook processing (related idempotency pattern)
- Stripe Idempotency Keys: https://stripe.com/docs/api/idempotent_requests

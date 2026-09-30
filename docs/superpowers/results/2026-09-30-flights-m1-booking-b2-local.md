# Flights M1 B2 local result

**Status:** B2 implementation and local verification complete. This report does not establish deployment or database migration.

**Base:** fetched `origin/dev` at `334de7622808c64edce3c0b6c83e8864e9d1dda3` after PR #19. The B2 branch is based directly on that SHA.

## Scope delivered

- Added explicit Keycloak `travel-web` audience mapping and retained the `basic` scope with a `sub` mapper. The local browser callback is limited to port 4200; port 4201 remains an isolated, build-time fictional demo.
- Added in-memory OIDC code-flow/PKCE login, access-token contract checks, post-login re-quote, single-passenger data entry, and hold/confirm requests. Passenger PII stays in component memory and is excluded from URLs, browser storage, and logs. Only a minimal short-lived, non-PII booking draft is used for the real OIDC redirect.
- Hold and confirm each use a separate stable UUID v4 idempotency key; uncertain requests retain the original key and body for retry. A confirmed order is not described as ticketed.
- The demo supports fictional hold/confirm responses, refuses bearer credentials, and does not persist or echo passenger details. No provider, payment, Anthropic, database migration, deploy, or B3 order-status work was performed.

## Authentication evidence

Before the realm fix, the local Keycloak token lacked `aud=travel-web` and a `sub` claim; `flights:book` was present. The exported realm's explicit default-scope list did not contain Keycloak's `basic` subject mapper. The callback already matched `http://localhost:4200/*`; the demo runs on 4201 and is not a real OIDC client.

After the realm update, a local Keycloak 26.6.4 authorization-code flow with PKCE S256 issued an access token with a GUID `sub`, `aud` containing `travel-web`, and `scope` containing `flights:book`. A temporary no-database ASP.NET JwtBearer probe configured with the real Identity audience and `NormalizedIdentityClaimsTransformation` accepted that token, normalized the subject to `NameIdentifier`, and allowed the booking policy. A browser smoke used the real local login and token, re-quoted after login, and verified that the token was not persisted and passenger PII did not enter URLs, storage, or console output. All flight API responses in that browser run were fixtures; this is evidence for the local token and middleware boundary, not a deployed Host/database/provider booking.

The separate port-4201 Playwright demo uses fake auth and fixture APIs. Its green result proves only that the explicitly fictional demo journey works; it does not prove JWT issuance or validation. The demo stub checks idempotency-key format but does not implement middleware replay/conflict or enforce a prior hold before confirm; those backend behaviors are covered separately by .NET tests.

## Self-review corrections

- Matched booking errors to the actual backend ProblemDetails contract: the error code is in `type`, `Flights.OfferExpired` is HTTP 400, and `Flights.HoldExpired` is HTTP 409. Only `Flights.IdempotencyInFlight` offers same-key retry after 409. Body conflict and other booking-state conflicts stop new requests and require manual review. Passenger validation codes now reach their fields.
- Required HTTPS for both the non-local application origin and Keycloak URL. An explicit staging runtime mode shows the non-Production test-wallet warning. A rejected Keycloak initialization can be retried from the same tab.
- Added a route from an expired quote in the passenger form back to re-quote, clearing component PII. An expired hold after confirm returns to a fresh search. If token refresh fails after an uncertain write, the attempt stays in memory for a transient retry in the same tab; a terminal auth loss requires manual order recovery because a redirect would discard the in-memory key/body and B3 status lookup is not implemented.

## Verification

- Angular web tests: 87 passed.
- TypeScript API-client tests: 17 passed; API-client and web lint passed.
- Flights demo Node tests: 14 passed; Chromium Playwright: 7 passed.
- Production SSR/prerender build passed and includes a lazy Keycloak chunk. Flights-demo SSR/prerender build passed with build-time fake auth and no Keycloak chunk.
- .NET Identity unit tests: 25 passed; Host Flights HTTP contract tests: 23 passed; Flights idempotency middleware integration tests: 10 passed. The idempotency tests used disposable PostgreSQL and `EnsureCreated`; no EF migration was applied.
- Biome CI reports no errors; its remaining diagnostics are informational `useLiteralKeys` / `useTemplate` notices in touched and pre-existing files. CSharpier and `git diff --check` passed.

## Boundaries

No non-local issuer has been supplied or exercised. Non-local use requires public runtime OIDC configuration at deployment time; local token verification is not a substitute for a deployed end-to-end check. The existing Host registers only the test-wallet payment gateway outside Production, so a real Production confirm path is not ready. No real provider, payment, ticketing, or B3 status path was called or implemented.

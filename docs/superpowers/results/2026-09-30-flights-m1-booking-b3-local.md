# Flights M1 B3 local result

**Status:** implementation verified locally for review; no deployment or migration application.

**Base:** fresh `origin/dev` was `d37fac5d7b6bda4ba0093bb9de8636426cd79496` (merge of PR #20). The managed `flights-b3-order-status` worktree and `codex/flights-b3-order-status` branch were created directly from that SHA; ancestry was verified.

## Delivered

- Added a typed, runtime-checked `GET /api/flights/orders/{aggregateId}` client and Client-rendered `/flights/orders/:aggregateId` page. B2 confirm hands off only the aggregate ID and immediate `Confirmed` outcome in memory. Tokens stay in memory; passenger data is not added to URL, browser storage or logs.
- After hold, the B2 panel offers a link to the order page so the owner can retain the direct URL while still seeing the server-provided hold deadline and separate confirm action in the current panel.
- The page distinguishes Held, Confirmed, Ticketed, Cancelled and Refunded. It shows ticket numbers only for Ticketed. `BookedAt` is labelled as hold creation, not payment or ticket issuance. A reopened Held order has no invented `HeldUntil`; its explicit confirm action lets the backend check expiry and retains the same idempotency key/body if the outcome is unknown.
- Automatic GET polling is limited to 30 seconds with a two-second interval and manual refresh afterwards. A 404 or stale Held following a known successful confirm is shown as projection lag. An unrelated direct-link owner 404 is unavailable. Route changes cancel the prior GET and do not display its result.
- Direct reload prompts for login when the browser has lost its in-memory token. The same order route is used as the OIDC callback URI only for a validated GUID path; arbitrary return paths fall back to `/flights`.
- The isolated demo stub now serves fictional order GET responses that converge through 404, stale Held, Confirmed and Ticketed. It stores no passenger details and still rejects bearer credentials. This stub does not enforce a real owner or idempotency replay contract.
- README, current architecture, request catalog and ADR 0017 were aligned with the executed behavior. ADR 0017 now distinguishes the client's UUID v4 convention from middleware's GUID acceptance and documents its raw-byte SHA-256 hash instead of HMAC/canonical JSON.

## Local verification

- `npx.cmd nx test api-client --skipNxCache`: 20 passed.
- `npx.cmd nx test web --skipNxCache`: 105 passed, including direct URL/reload auth, owner 404, 401/403, malformed response, stale projection, terminal states, same-key confirm retry, route cancellation and bounded polling.
- `node --test tools/demo/flights-search-api.test.mjs`: 15 passed.
- `npm.cmd run test:flights-demo`: 15 Node and 7 Chromium Playwright tests passed; the browser journey reached fictional Ticketed and survived reload. Desktop and 360px mobile screenshots were inspected. A duplicated carrier code found in the visual review was corrected and covered by an Angular test.
- `dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --maxcpucount:1 --filter "FullyQualifiedName~ReadmeRequestExamplesTests|FullyQualifiedName~FlightsEndpointsHttpTests"`: 32 no-database HTTP tests passed. The build emitted pre-existing ASP.NET route-analyzer AD0001 warnings.
- Production and `flights-demo` Angular SSR/prerender builds passed. The production build contains a lazy Keycloak chunk; the demo build uses the build-time fake auth replacement.
- API-client and web ESLint, Biome error-level CI, CSharpier, request-catalog tests and `git diff --check` passed.

## Evidence boundary and remaining gates

The B3 self-review found and fixed three client defects: a later denied GET left the prior order's details visible; the immediate command hint was not bound to the authenticated owner; and a login finishing after route navigation could reopen the previous order. The page now also hides prior details immediately on an identity change and discards an in-flight GET response from the previous owner. Regression tests cover these cases. `Ticketed` with no ticket number is treated as a malformed response rather than rendering an empty ticket list.

The browser path uses only fictional data and fake auth. The B2 local Keycloak claims probe remains prior evidence for `aud=travel-web`, GUID `sub` and `flights:book`; B3's order-specific redirect URI was unit-tested and is permitted by the checked-in local wildcard, but was not exercised against a live issuer in this slice. A deployed issuer must allow that callback path.

The real Host/OpenAPI integration fixture starts a Development Host with schema initialization, so it was not run under the no-migration instruction. Its source was updated for the new GET catalog entry, and the no-database HTTP fixture verified the same route and response fields. Actual EF projection convergence and owner enforcement on a running Host/database remain separate acceptance gates. No real supplier, payment, Anthropic or ticket issuance was called.

**Recovered-Held concurrency boundary:** after reload, the existing read model cannot distinguish a fresh Held order from a stale Held while a prior confirm is still in flight. The approved B3 behavior allows an explicit confirm from Held; a new browser session cannot recover the previous HTTP idempotency key. The backend checks aggregate state before side effects and passes a stable aggregate-ID key to payment authorization and provider confirm, but its concurrency test shows concurrent confirms can both reach payment capture and provider confirm before one Marten append loses. This is documented backend behavior, not proof of a real payment path. Stricter production recovery would need an authoritative in-flight/read contract or persistent server-side command status/idempotency design before enabling this path with real money; a client-only delay would not prove safety.

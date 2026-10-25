# Design

## Context

The failing PR76 receipt identifies `Network.getResponseBody` resource loss during `observeJsonResponse`, before status/body assertions. Installed Playwright 1.64.1 reads that renderer-held resource after response completion. Ordinary JSON/text/stream consumption with and without abort passed 72 synthetic checks. A controlled renderer-navigation/observer-delay fixture reproduced the same missing-resource error in all six original-observer attempts. This establishes the vulnerable lifetime dependency, not a proven historical navigation in PR76.

## Goals / Non-Goals

Keep the exact one-request response as the evidence for all existing candidate assertions, while retaining the ordinary browser, Host, identity, generator, persistence and restart journey. No response fabrication/replay, API readback substitution, runtime changes, dependency update, longer timeout or automatic retry.

## Decisions

1. Use a short-lived Chromium CDP session around only the exact loopback candidate URL. At Fetch response stage, read the finite original body before continuing that response with no status/header/body override. Keep the native Playwright response for metadata and require one matching POST/main-frame response. The browser still decodes and displays that same response; all existing assertions remain.
2. Bound retained data to the existing 128 KiB candidate contract, reject unexpected status/identity/redirect/duplicate/invalid or oversized JSON, and release the observer on every outcome. Do not expose raw events, credentials or payloads in diagnostics. Request/response deadlines and zero retries remain at their existing settings.
3. Add an actual local HTTP/browser fixture, separate from the real-stack journey. It checks original nonce/body fidelity, one POST, unchanged browser-visible bytes/headers, renderer resource loss, adverse responses, action failure and cleanup. Run it in the existing ordinary Trips test lane, with no API mocking in the real-stack test.
4. Eager cache reads are insufficient (retained CI failure and deterministic fixture). Re-fetch/retry can produce a different candidate and violates one-request proof. UI-only/readback checks would omit original response assertions. Broad cache/protocol retention or dependency replacement is unnecessary for this bounded response.

## Risks / Trade-offs

- Response-stage observation delays delivery of one finite body until captured. This is test instrumentation, not representative network timing; real generator completion and application assertions are retained. It must not become a general streaming observer.
- Chromium CDP is engine-specific, matching this suite's explicit Chromium project. Actual browser regressions and the pinned installed browser validate the protocol path.
- The exact historical reason for resource loss is unknown. This repair removes the renderer-cache dependency rather than asserting an upstream Chromium defect is fixed.
- Failure cleanup must not strand a paused request or swallow an error. Dedicated adverse fixtures and the real stack's existing owned cleanup cover that boundary.

## Validation and delivery

Start with a failing original-observer browser regression; then verify capture integrity and error behavior, focused unit/lint/type/structural checks, complete real Host/Keycloak/PostgreSQL E2E and independent source review. Publish a separate PR from fresh dev `b84287280156f615fa006fb46fa85ee7ca2e7158`, require green ordinary CI and leave it unmerged. V1 bytes, frozen materials/A/B scores and retained demo remain unchanged.

## Protocol references

- [Playwright CDPSession](https://playwright.dev/docs/api/class-cdpsession): public protocol access and session lifecycle.
- [Chrome Fetch protocol](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/): response-stage read and continuation without overrides. Installed Playwright protocol declarations confirm these contracts.
- [Playwright issue 42862](https://github.com/microsoft/playwright/issues/42862): related body-observation discussion; it is supporting context, not proof of this CI failure's precise trigger.

# Proposal

## Why

PR76 CI attempt 1 failed during original candidate-response observation with Chromium `body-resource-missing`, before candidate assertions; its unchanged-head retry and post-merge run passed. Eager `response.json()` still depends on a renderer resource whose lifetime is shorter than the test's observation window.

## What Changes

- Capture the one actual candidate POST response at the Chromium response stage before releasing it unchanged to the browser; retain all current JSON/status/UI/no-write/restart assertions.
- Add real-browser regression fixtures for resource loss, original-byte/request identity, rejection and cleanup. No repeated POST, API replay, fabricated response or weakened assertions.
- Retain failed and successful evidence separately, validate the real isolated stack and publish a separate PR into dev without merge.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This repairs test instrumentation for the existing `trips-local-verification` contract, without changing its requirements or application behavior; `skip_specs: true` is explicit.

## Impact

Only Trips E2E observation/regression tests, their test commands/configuration and this change's evidence. No production code, dependencies, timeout increases, retry setting, authentication replacement, generator/data release or retained interactive-demo changes. Human scores remain blank; v2 and real-user enablement stay out of scope.

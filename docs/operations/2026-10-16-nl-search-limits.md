# NL-search admission and accounting limits (2026-10-16)

The anonymous v1 endpoint and pinned `claude-opus-4-7` model remain unchanged. Host waits up to its existing 6 seconds for Core NATS request/reply. AI handler work has a separate 5-second completion budget across storage admission, inference and final accounting. A discarded AI failure has no automatic failure acknowledgement in pinned Wolverine; the caller can therefore wait its existing 6 seconds and use the existing safe failure path. These are separate intervals, not an end-to-end 5-second promise.

## API and model bounds

The API rejects empty, whitespace-only or null queries and queries over 1000 characters before dispatch. Its 16 KiB body guard runs before binding, including mixed-case/trailing-slash routes and unknown-length/chunked bodies. A process-wide sliding-window limiter admits 6 NL HTTP requests per minute with no waiting queue. `Flights:NlSearchRequestsPerMinute` can lower this to 1–6. Host owns routing/security middleware; Flights contributes its named policy and endpoint metadata. Rate slots can also be consumed by subsequently rejected requests.

Travel.AI independently validates direct-NATS input. `NlSearch` configuration can lower these validated ceilings: 1000 query characters, 4096 conservative input tokens, 512 requested output tokens, 16 KiB response, 3500 ms inference and 5000 ms whole handler. Input accounting includes the complete system prompt, date-bearing user message, JSON schema/name/description, model/options and 128 bytes of framing reserve. One UTF-8 byte is treated as one possible token; input is rejected rather than silently truncated. Output checks retain observed usage even when a noncompliant response exceeds token/text bounds. The SDK and NL-specific Wolverine delivery policy make no automatic inference retries.

## Local admission and uncertainty

The AI process admits one active operation and 6 admissions per rolling minute. Each admission reserves the full configurable maximum (default $0.10), within a default $1 rolling-hour estimate budget. The full input/output upper estimate must fit that per-call maximum. Known durably recorded usage settles to its estimate; unknown or unsettled usage keeps its full reservation until the rolling hour expires. These are local admission estimates: restarts reset the in-memory history and replicas multiply the limits. They do not prove distributed ingress protection or actual provider billing.

Every admitted model operation first commits an `Admitted` ledger row with nullable usage/cost. A pre-admission storage failure makes zero model calls. Failed final accounting retains that existing unknown row and never returns success. The private operation owns its async scope, DbContext and active lease until storage settles, even when storage ignores cancellation after the bounded outer handler returns. It checks cancellation after a late initial save before inference.

A model task ignoring cancellation retains the active lease until it settles. Its continuation only observes completion/releases the lease, and never uses a disposed DbContext or writes a late invented cost. Such late usage can remain durably unknown and requires reconciliation, not inference replay. See the [migration/compatibility notes](2026-10-16-ai-usage-accounting-migration.md).

## Estimate provenance

New NL estimates use Opus 4.7 standard-token prices of $5 input / $25 output per million tokens, verified on 2026-10-16 against [Anthropic pricing](https://platform.claude.com/docs/en/about-claude/pricing). One shared source supplies admission and ledger arithmetic: 100 input + 50 output = estimated $0.00175. Historical numeric values and `LegacyRecorded` rows are unchanged. Cache pricing, other tariff modifiers and actual bills are not established by this estimate. No credentials, billing systems or paid model requests were used for verification.

These source/disposable-test limits do not establish live deployment, external proxy limits, model quality or real provider/payment acceptance. Laya remains default-off for real Support traffic, with the existing experimental/manual warnings and failed quality gate retained.

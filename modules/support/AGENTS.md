# Support module

S1 implements private manual customer/operator conversations. Source acceptance and CI/target evidence are recorded in the [canonical change](../../openspec/changes/support-s1-case-workflow/verification.md). Laya S2/S3 is research only; no classifier, booking action, supplier call or money movement belongs to this workflow.

Core owns validation and transitions; Application owns guard-before-decrypt/replay, common action identity and synchronous decisions; Infrastructure owns EF transactions, certificate-protected text and bounded retention; Api owns DTOs, authorization and its Composition facade. Host owns global builders, authentication order and its single endpoint map. The exact module inventory is six modules/four layers (24 assemblies).

Customer owner identity comes from the validated principal. Operator routes require both `support:operator` and `azp=travel-support-operator`; customer and operator auth contexts remain separate. Receipts disclose metadata only to the actor and route provenance that committed the action. Positive retained receipts confirm an action; absence, timeout, unavailable protection, expiry or permission denial never proves NoEffect.

Resolved cases expire after90days. Reads, writes and receipts conceal expired content before decrypt; retention physically deletes only Support-owned records in batches≤50, at most1000 per pass. Reopen/delete decisions compare current revision and expiry.

Title/body protection binds owner/case/kind/message/written-at revision. Normal operation never initializes an empty ring. The explicit key-only command and recovery procedure are [operational source](../../docs/operations/support-text-key-recovery.md), not evidence of target provisioning. New schemas/keys/scopes, migration application, publication and runtime changes still require applicable authorization.

Flights order linking uses only [the published leaf](../../shared/dotnet/Travel.IntegrationContracts.Flights/SupportOrderAccess.cs). Flights reads its authoritative booking stream; Support never imports Flights internals or trusts the lagging projection. Only the two reserved pre-write OrderLink errors allow an explicit fresh unlinked create; generic errors retain uncertainty.

Tests: [unit](../../tests/support/Travel.Modules.Support.Tests.Unit), [integration](../../tests/support/Travel.Modules.Support.Tests.Integration) and [HTTP/issuer source](../../tests/Travel.Host.Tests.Integration/Support). Local .NET entry points must be audited first; actual certificate/PostgreSQL/Keycloak fixtures and full Host/Aspire remain disposable ordinary CI acceptance.

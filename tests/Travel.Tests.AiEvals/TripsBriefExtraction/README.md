# Trips brief extraction: fictional evaluation

This folder contains the exact published M3 qualification48 packet, a strict loader,
a bounded opt-in runner and the production extractor/normalizer seam. It does not
select a model, discover a credential or run an inference during ordinary tests.

Run the mechanical checks from the repository root:

```text
dotnet test tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj --filter FullyQualifiedName~TripsBrief --maxcpucount:1
```

These tests use a scripted pipeline, including a simulated24-case packet copied
from public cases to exercise schema/custody controls. That simulation is **not**
an independently authored or unseen holdout, model-quality evidence or a real run.

## Artifact and runtime binding

The qualification JSON transcribes D01–D24/H01–H24 from the active change's
evaluation-plan.md. Each original literal text and compact oracle is retained
alongside typed expected states/values/issues. The canonical48 source rows hash is
`ed0ef5cb4a3a407b4b4d988bf4be466ae79ec549b129f7e2f0fa2f75de446052`.
Only explicitly listed alternatives are exact-scored (D13); other alternatives
still pass independent admissibility checks.

A real run requires a new explicit user authorization with source tree hash,
packet SHA256, actual embedded prompt SHA256, exact configured model and verified
prices, maximum calls, total USD allowance and allowed wall-clock duration.
`TripsBriefEvaluationRunner.RunAuthorizedAsync` rejects missing authorization,
changed packet/prompt/model binding and exhausted total/hour/request allowance
before dispatch. RunReceiptDirectory is a stable, explicitly approved local evidence directory across restarts. Before any dispatch an atomic CreateNew metadata-only claim consumes the authorization reference; it is kept on success, failure or crash and is never automatically removed. Reusing the authorization, even with a new RunId, fails without a second call. Changing/resetting the receipt directory requires new authority. A new authorization can name exact ApprovedCaseIds for diagnostic remaining work; unexecuted/prior cases stay NotRun in the original denominator and cannot produce a full quality pass. It uses the supplied production pipeline; it neither constructs
a provider client nor reads environment secrets. An accepted policy/model config
is separately required by the ordinary production extractor.

`ProductionTripsBriefEvaluationPipeline` delegates to the same
`TripsBriefExtractor` and EF-ledger seam. The runner then invokes the production
Trips candidate validator/normalizer. Corpus reference anchors are fixed input
data; runtime TimeProvider deadlines/accounting/monotonic duration remain real.
No public API clock override exists. Per-case timings exclude actual
Host/NATS/browser transport; they are component-pipeline evidence only.

The qualification48 schedule is6windows×8 at0/65/130/195/260/325minutes,
at least11seconds between calls and6hours overall. A later sealed24 packet uses
3windows/3hours, IDs E01–E24, partition holdout,12prelabelled useful cases,
hazards/oracles and a separate independent custody attestation. The actual unseen
packet must be independently authored/sealed after source/prompt/model freeze
and before outputs; it is not authored or opened by this implementation.
Attestation metadata is a required external evidence reference, not automatic
proof of independence.

Before each dispatch the runner rechecks stop/binding/pacing, reserves the full
estimated call against authorized total, <=USD0.03/request and
<=USD0.30/rolling-hour allowance, and never retries. Unknown usage/model/cost,
interruption or inability to finish stops the run; remaining IDs stay NotRun in
the original denominator. The complete receipt retains per-case normalized
fictional results, controlled outcomes, timings, known/unknown usage/spend,
stop reason and bound source/configuration identities. It makes no invoice or
real-user quality guarantee.

Passing fictional mechanical tests is **source mechanics only**. Actual model
qualification, independent holdout, human quality, processor/private-text
permission, target readiness and rollout remain separate. No real evaluation has
been executed here.

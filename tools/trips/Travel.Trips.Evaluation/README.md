# Trips fictional evaluator

Standalone source/test preparation for Trips M3 task6.3. It reuses the production
extractor, metadata-only EF ledger and Trips candidate validator. No Host, NATS,
UI or retained demo is started. Model selection, price/spend authorization,
qualification, private-text processing, target and rollout remain pending.

## Source-only commands

From the repository root:

```text
dotnet run --project tools/trips/Travel.Trips.Evaluation -- preflight --repo-root .
dotnet run --project tools/trips/Travel.Trips.Evaluation -- prepare-source
```

Default is preflight: fixed source/build inspection, bounded JSON,
ReadyForExecution=false and ProviderCalls=0. No Docker/client/ledger/key access.
CI runs only this command after the ordinary solution build. Optional --manifest
PATH validates strict v1 metadata; unknown/duplicate members and changed/dirty
source, binary, policy, model, prices, caps or expiry fail admission.

Explicit prepare-source publishes the worker and prepares a runtime from the two
pinned Trips images. It uses Docker/build resources, but no key or model. Those
build resources and the host/Docker control plane are outside the runtime data
plane quota. Preparation metadata lives in ignored out/eval-prep/envelope.
The runtime image is stable; published application files are bound read-only.
Before a later approved run, prepare again on the final clean committed source,
review observed hashes and freeze the exact manifest. Current dirty source cannot
execute. Preparation or a manifest's Authorized=true is not human permission.

## Explicit wire projection

trips-anthropic-wire-v1 removes only maxItems from the provider schema while
retaining compact $defs/$ref. The native Anthropic12.20.0 request factory bypasses
its automatic lossy schema transformer. Full local schema and parsing bounds are
unchanged: oversized issues/evidence/alternatives still fail InvalidOutput.
Actual fake HTTP checks all48 SDK requests, the wire schema and complete envelope
bound. This proves serialization and local rejection, not provider acceptance,
actual tokenizer overhead, pricing or model quality.

## One runtime budget

The .NET worker, PG and child processes share one Linux cgroup: CPU2, RAM2GiB,
swap0; nonroot UID999, drop-all capabilities, no-new-privileges, read-only root/app,
no published port, no Docker logs or reaper sidecar. The worker checks cgroup,
PG/worker membership, actual tmpfs capacities and RLIMIT_FSIZE before WORKER_READY.
The host separately checks exact image, ownership and both bind sources.

Total writable storage is256MiB: PG112 + socket4 + /tmp12 + scratch32 + shm32 MiB
(tmpfs192MiB) plus one preallocated persistent64MiB evidence file. The file is the
only writable persistent bind; its parent is read-only. Kernel fsize64MiB prevents
growth. A32MiB fsize trial caused runtime OutOfMemory at about130MiB cgroup peak
with zero kernel OOM events;64MiB passed without increasing the aggregate budget.
This is consistent with a JIT backing-file limit; no native-call trace was taken.
Source probes test both archive-growth rejection and actual scratch ENOSPC.
An in-container supervisor ends even an orphan after6h, with up to5s kill grace.
Tmpfs/PG are disposable; the bound journal survives. This is not a whole-host cap.

## Evidence, timing and future execution

One fixed archive consumes the authorization reference atomically. Immutable
<=64KiB intent/result/index payloads use flushed commit markers and hashes.
Torn/unmatched intent remains uncertain; claims and prior evidence are never
removed or resumed. The physical approved evidence root must have no links.
The older file journal remains a mechanical fixture, not the runtime composition.
Only normalized fictional fields, controlled outcomes, accounting and diagnostics
are retained; no input, prompt, completion, key or exception bodies.

The future execute branch requires --manifest PATH and
--confirm-manifest-sha256 HASH after separate explicit human authorization.
It transfers the remaining monotonic authority after the kernel handshake and
resolves only TRIPS_EVALUATION_ANTHROPIC_KEY, last after repeated source checks.
No implicit credentials/config fallback exists. All extraction, ledger,
normalization and receipt writing run inside the shared envelope. Cancellation or
worker loss triggers exact owned-container cleanup; uncertainty never permits
retry. Source probes use network=none and fictional fake HTTP; no key was read.

Actual SDK starts share one monotonic schedule: original six windows, >=11s
spacing, <=6h and the original authority remainder including setup. Component
latency includes normalization; pacing/journal I/O consume overall time. Immutable
summary is always Passed=false/FinalizationPending. Only a matching returned
PostFlushVerdict with RunId/index hash can support later review; a crash before it
grants no acceptance. CLI stdout after the verdict timestamp is outside Duration.
This excludes Host/NATS/browser latency and proves no model-quality result.

See the [source receipt](../../../openspec/changes/trips-m3-brief-confirmation/source-preparation-verification.md).

# Independent source review

Reviewed the complete change against base `6f50ce5796f2ed3d559ce69b8ec5adc579d732a1`, followed by a narrow recheck of the final expiry guard. Review was read-only; it did not mutate runtime, databases, network sources or retained demos.

One Important boundary was identified: a frozen build/validation instant could permit a success receipt when the package expired during preparation. `withReceipt` now requires the actual completion instant to precede the strict reader's `releaseValidUntil`, covering both source and original POI identity expiry. The regression reached the exact expiry during validation: RED was a missing expected rejection (13/14); GREEN was 14/14, with a failed receipt verified. Recheck closed the finding. No open Critical, Important or Minor findings remain.

Review covered fixed-source and same-GET provenance, bounded/exclusive acquisition, all six output files, helper stability, read-only strict validation, complete historical JSON/ETag/persisted fields, source replacement/refusal, Host restart and explicit CAS editing. Production package, descriptor/composition, public assets and existing builder have no diff against base.

New source/v2 suitability, human route quality, rights acceptance and target enablement are outside this mechanical/source review. Actual v1 reproduction and local test receipts are recorded in [verification](verification.md); final PR CI is a separate delivery gate. The review does not authorize merge or activation.

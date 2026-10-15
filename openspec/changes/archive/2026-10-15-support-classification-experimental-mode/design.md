# Design

## Context

See proposal.md and the support-classification-suggestions delta. S2 already separates request, category prefill and a current-revision manual save. Its provider and application already enforce a 10-second deadline. The historical runtime evaluation is negative; this change implements the user-approved experimental presentation and local application check.

## Decisions

- Treat the existing operator suggestion as experimental throughout its UI lifetime; add a persistent visible warning associated with the request control. Reuse existing styling and the existing two-step prefill/save flow rather than adding a dialog or another state machine.
- Keep the production question support-category-v1, forced multilingual Laya, eight categories, nonzero guards 0.75/0.60, HTTPS/auth/profile validation, input bounds and expiry checks unchanged. A/B definitions remain experimental analysis artifacts and are not promoted.
- Update the current contract and operations guidance to allow explicitly accepted limited experimental use without calling the previous quality result successful. Default-off and separately approved target/private-text provisioning remain in force.
- Extend the nearest real-browser scenario: warning before/after request, customer exclusion, exactly eight candidate categories plus Unclassified, unchanged persisted data after request/prefill, one operator save and no automatic replay. Use loopback-only fictional API/model fixtures.
- Run existing Support unit tests for fixed categories and the 10-second cancellation behavior. No new model evaluation, holdout access, paid API, Host/database startup or deployment is part of this check.

## Risks / Trade-offs

- Experimental labelling does not improve accuracy or prove operator-time savings. Operators can still trust a wrong suggestion; keep the warning and require a separate manual save.
- Fixture E2E proves product interaction only. Preserve the failed synthetic evaluation and disclose that this application check does not execute Laya.
- A new quality acceptance policy must be explicit. Preserve historical evidence and describe this as an approved experimental exception, not a retrospective pass.

## Delivery and rollback

Source-only changes and local synthetic checks are authorized in this turn. Deployment and processing real Support text remain separate. Remove the UI change to roll back presentation; the product default remains disabled. No schema or migration is required.

## PR CI follow-up — 2026-10-15

User explicitly authorized PR publication and fixing CI while keeping real Support traffic disabled. Initial run 38736240752 on 50a407ad67a08aa7c74c49ba840cb23a6ffba069 failed restore with NU1902/NU1903: MailKit 4.16.0 -> MimeKit 4.16.0 -> System.Security.Cryptography.Pkcs 10.0.0. Both GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx identify 10.0.13 as patched. MailKit/MimeKit latest 4.18.1 still permits the vulnerable 10.0.0 floor, so updating the mail client alone does not resolve it.

Require PKCS 10.0.13 centrally and add the explicit override at the existing Flights Infrastructure MailKit consumer. Keep the mail client version, auditing and warning-as-error policies unchanged. This avoids enabling global transitive pinning, replacing mail infrastructure or suppressing security checks. Validate restore and actual resolved versions across the solution, then compile/test and wait for the new exact-head PR CI. No live mail, supplier, model, Host/DB or deployment execution is authorized by this fix.

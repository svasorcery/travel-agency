# CI receipts

PR41: https://github.com/svasorcery/travel-agency/pull/41

Implementation head65ff004bca178611aaac76844c2493423010c323, existing PR CI38134076012. First attempt failed; it is not acceptance.

PASS: lint,build-dotnet,frontend-affected,test-contract,test-identity-unit,test-ai,test-architecture,test-aspire-smoke. FAIL: test-flights-unit(1/1373),test-host-http(1/394),test-host-integration(2/52),test-flights-integration(6/397). Paid evals skipped by policy; PR-E2E skipped duefailed prerequisites.

Public normalized Host OpenAPI extracted from job114454441763 generator diagnostics;98905UTF8bytes. Baseline copied from actual generator, not fabricated. Same implementation source production contract unchanged in fixture corrections.

Architecture pre-push audit: original user restriction prohibits local Host/AppHost/DB/schema/key actions; no module initializers, assembly fixtures, process/db startup in Architecture suite. Its metadata/IL-only170tests passed before ordinarypush. Earlier plan wording existing-project-only-CI was an overbroad guideline; audit and user instruction take precedence. No hook bypass.

## Accepted implementation

f68d8139e8ab79081be67f7f3bf124cd00e59603 — CI38136072675 success,13normal jobs success,paid skip.

- FlightsUnit job114460355440:1373PASS.
- FlightsIntegration job114460355426:397PASS,5m18s.
- HostHTTP job114460355410:394PASS.
- HostIntegration job114460355465:52PASS,1m8s,including generated snapshot.
- Architecture job114460355908:170PASS.
- AspireSmoke job114460355489:5PASS.
- PR-E2E job114461460136:2PASS,real AppHost/Angular/README smoke in existingCI.
- Lint/build/frontend/identity/AI/contract allsuccess.

Existing workflow unchanged; no test skip/filter narrowing. Final docs/closure head is pending its own CI receipt.

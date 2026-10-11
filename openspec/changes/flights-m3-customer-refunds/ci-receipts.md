# CI receipts

PR41: https://github.com/svasorcery/travel-agency/pull/41

Implementation head65ff004bca178611aaac76844c2493423010c323, existing PR CI38134076012. First attempt failed; it is not acceptance.

PASS: lint,build-dotnet,frontend-affected,test-contract,test-identity-unit,test-ai,test-architecture,test-aspire-smoke. FAIL: test-flights-unit(1/1373),test-host-http(1/394),test-host-integration(2/52),test-flights-integration(6/397). Paid evals skipped by policy; PR-E2E skipped duefailed prerequisites.

Public normalized Host OpenAPI extracted from job114454441763 generator diagnostics;98905UTF8bytes. Baseline copied from actual generator, not fabricated. Same implementation source production contract unchanged in fixture corrections.

Architecture pre-push audit: original user restriction prohibits local Host/AppHost/DB/schema/key actions; no module initializers, assembly fixtures, process/db startup in Architecture suite. Its metadata/IL-only170tests passed before ordinarypush. Earlier plan wording existing-project-only-CI was an overbroad guideline; audit and user instruction take precedence. No hook bypass.

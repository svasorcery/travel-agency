# S2 contract examples

Это source examples, без actual inference или target credentials. Result metadata не доказывают accuracy/quality.

## Request

```json
{"requestId":"22222222-2222-4222-8222-222222222222","expectedRevision":1}
```

POST /api/support/operator/cases/11111111-1111-4111-8111-111111111111/classification-suggestion, private operator scope/client, application/json,≤1KiB; Authorization token never belongs in the body.

## Default Disabled

```json
{"caseId":"11111111-1111-4111-8111-111111111111","requestId":"22222222-2222-4222-8222-222222222222","capturedRevision":1,"generatedAt":"2030-01-01T12:00:00Z","expiresAt":"2030-01-01T12:02:00Z","status":"Disabled","reason":"Disabled","category":null,"queue":null,"servingProfileId":null,"questionVersion":null,"urgency":{"kind":"Unknown","nextScheduledDepartureAt":null,"observedAt":null,"streamVersion":null,"ruleVersion":"support-urgency-v1"}}
```

Candidate differs only with allowed category=queue, reasonReady, validated profile64hex and questionVersion support-category-v1. There is no sourceAI persisted TriageSource. ManualRequired category/queue remainnull; independently verified urgency may propose High. Operator preview/prefill do not save anything; existing SetTriage uses a fresh actionId and current revision. Ambiguous saves retain the S1 barrier.

400/401/403/404/409/413/429/503 remain fixed no-store problems without echoes. Busy429 carries advisory Retry-After60, not retry permission. Deadline/lost inference does not declare NoEffect, and financial order barriers remain independent.

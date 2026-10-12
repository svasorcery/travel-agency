# Source contract examples and enablement

Обычный запрос `/api/flights/orders/quote` остаётся совместимым. Новый optional `explanationMode` принимает `deterministic` или `organized`; отсутствие значения даёт обычное объяснение. Anonymous organized request не отправляет AI message. Unknown string mode даёт controlled fallback. AI-выбор выполняется только внутри явного обычного quote/re-quote.

Пример запроса на тестовых данных:

```json
{"providerOfferRef":"off_test_fixture","provider":"duffel","passengerCount":1,"explanationMode":"organized"}
```

При root conditions missing и leg change allowed с `0.00 GBP` ответ содержит root permission `Unknown`/penalty null, отдельную leg permission `Allowed`/penalty `{ "amount":"0.00", "currency":"GBP" }`. Brand и actual fare basis code остаются отдельными nullable полями. Багаж относится к конкретному взрослому/сегменту; explicit0 не заменяет отсутствие type/quantity. Условие leg не устанавливает разрешение на всё предложение.

`fareSummary` связывает aggregateId/offerId/quoteRevision/fingerprint и original offer expiry/first departure с конкретным расчётом. Controlled sections не обещают бесплатный обмен, возврат полной цены, возврат extras или bank credit. В ответе всегда есть missing full-text/after-departure/no-show/involuntary notices. `AiOrganized` меняет только порядок/группировку разрешённых fact IDs. Disabled/Busy/BudgetExceeded/InvalidOutput/UsageUnknown/AccountingUnavailable возвращают deterministic вариант и объяснимую причину; повторного model call нет.

Existing private creation-reader добавляет `quotedFareSummary`: Historical/Stale либо Unavailable. Он не вызывает provider/AI/decrypt/outbox. Original expiry берётся из frozen snapshot и не заменяется HeldUntil. Старые bool-only/unsupported snapshots не становятся подтверждённым запретом. Both quote writers создают snapshot до commit; replay копирует сохранённый fingerprint/schema/observed time.

Defaults: Host `Flights:FareSummary:OrganizedEnabled=false`; AI `FareSummary:Enabled=false`. Consumer source должен попасть в совместимое окружение раньше включённого producer. Обе стороны используют leaf v1, subject `travel.ai.fare_summary`, queue group `travel.ai.fare_summary.workers`; NL не меняется. Цена модели должна быть отдельно проверена и совпасть с exact model ID. Per-process1inflight/6admissions per minute и per-request budget не являются global/account spend ceiling.

OpenAPI source snapshot дополнен только сгенерированным nullable `explanationMode`. Его схема получена public IOpenApiDocumentProvider/document transformer без Start/Run, listener, Host Program или инфраструктуры. Inline/reference shape остальных DTO не подменялась этим частичным генератором. `FlightsBookingOpenApiTests` дополнен проверками summary/creation/exact-string money; полная реальная Host OpenAPI snapshot acceptance остаётся CI.

Следующие gates: commit/push/PR и ordinary CI/PR E2E после отдельного разрешения; paid/human packet отдельно; deployed streaming/assets/runtime compatibility отдельно. Source/fake acceptance не включает вызов Duffel sandbox или Anthropic и не включает deploy/enablement.

# Fictional customer refunds demo

Demo проверяет интерфейс и retained in-memory Node state. Real supplier/payment/Anthropic,
durable outbox и bank credit он не доказывает. См. [verification](verification.md).

## Запуск

Terminal1, из worktree:
```powershell
$env:TRAVEL_FLIGHTS_PRESET='refund-sent' # refund-zero / refund-result-lost
node tools/demo/flights-search-api.mjs
```
Terminal2:
```powershell
$env:NX_DAEMON='false'
$env:NX_ISOLATE_PLUGINS='false'
node node_modules/nx/dist/bin/nx.js serve web --configuration=flights-demo
```

Открыть http://127.0.0.1:4201/flights, выбрать будущую дату, quote/demo login/re-quote.
Заполнить только вымышленные данные, выбрать2багажа и бесплатное место12A.
Base80GBP+extras20GBP=100GBP за весь заказ. Принять purchase, удержать, подтвердить,
получить условия и явно согласиться на whole-order cancellation.

- refund-sent: «Демо: возврат отправлен»,80GBP, исходный вымышленный способ.
- refund-zero:0GBP/NoRefundDue,0gateway calls.
- refund-result-lost: тот же case80GBP, manual, без repeat payment action.

Supplier settlement и bank credit всегда NotEstablished. GET/refresh/reload не исполняют
workflow. Preset фиксирован при старте; fault/resolve API отсутствуют.
Result-lost показывает уже итоговый manual result без3минут ожидания. Это presentation fixture,
не proof runtime timer. .NET deadline всегда admission+180s.
Node restart очищает demo records; persisted restart проверяется отдельно в CI.

## Проверки

```powershell
node --test tools/demo/flights-search-api.test.mjs tools/demo/flights-customer-refunds.test.mjs
node node_modules/@playwright/test/cli.js test -c tests/travel-e2e/playwright.flights-demo.config.ts
```

65 Node/52 browser cases PASS. Три refund cases стартуют независимые API.
Sent:9взрослых/4legs/services, exact80GBP, staleGET/reload/mobile/privacy.
Покупка для refund browser cases seed'ится через loopback API; actual purchase UI дополнительно
проверяется existing ancillary browser cases. Карточки сохраняются в test output screenshots.
Case/payment/receipt IDs и tokens отсутствуют в storage/URL/clipboard/console;
support fallback использует только order resource.

## Привилегированный разбор

Product GET/POST /api/flights/orders/{aggregateId}/customer-refund/review требует
flights:customer-refund-review. Actor только изJWT. POST: refundId,expectedRevision,
resolutionId,decision,evidenceReference, typed observation/senderStop.
Inconclusive сохраняет uncertainty. Sent требует совпадающей денежной базы/receipt.
ConfirmedNoEffect требует affirmative no-effect/no-pending и quiescence exact old sender.
Exact normalized replay возвращает старый результат; changed payload conflicts.
Review POST не вызывает gateway. Demo не имеет этого endpoint.

Report разбирается через existing cancellation-review scope. GET supplierReportTarget доступен
независимо от primaryTarget. POST existing resolve:targetKind=SupplierCancellationReport,
targetId=reportId,expectedRevision,resolutionId,
decision=ConfirmCancellation/ConfirmNotCancelled/Inconclusive.
Evidence: evidenceRef/category/observedAt и reportEvidence{reportId,providerOrderRef,
cancellationConfirmed,notCancelledConfirmed,noPendingCancellationConfirmed,cancelledAt}.
Report resolution не создаёт деньги и не закрывает competing attempt.

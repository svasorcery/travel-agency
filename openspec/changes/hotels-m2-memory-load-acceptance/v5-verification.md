# V5: проверка диагностического исполнителя — 2026-10-30

Этот документ фиксирует source preparation до запуска. Последующий отдельно разрешённый runtime/анализ записан в [diagnostic-v5-result](diagnostic-v5-result.md).


Выполнено поручение: проверить весь исполнитель на реальных JSON-ответах, исправить ошибки и подготовить v5 **без нового эксперимента**.

## Входы и граница проверки

- Сохранённый search response run-v4/job-0.json использован без изменения; его SHA-256 EB96811475D310B457CE5F558E500C80A6DF506406A082D383EE99DC34FD2B5C.
- Новый HotelDiagnosticJsonContractTests получает 8 search JSON и expired409 ProblemDetails через existing TestServer/owned handlers/in-memory store. FakeTimeProvider advances302s только в тесте. Kestrel, Travel.Host, PostgreSQL, collector и внешняя сеть не запускаются.
- Raw responses, request и capture metadata сохранены в v5-contract-capture/. Их происхождение/хеши — v5-input-provenance.json. Негативные варианты явно получены мутациями этих ответов в тесте.
- Replay загружает определения функций runner через AST без executable bootstrap. Исполняется тот же полный orchestration; подменены только процессы, HTTP transport, часы и локальные resource probes. Создаваемые mock collector files — synthetic placeholders, они удалены и не являются снимками памяти.
- Четыре Hotels assemblies принятой fixture-сборки совпали по hash с assemblies свежих standalone tests. Сам wrapper не перестраивался: его runtime code не менялся.

## Что исправлено и проверено

| Область | Поведение v5 |
|---|---|
| Реальный JSON | Принимает string Complete; отклоняет numeric enum, Limited/Failed/Unsupported, неверные bool/status types, неверные sources/aggregate shapes и запрос |
| Идентичность / TTL | Проверяет уникальные search IDs, непустой propertyGroupKey и ISO expiry; сохраняет исходные8search, cadence6s, wait302s и9jobs |
| Учет запросов | Сохраняет HTTP response facts, failed/cancelled/not-run и terminal timestamps |
| Владение процессами | Единый run-state; Process object регистрируется сразу после старта, до metadata/readiness; cleanup охватывает все созданные процессы |
| Сборщик | Timeout/ошибка/пустой dump не дают успешный snapshot; логи дочитываются после остановки с ограниченным ожиданием |
| Повтор / сохранность | attempt-v5.json атомарно занимает единственный вызов -Execute, включая preflight failure; отдельный run-v5/ сохраняет старые результаты |
| Итоговый результат | Ошибка записи receipt, неполные outputs, cleanup failure и превышение720s дают complete=false |
| Deadline | Проверяется перед стартом процесса/HTTP dispatch и после hashing/cleanup; превышение не скрывается успешными9responses/3artifacts |

Параметры настоящего эксперимента и profile.json не изменены. До отдельного разрешения разрешена лишь команда проверки без -Execute. Финальный attempt marker является результатом всего вызова; acquisition receipt сам по себе не заменяет успешную finalization.

## Проверки и review

| Проверка | Результат |
|---|---|
| Исходный v4 validator на реальном job-0.json | RED: отвергал Complete у обоих sources |
| Тот же unmodified job-0.json через v5 validator | GREEN |
| Standalone API contract capture |1/1passed |
| Первый full-flow replay |33/37; найдены четыре пробела timestamps/type/evidence |
| После их исправления |37/37 |
| Независимый read-only review |0Critical,2Important,1Minor |
| Review RED |37/43; receipt write failure, last-hash/cleanup/first-hash/preflight deadlines и preflight terminal times |
| Финальный full-flow replay |43/43passed |
| Полный разрешённый Hotels standalone suite после fix-pass |136/136passed,0skipped |
| CSharpier |1file checked |

Из aggregate standalone команды исключены только HotelHttpTests и HotelDurablePurchaseTests, требующие PostgreSQL. Сохранилось известное Verify warning об autodiscovery solution, не подавлялось. Ни один результат здесь не является новым load/heap proof.

Review выполнен fresh-context агентом hotels_v5_review (gpt-6-astra/high), один fix-pass выполнен автором с meaningful RED/GREEN и финальным suite. Оба Important исправлены; Minor об отсутствии terminal times у preflight также исправлен в рамках прямого поручения исправить ошибки исполнителя. Deferred minors нет. Повторный review не выполнялся по workflow; covering tests и полный разрешённый suite проверяют fix-pass.

Reviewer отдельно не оценивал live Windows/collector, фактические сбор/остановку, heap/DAC/root analysis и H4. Эти gates остаются pending; freeze проверяется отдельно ниже. Выбор оставить ограниченный PowerShell entrypoint вместо нового framework сохраняет scope; цена offline doubles — отсутствие нового runtime evidence.

## Артефакты

Все local receipts находятся в TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/:

- run-v5.ps1, v5-replay-tests.ps1;
- v5-real-wire-red.json, v5-original-wire-green.json;
- v5-contract-capture/, v5-input-provenance.json;
- v5-replay-red/result.json, v5-replay-green/result.json;
- v5-review-red/result.json, v5-review-green/result.json;
- validation-v5/v5-api-contract.trx, validation-v5/v5-final-standalone.trx.

Исторические v2/v3/v4 ошибки и один actual baseline dump сохранены. Source task2.1 дополнен подготовкой v5; task2.2 остаётся unchecked, общий прогресс3/8. M1 failed16/17, H4-M2 unknown и отсутствие M2 load acceptance не изменились. Нет stage/commit/push/merge/deploy.

Команда проверки из физического repo root:

~~~powershell
pwsh -NoProfile -File ./TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/run-v5.ps1
~~~

Будущий эксперимент требует отдельного явного разрешения на тот же script с -Execute. Сейчас он не выполнялся.


## Frozen packet v5

Default invocation завершилась exit0: readyForAuthorization=true, executed=false. Проверены1919source files,1125build/tool/packet files и347runtime/launcher files, включая PowerShell JSON/runtime assemblies. В packet root нет attempt-v5.json и run-v5/. Это integrity/profile proof, а не Windows/runtime acceptance.

| Артефакт | SHA-256 |
|---|---|
| run-v5.ps1 | 575A9F892DEB9252E2A254FF44EC2ACDABF689C39B99277A4F7F9297F5F77669 |
| source-manifest-v5.json | 9B8A3694571038B36752564806D85F53DE2C8D75C719305D533AC7EBABFCB623 |
| integrity-v5.json | 3DBEABCE0FA5FCF14D73B10E890FB63451C2EC634527D1135A9688366B39E546 |
| v5-replay-tests.ps1 | 14CC9FB557BEE3A6C7DCF79B176261D16A9FC9F702DFD3714A931F0F0D040574 |
| profile.json | 523530B53BBC50935242E5E6E1DE6665F04FF26FE34B012E4E248E2F9F8C0746 |

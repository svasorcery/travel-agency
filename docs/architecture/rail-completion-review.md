# Rail M1 — завершение кода, спецификации и CI

Дата: 2026-10-20. Rail M1 реализован отдельным исполнителем `gpt-6.1-sol`, лично проверен основным агентом и слит в `dev` через [PR #57](https://github.com/svasorcery/travel-agency/pull/57). Приняты source/local, actual Host/OpenAPI, вымышленные измерения и обычный PR/post-merge CI. Реальный Yandex.Rasp остаётся выключенным; права, доступ и target rollout требуют отдельных этапов.

## OSS/демо-сценарий (2026-10-21)

Проект может оставаться публичным кодом или использоваться для редкого демонстрационного стенда. Для завершённости этого сценария не требуются регистрация у поставщика, настоящий ключ, живые API-вызовы или публичное развёртывание. При обычных настройках реальный Rail выключен; вымышленное демо выбирается явно. Готовность кода, локальные проверки и CI сохраняют свою самостоятельную приёмку.

Если владелец конкретного экземпляра решит подключить Яндекс, он использует свой аккаунт и ключ и отвечает за соблюдение условий, квот, правил работы с данными и атрибуции. Это относится и к авторскому стенду, и к чужому fork. MIT-лицензия кода не передаёт доступ к аккаунтам автора или права на данные поставщика. Практические границы: [OSS/demo и реальный доступ](../../modules/rail/README.md#ossdemo-and-optional-real-access).

## Текущий контракт

[Rail schedule search](../../openspec/specs/rail-schedule-search/spec.md) содержит 23 требования и 90 сценариев. Все 25 задач завершены. Proposal, design, исходный delta, личное ревью и результаты сохранены в [архиве change](../../openspec/changes/archive/2026-10-20-rail-m1-yandex-search/archive-verification.md). Исторический delta повторно применять к основному контракту не нужно.

Поиск ограничен конкретными станциями РФ, выбранной датой и прямыми поездами/электричками через один Yandex.Rasp. Собственный Angular-интерфейс различает complete-empty, limited, skipped и failed, показывает плановые времена с предоставленными offsets и независимые билетные факты. Точная цена сохраняется строкой; electronic-ticket indicator и список типов билетов не доказывают наличие мест. Бронирование, пересадки, realtime и дополнительные источники в M1 не входят.

Каталог и результаты хранятся в ограниченной RAM с абсолютным TTL. Rail не добавляет постоянную БД, миграции или брокер. Внешний протокол, DTO и mapping принадлежат Infrastructure; композиция Host проходит через Api.Composition. Подключение Trips M1 выполнено отдельно в PR #58.

## Merge и обычные CI-проверки

| Этап | Точная привязка | Результат |
|---|---|---|
| Source после исправлений | `7d8abf82697e653d28bc7af67854380a6f7bc6da` | [CI 39445766355](https://github.com/svasorcery/travel-agency/actions/runs/39445766355), attempt 1: 17/17 обычных jobs, включая PR E2E |
| Окончательный head PR #57 | `ea40575d14df8ccdfbab29863bb237a8c3d5c8fb` | [CI 39448274735](https://github.com/svasorcery/travel-agency/actions/runs/39448274735), attempt 1: 17/17 обычных jobs, E2E 2/2 |
| Merge в dev | `22a06448edf1edc6cd139894aae3fcdc5e8edf49` | Родители: прежний dev `799e31a6b31ff256ba017861cd911e547e4e9896` и принятый head `ea40575d14df8ccdfbab29863bb237a8c3d5c8fb`; merge tree совпадает с head PR |
| Post-merge push | тот же `22a06448edf1edc6cd139894aae3fcdc5e8edf49` | [CI 39453275768](https://github.com/svasorcery/travel-agency/actions/runs/39453275768), attempt 1: 16/16 обычных jobs |

На push PR-only E2E и protected paid AI eval пропущены по существующим условиям workflow. В PR пропущен только paid AI eval. Обычные gates не пропущены; модельные eval и deploy не запускались.

Post-merge логи подтверждают Rail unit 35/35, adapter/HTTP 75/75, Host HTTP 486/486, Host integration 118/118, architecture 182/182 и fictional browser 86/86. Эти counts относятся к merge Rail, до независимых изменений Trips. CI текущего документационного diff не выдаётся за CI реализации.

## Локальная приёмка и измерения

Actual Host/OpenAPI проверен через настоящий Program.cs и одноразовый PostgreSQL: 3/3, без настоящего supplier transport. Обнаруженная привязка service как POST body исправлена явным FromServices; строгий JSON reader, no-store и точные цены проверены. Полный Host snapshot добавляет только Rail paths/schemas/tag. Подробности: [runtime verification](../../openspec/changes/archive/2026-10-20-rail-m1-yandex-search/runtime-verification.md).

Зафиксированный профиль `rail-m1-host-memory-v1`, SHA256 `802e60aee97b248c242382672c5e4e5e7a0c2137006f025fae5223c3a30473ed`, прошёл полным отдельно разрешённым attempt 02:

- 3 цикла при 1 и 2 одновременных поисках: 6000/6000 полезных uncached HTTP successes, 0 ошибок.
- Потоковый каталог 40 MiB / 100000 станций; давление на 128 cache entries в каждом цикле.
- Baseline: медиана пяти samples за 30 секунд — 468762624 bytes; порог восстановления — 539077017 bytes.
- После 120 секунд recovery во всех шести циклах: 362598400–449933312 bytes; workingSetRecovery 6/6. Forced GC не использовался.
- Наихудший p95 — 2.1073 ms при пороге 1000 ms. Это latency вымышленного terminal transport, не Яндекса.
- Saturation отдельно: 12 cancellations и 12 capacity refusals не включены в полезный denominator. После recovery: 1 bundle, 0 search entries/children/loaders, 2 свободных search permits.

Измеренный source digest — `7c5c7bca06287ce97d7076a066a5ee059133ef1781107b1ecb85a811cb558ec7`, с 150 file hashes и 10 executed DLL hashes. Source/DLL не менялись во время полного run. Последующие source/документационные digest не подставляются вместо него.

## Сохранённые исходные результаты

Первый measured attempt прерван извне после 4/6 успешных циклов: terminal verdict отсутствует, неизвестные попытки пятого цикла не выдуманы. Он остаётся incomplete; attempt 02 получил отдельное разрешение. Первый CI [39440021934](https://github.com/svasorcery/travel-agency/actions/runs/39440021934) упал на шести ESLint errors. Второй [39442338914](https://github.com/svasorcery/travel-agency/actions/runs/39442338914) выявил 17 mobile overflow failures из 86 browser cases. Исходные отказы сохранены; исправления не отключали тесты и не расширяли пороги.

После уборки 348 файлов исходных отчётов, TRX, screenshots и interrupted/RED receipts сохранены в локальном невёрсионируемом архиве `D:\_Projects\_github\travel-agency\TestResults\_archives\rail-20261020\rail-evidence.zip`, SHA256 `4bde938215bb4148dc20878a8994cbc15a063fbf350c06e9825841a9726281eb`. Внутри есть manifest с SHA256 каждого файла; прежние пути TestResults в архивных документах относятся к этим материалам.

## Внешние этапы при выборе реального развёртывания

Следующие проверки относятся к конкретному экземпляру с настоящим поставщиком. Они остаются обязательными перед таким использованием; публикация исходного кода и вымышленное демо не требуют закрывать эти этапы. Небольшая аудитория сама по себе не подтверждает права или квоту.

| Этап | Статус |
|---|---|
| Реальный доступ, права на собственный UI и временную обработку данных | Pending; регистрация, переписка и секреты не использовались |
| Текущие account quota/reset, restart/scale-out accounting и безопасный остаток | Pending; исторические 500/day не считаются разрешённым budget |
| Реальные source shape/метаданные и видимость официального HTTPS banner с target CSP/sandbox | Pending; вымышленный local frame не является live proof |
| Target configuration, provisioning и deploy | Pending; Deploy workflow manual-only, развёртывание не запускалось |

Наличие мест, гарантированная покупаемость и реальные цены/движение не устанавливаются расписанием или вымышленными fixtures. Inventory supplier и следующие функциональные этапы требуют отдельного выбора. Hotels M1 L1 остаётся failed 16/17 с workingSetRecovery=false; его waiver не переносился на Rail. Laya остаётся экспериментальной и default-off.

## Rail M2: source/offline и fictional runtime, 2026-10-22

M2 реализован в отдельной ветке `codex/rail-m2-journey-selection-details` от свежего `origin/dev` `a8721effd9056459d217966083816455fbea599d`. [Change](../../openspec/changes/rail-m2-journey-selection-details/proposal.md) остаётся активным; source/offline и [личный review основного агента](../../openspec/changes/rail-m2-journey-selection-details/personal-review.md) приняты,21/21source tasks завершены. Обычный CI пока pending. Полные команды, результаты, ограничения и привязка к source: [verification](../../openspec/changes/rail-m2-journey-selection-details/verification.md).

Локальные фильтры/сортировка работают среди полученных вариантов без HTTP. Одна панель маршрута по явному клику сначала перепроверяет exact key+instants+offsets свежим поиском одного mode, затем читает thread с датой из проверенного source locator. Нет угадывания дня по посадке/start_date, сопоставления по номеру или обновления цен. Новый `Rail:JourneyDetailsEnabled` default-off и отзывается вместе с profile epoch; shared admission2, whole-child deadlines и условная eviction старого snapshot сохраняют M1 bounds.

Offline unit/terminal adapter/lean HTTP/Wolverine OpenAPI, TS/Angular и fictional browser проверены отдельно. Host snapshot дополнен только новым Rail path и девятью схемами; все прежние paths/schemas структурно сохранены. После отдельного разрешения actual Program.cs/одноразовая PostgreSQL/OpenAPI прошли 3/3 теста, включая явный detail request и zero real dispatch. Команды и identity — в [runtime receipt](../../openspec/changes/rail-m2-journey-selection-details/runtime-verification.md).

Отдельный `measured-m2-profile.json` и SOURCE существующего harness описывают6cycles,500uncached all-mode search+500detail/цикл,200stops,1500search GET+500thread GET,1000/1000 полезных outcomes и0failures. Predicate проверяет exact dispatched request/profile, stage/verification/TTL и stops;200failed/limited/skipped и неверный binding не successes. Отдельные search/detail p95 и прежние40MiB/100000stations/128cache/baseline/recovery thresholds сохранены. Один отдельно разрешённый M2 run прошёл 6/6 циклов: 6000/6000 полезных операций, 0 ошибок и 12000 fictional source reads. Working-set recovery прошёл 6/6: максимум 396849152 bytes при пределе 541309747 bytes. Максимальный search/detail p95 — 1.2071/10.4692 ms; это latency вымышленного transport. Исполненный source digest `41ef618fd29d2d5dce3272bf71f1cd9d8c67ad4adba170223bd78f0bb4d4a1bb` и 10 DLL hashes не менялись. Полный denominator и cleanup сохранены; M1 receipt не подменён результатом M2.

Не выполнялись supplier/paid/model calls, получение credentials, AppHost, stage/commit/push/PR/sync/archive/deploy. Actual Host/одноразовая БД и фиксированная нагрузка выполнены в разрешённом fictional контуре; временные ресурсы удалены. Сохранены 30 build warnings (29 AD0001 и standalone Verify discovery); анализаторы не отключались. Trips source/specs не менялись. Реальные права, форма ответа, квоты, banner и target rollout остаются отдельным выбором владельца экземпляра.

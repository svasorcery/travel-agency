## Why

Архивировано2026-10-19:25/25 задач, реализация слита в dev через PR55, PR/post-merge CI прошли, требования перенесены в main. Итог и границы — [archive-verification.md](archive-verification.md).

Hotels M1 уже умеет искать и бронировать через default-off Test-only Duffel Stays. Пользователь выбрал **Duffel + Hotelbeds** для следующего портфолио-среза: одна новая внешняя интеграция, поиск по двум источникам и полный путь покупки у выбранного поставщика. Собственный код реализован и проверен автономно на вымышленных данных; партнёрского доступа нет. Альтернативный fake M1 не считается вторым поставщиком.

Объединение выдачи требует сохранить происхождение данных, отличать отель от номера/тарифа/предложения и показывать неполноту поиска. Иначе похожие названия, разные условия и разные виды цены могут превратиться в ложное «самое дешёвое предложение» или повторную покупку.

## What Changes

- Добавить Hotelbeds Booking API adapter: Availability, необходимые CheckRate/conditions, один Booking и ограниченное чтение по известному booking reference. Внешние DTO, auth, mTLS и mapping остаются в Hotels Infrastructure.
- Выполнять ограниченный параллельный поиск Duffel/Hotelbeds; сохранять skipped, complete-empty, limited, failed и причины в snapshot, API и UI. Не подменять supplier failure вымышленными данными.
- Объединять карточки только по проверенному соответствию физических отелей. Номера, тарифы и предложения разных поставщиков остаются отдельными.
- Сравнивать точные суммы за всё проживание, налоги, доплаты, питание и отмену с явными неизвестными. Разделить сумму поставщику и розничную цену; не придумывать наценку или FX. Ввести детерминированный порядок с понятными причинами и областью сравнения.
- Пройти обычный owner/quote/consent/attempt/private-result путь для обоих источников. Quote сохраняет профиль и привязку источника; новый purchase admission повторно проверяет доступ/account/profile/validity. Смена источника требует нового выбора и согласия в том же purchase flow; неизвестный исход исключает повтор и автоматическую покупку у другого поставщика.
- Сохранить M1: один номер/1–2 взрослых, privacy, durable single dispatch, read-only recovery, default-off/Test-only и общий Duffel webhook с раздельными обработчиками.

Первый вертикальный срез: небольшой curated каталог, одна проверенная вымышленная пара отелей и реальные source adapters за fictional HTTP transport, от поиска до приватного результата покупки Hotelbeds; затем негативные/конкурентные случаи и расширение bounded набора. Это полный booking, а не search-only M2. Реальная sandbox acceptance является отдельным будущим этапом.

## Capabilities

### New Capabilities

- `hotel-property-matching`: идентичность, происхождение, проверенные соответствия, сомнения и безопасная группировка отелей.
- `hotel-offer-comparison-ranking`: сравнимость денег/условий, deterministic ranking и объяснение порядка.

### Modified Capabilities

- `hotel-stay-search`: два источника, bounded fan-out, truthful completeness, immutable snapshot и точный source-specific selection.
- `hotel-reservation-booking`: Hotelbeds purchase через существующую authority, source-bound consent и запрет cross-provider повторной покупки при неопределённости.

**Порядок корпуса:** принятый M1 baseline был ранее полностью перенесён в main; M1 архивирован без повторного sync. Затем все четыре M2 deltas применены к основным спецификациям и M2 архивирован. Текущие контракты: [hotel-stay-search](../../../specs/hotel-stay-search/spec.md), [hotel-reservation-booking](../../../specs/hotel-reservation-booking/spec.md), [hotel-property-matching](../../../specs/hotel-property-matching/spec.md), [hotel-offer-comparison-ranking](../../../specs/hotel-offer-comparison-ranking/spec.md); [duffel-webhook-routing](../../../specs/duffel-webhook-routing/spec.md) не изменён. История M1 сохранена в [соседнем архиве](../2026-10-19-hotels-m1-duffel-booking/archive-verification.md).

## Impact

Область source изменений: Hotels Core/Application/Infrastructure/Api, Angular Hotels flow, typed TS client/OpenAPI и соответствующие tests. Wiring — через `Api.Composition`; Host сохраняет process-wide ownership. Shared abstractions добавляются лишь при реальном общем потребителе. Не нужны global catalog, matching service, AI ranking, новая очередь, booking engine, customer PSP, карты или post-booking actions.

Supplier selection handles хранятся server-only внутри того же bounded cache entry и входят в его byte accounting; публичный API их не раскрывает. Подготовленная quote получает минимальный durable binding вне accepted terms, поэтому admission не зависит от живого search cache и не переписывает старые M1 facts.

Проверенная база: `HEAD = dev = origin/dev = 75dfba532c0f321286630e3d04fbea70347903a7`; checkpoint `fd94b9c91cd4345f1b5a684a3c8b73d2bc84e6b9` — предок. M1 завершён с принятым ограничением: исходный L1 **16/17**, `workingSetRecovery=false`, `passed=false`, exit1. Он остаётся failed; исключение не переносится на M2. Dependency pins и braces backport не меняются.

Публичные условия **не подтверждают право настоящей агрегации**: Duffel metasearch scope и Hotelbeds test-mapping запрет остаются отдельными внешними gates. Свой код и fictional fixtures не являются доказательством supplier permission. Подробные источники — [research.md](research.md), конкретный проект — [design.md](design.md).

Владелец утвердил итоговую спецификацию и sync M1 на2026-10-19, затем отдельно поручил реализацию, локальную relational/Host проверку, публикацию и merge. Исполнитель gpt-6.1-sol и основной агент завершили25/25 задач. PR55 содержит source a7f67b0070b194e02b85c68d5d4a6370da05f9b6, merge5a0fb7a36de6101170c4b676415e616890b4bfa6; PR CI17/17 и post-merge CI16/16 прошли. По последующему поручению архивированы M1/M2 и актуализированы main specs. [Verification](verification.md) и [archive verification](archive-verification.md) сохраняют точные receipts и исторические RED. Реальные supplier/paid API, нагрузочный M2 профиль, target provisioning и deploy остаются отдельными непройденными этапами.

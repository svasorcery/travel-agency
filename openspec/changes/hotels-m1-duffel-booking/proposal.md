# Proposal

## Why

Hotels должен показать законченный, понятный пользователю путь покупки проживания и измеримую устойчивость демо под нагрузкой. До реальной агрегации нужно проверить, что общий контракт сохраняет смысл ответов разных поставщиков, а безопасная покупка не зависит от частностей Duffel.

## What Changes

- Сохранить четыре подтверждённых решения: один внешний Duffel Stays в M1, полный Angular flow, один номер на 1–2 взрослых и общий webhook-вход Flights/Hotels.
- Добавить компактные Hotels-контракты поиска, подготовки принимаемых условий, покупки и опционального наблюдения. Offline support check и исполнение используют одно правило; unsupported/skipped, successful-empty, failure и limited/partial не теряются в snapshot, API или UI.
- Проверить общий сценарий небольшим альтернативным fake: без supplier quote ID и webhook, с синхронной бронью и явно неподдержанным запросом. Он служит contract test, не второй внешней интеграцией или платформой симуляции.
- Дать пользователю поиск/выбор, актуальную локальную quote, review точной цены/условий, защищённые данные гостей, одну долговечную попытку покупки и приватный результат после reload. Детали Duffel quote/balance/HTTP остаются в Infrastructure.
- Согласовать таблицу состояний, включая подтверждённую внешнюю отмену и конфликтующие факты. Десятиминутная эскалация не запрещает ограниченное получение деталей впервые установленной поздней booking identity; дубликаты и restart не создают новый бюджет.
- Сохранить `/webhooks/duffel`, два изолированных module receivers и собственные atomic inbox/outbox. Приём новых v2 `data.object` отделить от чтения сохранённых исторических Flights entries с корневым `object`; разрыв формата не переносится в новый ingress.
- Хранить принятую покупку, единственную attempt и текущие факты в одной модели reservation с защищённым private content. Самостоятельные receipt/result/journal/tombstone подсистемы не вводятся; минимальные identities и запрет повторного create переживают очистку private content.
- Добавить безопасную диагностику и короткий runbook; заранее заданный короткий нагрузочный профиль проверяет полезный throughput, p95, отказоустойчивость, memory/concurrency bounds и отсутствие starvation private reads/наблюдений.
- Строить M1 сквозными срезами с UI на каждом шаге. M2 добавляет второго настоящего допустимого поставщика, bounded fan-out, контролируемое сопоставление/дедуп и deterministic ranking. Третий источник добавляется при новом существенном случае.

Source implementation поручена в текущем checkout; исходные четыре решения не открываются повторно. Реализация и fictional acceptance проходят проверку по этому единственному corpus. Продуктовые границы и начальные настройки различаются в design; итоговая личная проверка основного агента остаётся отдельной задачей5.4.

## Capabilities

### New Capabilities

- `hotel-stay-search`: поддержка запроса без сети, честный результат поиска/тарифов, его сохранение в snapshot и полноценный выбор в UI.
- `hotel-reservation-booking`: локальная quote и согласие, private single-attempt покупка, фактический результат, bounded late observations и диагностика.
- `duffel-webhook-routing`: один вход, аутентифицированный owning module, актуальный v2 формат, historical Flights reader и изолированная durable acceptance.

### Modified Capabilities

Нет изменений main OpenSpec capabilities. Поддержанные Flights business transitions и сохранённые receipts остаются; ограниченная корректировка wire reader описана в новой routing capability. Не добавляются все Duffel events или Hotels order links в Support.

## Impact

Source реализация затрагивает существующие Hotels projects/tests, Angular routes, типизированный TS client, Host composition/OpenAPI и source identity fixture. Общий ingress использует узкий raw-delivery контракт в существующем Shared.Abstractions с двумя реальными consumers и ограниченные изменения Flights webhook decoder/codec/tests. Domain/application Hotels не импортируют чужие internals или supplier DTO. Hotels.Api.Composition остаётся единственной module surface Host; отдельные сервисы, общий booking engine, ES Hotels и новые dependencies не нужны.

Точные contracts, состояния, варианты, source evidence, настройки, load profile и requirement → decision → task → acceptance mapping находятся в [design.md](design.md); порядок работ — [tasks.md](tasks.md). Это единственный planning corpus.

Для исходной source-фазы были разрешены source implementation отдельным `gpt-6.1-sol` с последующей личной проверкой основного агента, подготовка additive EF source migration/offline SQL и проверки на собственных disposable PostgreSQL/Host/fake-provider fixtures с вымышленными данными и test keys. Это включало заранее объявленный360s L1 после проверки профиля и runner основным агентом. На этом этапе существующие/целевые БД, target provisioning, сохранённые credentials, реальные provider/payment/model calls, dependency updates, deployment, stage/commit/push/PR/publication и archive не были разрешены. Последующее разрешение на PR указано ниже; оно не расширяет продуктовую спецификацию.

## Завершение M1 с принятым ограничением — 2026-10-17

Пользователь явно принял ограничение проверки памяти и поручил завершить M1 в текущем OpenSpec без новых прогонов. Source/fictional M1 завершён с этим ограничением; задачи закрываются22/22. Измеренный L1 сохраняет результат **16/17**, `workingSetRecovery=false`, `passed=false`, exit1. Эта проверка не пройдена и не становится успешной при закрытии M1; численные пороги, профиль, исходные receipts и диагностические данные не меняются.

Решение, обоснование и границы принятия записаны в [verification.md](verification.md#принятое-пользователем-ограничение-и-завершение-m1--2026-10-17), согласованы с D6 design и capability spec. Оно не доказывает отсутствие утечки, hard2GiBreference capacity, supplier sandbox или готовность целевого окружения. Дальнейший rollout и публикация этим решением не разрешены.

## Отдельное разрешение на PR — 2026-10-17

После принятия M1 пользователь отдельно поручил создать PR Hotels M1 в `dev` из новой ветки, указать принятое ограничение памяти/результат16/17 и дождаться CI. Для этого разрешены отдельная ветка, stage/commit/push и создание PR. Ветка `codex/hotels-m1-duffel-booking` создана от свежего `origin/dev` на894c40f895b9e864263686f814ac0993bdbd3e8b; ancestry проверена. Merge, деплой и повторная нагрузка явно исключены. Нагрузочный `workingSetRecovery=false`/`passed=false`/exit1 не переоценивается по результату обычного PR CI.

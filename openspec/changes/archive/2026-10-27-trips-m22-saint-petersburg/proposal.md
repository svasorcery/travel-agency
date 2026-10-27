# Proposal

## Why

Trips уже умеет сохранять поездки в Санкт-Петербург, но предлагает прогулочный маршрут только для Москвы. Второй ограниченный городской пакет проверит повторное использование существующего генератора и стоимость обслуживания данных после принятого Moscow demo, не расширяя продукт до универсального планировщика.

## What Changes

- Добавить `saint-petersburg-outdoor/1`: наружный осмотр и прогулки в компактном центре на левом берегу Невы, 1–3 дня, 1–9 взрослых. Предварительный набор — 20 отдельных POI; точное покрытие и 60–100 направленных переходов подлежат проверке по реальному OSM, а не объявляются готовыми.
- Переиспользовать один offline builder, строгий loader, Core generator и независимый validator. Выбирать один явно закреплённый пакет по сохранённому destination; отказ одного города не отключает другой.
- Расширить существующие HTTP routes явным opt-in `X-Trips-Itinerary-Version: 2`. Старый режим без заголовка сохраняет Moscow-only v1; v2 availability требует один `destinationId`, candidate POST по-прежнему использует пустой объект и город из owner plan. Версии декодируются раздельно и строго.
- Сохранить каталог версии `1`, Moscow v1 и все шесть его файлов/ссылок/исходный expiry. Защитить от смешения destination, dataset, source, POI/edges и derived-data offer.
- Расширить обслуживание пакетов первым выпуском для нового города, без фиктивного сравнения с московской версией. Подготовка не активирует пакет.
- Добавить самостоятельные SPb D_demo, механический Q_demo, фактологический аудит и настоящий Host/Keycloak/PostgreSQL E2E с перезапуском и сохранностью обеих географий. Human A/B и rollout остаются отдельными.
- Не менять существующее локальное demo и Q01–Q12, не выпускать Moscow v2, не включать реальных пользователей; исключить платные API, runtime-провайдеров, M3/free-text, AI framework, новые migration/schema/keys.

## Capabilities

### New Capabilities

Нет: расширяются существующие контракты, без отдельной дублирующей capability на город.

### Modified Capabilities

- `trip-itinerary-candidates`: два явно поддержанных города, destination selection, совместимый opt-in протокол и независимая availability.
- `trip-data-package-maintenance`: city-scoped inspection/bootstrap/reproduction при сохранении московского baseline и границы candidate/release.
- `trip-plan-workspace`: явная гарантия совместимости catalogue-v1 и защищённых исторических планов обоих городов.
- `trip-rail-intentions`: убрать устаревающее нормативное Moscow-only ограничение walking; правила Rail не расширять.
- `trips-local-verification`: два полноценных real-service сценария с корректно раздельным временем и проверкой обеих сохранённых поездок.

## Impact

Изменения после отдельного разрешения затронут закрытую валидацию профилей Trips Core, Application/Infrastructure/Api composition и HTTP guards, общий TS api-client и Angular state/UI, offline helper/maintenance/local-stack инструменты и существующие Trips/Host/browser тесты. Core алгоритм и его лимиты сохраняются. Новая внешняя runtime-зависимость, DB migration и увеличение CityCatalogue.Version не нужны.

Это только план. Основание: свежий `origin/dev=11ba5a1572e53cd901ae4ec56b77b482ec95f699`, полученный 26.10.2026; ветка `codex/trips-m22-saint-petersburg`. Решения и ограничения — [design](design.md); исследование источников и предварительный inventory — [source-dossier](source-dossier.md). Реализация, commit/push/PR/merge и target actions сейчас не разрешены.

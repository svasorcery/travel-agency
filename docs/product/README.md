# Продуктовая история Travel

[Читать документ](travel.ru.md) · [открыть HTML локально](travel.ru.html) · [общая схема SVG](assets/travel-map.svg)

`travel.ru.md` – исходный текст. `travel.ru.html` – версия для чтения с оглавлением, адаптивной вёрсткой и встроенными изображениями; её можно передать одним файлом и открыть без сервера и интернета. На GitHub HTML показывается как исходный код: для чтения нужно скачать файл и открыть в браузере. В `assets/` находятся четыре иллюстрации в SVG; PNG для статей и презентаций можно получить локально командой ниже.

## Обновление

Из корня репозитория:

```text
node tools/docs/build-product-story.mjs
```

Команда пересобирает SVG, HTML и `publication.json` с хэшами текста и схем для загрузки в блог. Текст редактируется в Markdown, оформление и схемы – в скрипте сборки. Для обновления PNG нужен установленный в проекте Playwright с Chromium:

```text
node tools/docs/build-product-story.mjs --png
```

Встроенный обработчик Markdown поддерживает только используемые в документе заголовки, абзацы, цитату, выделение, ссылки и изображения. Это сборщик одного документа.

## Пакет для блога

Интеграция страницы `/projects/travel/` использует опубликованную ветку `dev`. После правок запустите сборщик и включите обновлённые Markdown, SVG и `publication.json` в одну публикацию. Хэши считаются по UTF-8 с переводами строк LF; манифест вручную не редактируется.

Блог при сборке фиксирует один коммит Travel и загружает из него весь комплект. После первоначального включения страницы её обновляет существующая ежечасная сборка блога; читателям текст и схемы отдаются с домена блога. Описание и иллюстрации продолжают редактироваться только здесь.

## На чём основан текст

Материалы сверены с исходниками 16 ноября 2026 года. Таблица помогает найти правила при последующем редактировании; она не входит в читательскую HTML-версию.

| Тема | Основные источники |
| --- | --- |
| Состав платформы | [AppHost](../../apps/Travel.AppHost/Program.cs), [Host](../../apps/Travel.Host/Program.cs), [навигация](../../apps/web/src/app/app.html) |
| Поиск и оформление перелётов | [README](../../README.md), [правила сортировки](../../modules/flights/Travel.Modules.Flights.Application/Search/OfferRanker.cs), [багаж и места](../../openspec/specs/flights-booking-ancillaries/spec.md) |
| Отмена, деньги, обновление заказа | [Отмена](../../openspec/specs/flights-whole-order-cancellation/spec.md), [возврат клиенту](../../openspec/specs/flights-customer-refund-after-cancellation/spec.md), [обновления](../../openspec/specs/flights-order-live-updates/spec.md) |
| Отели | [Поиск](../../openspec/specs/hotel-stay-search/spec.md), [сопоставление](../../openspec/specs/hotel-property-matching/spec.md), [сравнение](../../openspec/specs/hotel-offer-comparison-ranking/spec.md) |
| Поезда | [Расписание](../../openspec/specs/rail-schedule-search/spec.md), [маршрут](../../openspec/specs/rail-journey-details/spec.md) |
| Планирование | [Личный план](../../openspec/specs/trip-plan-workspace/spec.md), [прогулки](../../openspec/specs/trip-itinerary-candidates/spec.md), [разбор пожеланий](../../openspec/specs/trip-brief-extraction/spec.md) |
| Поддержка и AI | [Обращения](../../openspec/specs/support-case-workflow/spec.md), [классификация](../../openspec/specs/support-classification-suggestions/spec.md), [объяснение тарифа](../../openspec/specs/flights-fare-rules-summary/spec.md) |
| Proofline | [Репозиторий](https://github.com/svasorcery/proofline), `README.md` и `docs/operator-ui.md` в соседнем локальном проекте |

При обновлении сохраняйте один общий блок об условиях демонстрации. В основном тексте объясняйте поведение и пользу конкретного сценария. Отличия между планом и заказом, деньгами поставщика и возвратом клиенту, подсказкой и решением человека – часть предмета рассказа. Схема Proofline показывает тематическую связь самостоятельных проектов.

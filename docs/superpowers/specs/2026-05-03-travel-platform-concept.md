# Travel Platform — Концепция и северная звезда

**Дата:** 2026-05-03
**Статус:** финализировано (три итерации ревью с web-верификацией), готово к декомпозиции на спеки подпроектов
**Скоуп этого документа:** концептуальная "северная звезда" для всех будущих подпроектов — фиксирует видение, декомпозицию, архитектурный стиль, выбор стека, AI-стратегию, выбор интеграций, скоуп флагмана и стратегию деплоя.

---

## 1. Видение и аудитория

Публичный showcase-репозиторий, демонстрирующий зрелые навыки в архитектуре, DDD, .NET, Angular и AI-assisted разработке. Сфера — туризм. Целевая аудитория — широкая русскоязычная аудитория через сопровождение блогом.

Репозиторий — "живой", сопровождается серией статей по каждому подпроекту. Цели:
- база для презентации навыков (резюме, собеседования, доклады);
- источник материала для блог-постов;
- "playground" для дальнейших экспериментов с AI-практиками.

### 1.1. Двухслойная модель продукта

- **Слой 1 — "Инфра" (booking).** Поиск и бронирование транспорта и проживания: авиа, жд, отели, трансферы.
- **Слой 2 — "Ценность" (trip planning).** Планирование путешествий поверх Слоя 1: композитные сценарии, AI-генерация маршрутов, объяснимое ранжирование.

### 1.2. Стратегия реализации

**C: Флагман + спутники.** Один домен реализован production-grade end-to-end ("флагман"), остальные — архитектурный каркас с явно помеченным уровнем зрелости в README/ADR. Каждый подпроект сопровождается серией статей.

---

## 2. Декомпозиция на подпроекты

| # | Подпроект | Что показывает | Целевая зрелость |
|---|---|---|---|
| 0 | **Foundation** | Монорепо, AI-harness, контракты, observability, CI, ADR-структура | Production-ready |
| 1 | **Flights — флагман** | DDD, single + mixed-aggregation booking, NDC-сложность, sagas, money flows, webhooks | Production-grade end-to-end (Tier 2) |
| 2 | **Hotels — мульти-GDS** | Distributed search, fan-out, ranking, dedup, ACL × 3 поставщика | Полный мульти-supplier search + один happy-path booking |
| 3 | **Rail — read-only** | CQRS read-side, non-bookable интеграция, мульти-source | Полный поиск, без букинга |
| 4 | **Trip Planning (Layer 2)** | Композит над 1-3 + side-cars, AI-генерация маршрутов | Отдельный нарратив, отдельный брейнштормить-цикл |
| 5 | **AI features as a service** | Multi-agent orchestration, evals, prompt versioning | Отдельный нарратив |

**Порядок:** 0 → 1 → 2 → 3 → 4 → 5. Каждый подпроект — отдельный цикл brainstorm → spec → plan → реализация → серия статей.

**Текущая сессия покрывает концептуально подпроекты 0 + 1.** Спеки 2-5 пишутся в свой срок.

---

## 3. Выбор интеграций

### 3.1. Доступность по доменам в OSS-формате (май 2026)

| Домен | Bookable провайдеры | Read-only / data-only |
|---|---|---|
| **Flights** | Duffel (sandbox unlimited) | Travelpayouts/Aviasales (RU контент, deeplink) |
| **Hotels** | LiteAPI, Duffel Stays, Amadeus Hotels (test env) | — |
| **Rail** | — | Yandex.Rasp (RU, верифицировано — живо), DB open data + `db-vendo-client` (EU), GTFS-фиды |
| **Car rental** | Duffel Cars (релиз апрель 2026; планируется в подпроекте 4 как часть trip planning) | — |
| **Side-cars** | — | Open-Meteo, Frankfurter, OSM/Overpass, REST Countries (все free, no auth); OpenRouteService (free key, 2k req/день, 40/мин) |

### 3.2. Особый паттерн: mixed bookable + deeplink aggregation

Travelpayouts добавляет к Flights флагману категорию `DeeplinkOffer` (есть цена, нет букинга — переход к партнёру). Это реальный паттерн настоящих OTA, который без этого решения было бы не показать. Создаёт отдельный архитектурный нарратив "поиск, в котором не все результаты можно купить".

### 3.3. RU-фокус

- **Flights:** Travelpayouts даёт реальный российский контент (домашние маршруты + RU-перевозчики).
- **Rail:** Yandex.Rasp (РЖД дальнего следования + электрички + автобусы + авиарасписания). API живо в 2026, ключи выдаются после email-подтверждения. Лимит ~500 req/день. **TOS-ограничения (`yandex.ru/legal/rasp_api/`):** обязательная атрибуция в UI ("Данные предоставлены сервисом Яндекс.Расписания" + ссылка на rasp.yandex.ru); запрет на не-временное кэширование raw-ответов. **Архитектурное решение:** raw-ответы в кэше живут ≤1 часа in-memory (Redis с TTL), персистим только нормализованные доменные сущности с собственным lifecycle. **Риск-флаг для DB open data + db-vendo-client:** community-обёртка; legacy DB HAFAS API был отключён в январе 2026, vendo-client — drop-in замена, активно поддерживается (v6.10.x на апрель 2026), но имеет более жёсткие rate-limits чем legacy HAFAS. Пинимся на версию, мониторим issues репо, абстрагируем под `IRailScheduleProvider` для лёгкой замены.
- **Hotels:** RU-сегмент закрыт после 2022. В README объясняется, что мульти-supplier архитектура позволяет добавить Островок/Бронёвик и т.д. через реализацию `IHotelSupplier`.
- **Trip planning:** локализация — рубли по умолчанию, мск-таймзона, кириллические POI из OSM, дефолтные направления Москва → СПб/Калининград/Сочи.

---

## 4. Архитектурный стиль

**Выбран вариант C: Modular monolith с явной split-readiness + один реально вынесенный сервис.**

### 4.1. Структура процессов

- **`Travel.Host`** — модульный монолит: модули Flights, Hotels, Rail, Trip, Identity, Shared.
- **`Travel.AI`** — реально отдельный процесс. Хостит **четыре продуктовых агента на Microsoft Agent Framework** (MAF 1.0 GA, апрель 2026; first-party Anthropic Claude connector) + **один маленький Travel Advisor на собственном минимальном runtime** как образовательная "deconstruction" демонстрация (показывает, что устроено внутри агентного фреймворка).
- **Aspire** оркестрирует оба процесса в одном `aspire run`.

### 4.1.1. Обоснование MAF-полярности

Изначально планировался обратный расклад (custom primary + MAF reference). После проверки актуальности: MAF вышел в GA 3-7 апреля 2026, консолидировал SK + AutoGen, идёт со встроенным Claude-коннектором. Для проекта, стартующего в мае 2026, ставка на собственный runtime для всех агентов читалась бы как NIH — нет конкретной orchestration-семантики, которую MAF не выражает. Поэтому: **MAF — production-путь, custom — образовательный артефакт**. Минимальный custom runtime на одном агенте (Travel Advisor) — это +1 статья "что устроено внутри agent framework" без production-стоимости.

### 4.2. Почему именно AI вынесено в отдельный сервис

- Разный профиль нагрузки (длинные стримы, дорогие вызовы, latency не имеет жёсткого SLA).
- Разный deploy cadence (часто меняем промпты — не хочется деплоить весь монолит).
- Разные secrets boundary (ключи Anthropic, eval API).
- Естественная асинхронность.
- Так часто и делают в реальности → сигнал "понимаю где распиливать".

**Не выносим Search Aggregator** — слишком тесно завязан на доменные модули, лишний hop вреден latency.

### 4.3. Связь между процессами

- **Wolverine с NATS JetStream** — async команды/события.
- **Wolverine с PostgreSQL transport** — outbox.
- **HTTP** — синхронные запросы где нужно.
- Один **PostgreSQL** (две схемы: `monolith`, `ai`), миграции независимые.
- Один **Redis**.

### 4.4. Ключевые паттерны (обязательные к показу)

- **DDD:** bounded contexts, aggregates, value objects, domain events, ubiquitous language per module.
- **CQRS:** разделённые read/write модели (особенно ярко в hotels с агрегацией).
- **Selective Event Sourcing:** только для booking lifecycle (Marten на PostgreSQL). Не для всего подряд.
- **Outbox pattern** для гарантий доставки.
- **Saga (orchestrated)** для booking.
- **Hexagonal / Ports-and-Adapters** внутри модулей.
- **Anti-corruption layer** для каждой внешней интеграции.
- **ADR** в `docs/adr/`.
- **C4 диаграммы** в репо (Structurizr DSL).
- **Architecture tests** через ArchUnitNET.

### 4.5. Storage strategy

| Модуль | Marten (ES) | EF Core |
|---|---|---|
| **Flights** | `BookingAggregate` (OfferQuoted → Held → Confirmed → Ticketed → Refunded → Cancelled) | Saved travelers, supplier metadata, idempotency keys, outbox, search audit, deeplink offers cache |
| **Hotels** | — | Всё: dedup mapping registry, supplier configs, ranking weights, search snapshots, booking projection |
| **Rail** | — | Cached schedules, station registry, route projections |
| **Trip Planning** | `TripAggregate` (Draft → Planned → Booked → InProgress → Completed → Cancelled) | POI cache, weather snapshots, currency conversions, user preferences |
| **AI** | — | Prompt versions, eval runs, cost ledger, conversation history |
| **Identity / Shared** | — | Users, tokens, audit log, feature flags |

Месседж: "ES где история — это домен, EF где storage — это инфра. Полиглот persistence на одной БД."

Marten и EF ходят в одну PostgreSQL — Marten владеет своей схемой (`mt_*` таблицы), EF владеет своими таблицами в схеме модуля. **Никакого пересечения**. Миграции независимые: Marten применяет свои при старте процесса, EF — через `dotnet ef database update`. Это закрепляется отдельным ADR `0007-marten-and-ef-coexistence.md`.

### 4.6. Cross-cutting concerns

Модули, которые не привязаны к конкретному домену, но нужны платформе:

- **Identity** — Keycloak (self-hosted в docker-compose), OIDC интеграция в `Travel.Host`. Поддержка email/пароль + социальные провайдеры (Google, GitHub) для удобства контрибьюторов. На прод — тот же Keycloak в проде.
- **Notifications** — два канала: **email** (через MailKit + Mailpit локально / SMTP на проде) и **server push** через **SSE** (`/events/{userId}` стрим). Используются для: подтверждение брони, напоминания за 24ч, реакция на webhook от Duffel (изменение статуса заказа), AI-агент завершил долгую задачу.
- **Payments** — Duffel test wallet (sandbox), оборачиваем в `IPaymentGateway` с явной разметкой "TestOnly". Никаких реальных платежей, никакой PCI-обвязки. Архитектура расширяется до реальных платежей через реализацию интерфейса (Stripe/CloudPayments), но это вне скоупа showcase.

---

## 5. Стек

### 5.1. Backend
- **.NET 10**
- **Aspire** — оркестрация, OTel dashboard, генерация production manifest
- **Wolverine** — CQRS / Saga / messaging (MIT, JasperFx open core). Стратегическое позиционирование showcase: после ухода MediatR (июль 2025, Lucky Penny Software) и MassTransit (Q1 2026, Massient) на коммерческие лицензии — Critter Stack (Wolverine + Marten + WolverineFx.Http) остаётся единым полностью MIT-стеком от Jeremy Miller, покрывающим messaging + ES + HTTP в одном ментальном модели
- **Marten** — event sourcing на PostgreSQL (тот же автор, бесшовная интеграция с Wolverine; MIT)
- **EF Core 10** — read models, нон-ES агрегаты
- **PostgreSQL 17** — общая для Marten и EF
- **NATS JetStream** — async messaging между процессами
- **Redis** — кэш
- **WolverineFx.Http** — REPR endpoints через атрибуты + source generation; endpoints возвращают типизированный response (без side-effect модели как у FastEndpoints); native ProblemDetails + tuple cascading messages. Заменяет ранее планировавшийся custom endpoint framework — обновлено в Foundation-цикле после анализа экосистемы (FastEndpoints не подходит из-за async response модели; своя минимальная реализация — NIH в условиях зрелого WolverineFx.Http от того же автора)
- **ErrorOr** (Amichai Mantinband) — Result-pattern с встроенной HTTP-таксономией (`Error.Validation`, `Error.NotFound`, `Error.Conflict`, `Error.Unauthorized`); список ошибок из коробки. Заменяет ранее планировавшийся custom `Result<T>` — обновлено в Foundation-цикле (свой Result<T> в 30 строк = NIH; ErrorOr идиоматичен в .NET 2026 и автор узнаваем в clean architecture сообществе)
- **FluentValidation**
- **xUnit v3 + Verify + Testcontainers + ArchUnitNET + Shouldly**
  - **ArchUnitNET (TNG)** — чистая C#-библиотека (обычный NuGet, никакой Java-зависимости); fluent-API стилистически восходит к Java ArchUnit, но это просто конвенция builder-API. Активные релизы (0.13.x на 2026)
  - **Shouldly** — assertion library (BSD-2-Clause, MIT-style). **НЕ FluentAssertions** — FA 8.0+ ушёл на коммерческую лицензию Xceed в январе 2025; community fork AwesomeAssertions активен, но Shouldly — более чистый выбор без визуального сходства с FA
- **OpenTelemetry** end-to-end

### 5.2. Frontend
- **Angular 21** (стартуем на 21; пишем код по ожидаемым практикам v22 сразу — OnPush-by-default, Signal Forms, селекторлесс-компоненты — миграция на v22 через официальные `ng update` schematics)
- **Signals + Resource API** (`httpResource`/`rxResource`) — primary механизм работы с серверным состоянием
- **NgRx SignalStore** — управление состоянием (cross-feature и где Signals одних не хватает)
- **Standalone components** + zoneless (default в 21)
- **Angular SSR + Hybrid rendering** (SEO для travel-контента, per-route render mode — стабильно с v20)
- **Signal-based forms** (`@angular/forms/signals`, experimental в 21, стабилизация в 22) — для одной формы как demo "знаем направление"; основные формы на классических Reactive Forms до v22 GA
- **Tailwind v4** (4.2+; `@tailwindcss/postcss` — закладываем время на content-source scanning в Nx, `@apply`-нюансы)
- **PrimeNG** в **unstyled mode** + `tailwindcss-primeui` — UI-набор. PrimeTek даёт коммерческое корпоративное сопровождение, comprehensive component coverage критичный для travel-app (DataTable, Calendar, Multi-select, Autocomplete, Tree, Timeline), поддержка signal-based / zoneless Angular, продвинутая интеграция с Tailwind v4
- **Vitest** (default для unit в Angular 21) + **Playwright** с встроенным `toHaveScreenshot()` для E2E + visual regression
- **Storybook 10** для каталога компонентов

### 5.3. Monorepo и tooling
- **NX 22** + **`@nx/dotnet`** (официальный плагин с Nx 22; community `@nx-dotnet/core` deprecated)
- **NX Cloud free tier** — Hobby plan (50k credits/мес, 5 контрибьюторов) — distributed task execution (DTE) и remote caching без оплаты для соло-OSS-проекта
- **OpenAPI** генерируется из .NET → TS клиент через **heyAPI** (`@hey-api/openapi-ts`, plugin-architecture, Fetch output)
- **Pact.NET + Pact-JS** — консьюмер-драйвен контракты только на одну пару `Travel.Host` ↔ `Travel.AI` (минимизируем broker / verification overhead для solo-репо); как complementary signal — OpenAPI-diff в CI (Spectral) на FE↔BE границе
- **Biome** для FE; **CSharpier + `Roslynator.Analyzers` NuGet** для BE (NuGet-форма, не IDE-расширение — анализаторы должны путешествовать с проектом, а не зависеть от установленных IDE-плагинов)
- **Lefthook** + **commitlint** + **commitizen** — git-hooks (pre-commit format, commit-msg по Conventional Commits). Lefthook вместо Husky: одна Go-бинарка, language-agnostic, чище для polyglot monorepo
- **Renovate** (Mend, AGPL — free hosted app для OSS) — автообновления зависимостей через unified конфиг (.NET + npm + Docker + GitHub Actions). Лучше Dependabot для polyglot-репо
- **VS Code devcontainer** с официальным расширением `anthropic.claude-code` — основной "click and code" путь (GitHub Codespaces — см. раздел 9.1)
- **GitHub Actions** + matrix builds + NX Cloud DTE + кэшированные NX targets

### 5.4. AI infra
- **Microsoft.Extensions.AI** — абстракции (`IChatClient`), out-of-band NuGet 10.5.x (поставляется в tandem с .NET 10, не в BCL); версии абстракций и провайдеров пинить явно
- **Anthropic SDK для .NET** — package `Anthropic` на NuGet (официальный, owned by Anthropic; не путать с community `Anthropic.SDK` от tghamm и `tryAGI.Anthropic`). Реализует `IChatClient` напрямую. **Версионная мина:** пакет с этим именем раньше принадлежал tryAGI (3.x), затем передан Anthropic (12.x+) — пинить минимальную мажорную версию явно, любые туториалы 2024-2025 могут ссылаться на старый API
- **Microsoft Agent Framework** (1.0 GA, апрель 2026) — основной runtime для четырёх продуктовых агентов; first-party Claude connector. **Каверзка:** "1.0 GA" — это стабильность core agent APIs; ряд orchestration / workflow фич помечены preview и могут менять API. ADR `0020-maf-as-primary-agent-runtime.md` явно фиксирует, какие части используем (только стабильные) и какие в стороне
- **Собственный минимальный agent runtime** — для Travel Advisor как образовательная демонстрация; остальные образовательные ценности (tool registry, A2A, observability hooks) делаем как платформу _вокруг_ MAF, не _вместо_
- **Свой минимальный eval framework в .NET** — после анонса покупки Promptfoo OpenAI в марте 2026 conflict-of-interest при оценке non-OpenAI моделей делает свой eval честнее для Claude-primary showcase; обоснование в ADR `0019-ai-eval-strategy.md`
- **pgvector** — расширение к нашей PostgreSQL. Native vector search в EF Core 10 — для SQL Server 2025 / Cosmos DB, не для Postgres; используем community-binding `Pgvector.EntityFrameworkCore` (0.x, pre-1.0), относимся как к preview-grade компоненту, пинимся на конкретную версию

---

## 6. AI-стратегия

### 6.1. Слой A — AI-assisted development

| Практика | Уровень |
|---|---|
| `CLAUDE.md` иерархия (root + per-module) | Обязательно |
| Custom agents в `.claude/agents/`: `domain-modeler`, `adr-writer`, `test-author`, `migration-author`, `integration-mapper` | Обязательно |
| Custom slash commands: `/spec`, `/adr`, `/explore-domain`, `/test-this`, `/integration-from-openapi` | Обязательно |
| Hooks в `.claude/settings.json` (формат+типы pre-commit, авто-ADR при архитектурных изменениях, авто-обновление контракта при изменении DTO) | Обязательно |
| Custom MCP server (отдаёт AI текущие bounded contexts, ADR-граф, статус интеграций, словарь домена) | Понижено: дальняя стрейч-цель, делается после подпроектов 0-3 если останется ресурс |
| Specs / plans / ADRs как first-class артефакты в репо | Обязательно |
| Subagent-driven development | Обязательно |
| AI-eval framework в `tests/ai-evals/` — **свой минимальный в .NET** (вместо Promptfoo, после его покупки OpenAI 9 марта 2026) | Обязательно (критично) |
| Cross-tool repro (Codex/Cursor) | Опционально, для отдельных статей |
| `docs/ai-conversations/` — отобранные стенограммы реальных сессий с разбором (минимум один кейс на подпроект) | Обязательно — самый сильный showcase-сигнал в 2026 (никто из конкурентов так не делает) |

### 6.2. Слой B — AI-фичи в продукте

#### Tool calls (одношаговые AI-вызовы, не агенты)

| Подпроект | Фича |
|---|---|
| 1 (Flights) | NL-search, Explainable ranking, Fare-rules summary |
| 2 (Hotels) | Те же три, переиспользованы (доказывают многоразовость инфры) |
| 3 (Rail) | Базовое NL-search |

#### Агенты (многошаговые, с диалогом и контекстом)

Все четыре продуктовых агента — на **Microsoft Agent Framework** (1.0 GA, апрель 2026). Tools — наши Wolverine handlers, обёрнутые как MAF tool functions. Anthropic Claude — через first-party MAF connector.

| # | Агент | Что делает | Подпроект | Сложность | Runtime |
|---|---|---|---|---|---|
| 1 | **Trip Planner Orchestrator** | Принимает интент пользователя, дёргает sub-агентов, собирает целый трип, держит общий контекст | 4 | 🔴 | MAF |
| 2 | **Flight Search Agent** | Ищет рейсы, уточняет ("прямой? с пересадкой ок?"), переранжирует по ответам | 1, 4 | 🟡 | MAF |
| 3 | **Hotel Search Agent** | То же для отелей, плюс понимает "ближе к центру", "тише", "с бассейном для детей" | 2, 4 | 🟡 | MAF |
| 4 | **Itinerary Composer** | День за днём — POI, время, погода, окна работы, перемещения | 4 | 🔴 | MAF |

**Образовательная "deconstruction" реализация на собственном минимальном runtime: Travel Advisor.**
- Что делает: Q&A — "нужна ли виза в Турцию", "что взять в июне в Сочи", "опасно ли в Стамбуле сейчас". Использует поиск по нашим источникам (REST Countries, Open-Meteo, OSM) + общие знания LLM.
- Почему именно он для образовательной демо: самодостаточный (не зависит от наших Duffel/LiteAPI handlers), чистый Q&A use case с минимальным набором tools, поэтому минимальный runtime честно реализуем за разумное время. Поломка собственного варианта при наших же изменениях не аффектит критический путь продукта.
- **Цель образовательной версии — не альтернатива MAF, а "что устроено внутри".** Tool registry, agent loop, structured outputs, streaming — всё реализовано минималистично, ~300-500 строк кода. Параллельная статья: "Что такое agent runtime под капотом MAF".

### 6.3. AI primitives (общая инфра)

- Provider abstraction (Claude primary через MAF connector, легко подменить)
- **Structured outputs** везде, никакого парсинга строк
- **Streaming responses** для UX (через MAF + SSE до фронта)
- **Cost / latency observability** через OpenTelemetry (единый дашборд с обычной телеметрией)
- **Prompt versioning** (`prompts/v1/`, `v2/` с git как историей)
- **Свой минимальный eval framework в .NET** (вместо Promptfoo): пишет/исполняет evals для каждой AI-фичи, живёт в `tests/ai-evals/`
- **Caching ответов** где безопасно (детерминированные запросы по `(prompt_hash, model, params)`)
- **MAF-платформа в `Travel.AI`**: tool registry поверх Wolverine handlers, A2A через JetStream subjects, observability hooks, eval-bridge — всё это _вокруг_ MAF, а не _вместо_

### 6.4. Банк идей агентов на будущее

Не реализуем сейчас, но фиксируем как кандидаты для интерактивов / экспериментов / дополнительного контента:

| Агент | Что делает | Почему может пригодиться | Подпроект |
|---|---|---|---|
| **Booking Concierge** | Ведёт через покупку: "выбрать места?", "багаж?", сохраняет travelers, обрабатывает ancillaries в диалоге | Отлично заходит в нарратив про booking saga; превращает Tier-2 чек-аут в conversational UX | 1, 2, 4 |
| **Disruption Handler** | Перевозчик сдвинул рейс на 4 часа → анализирует, предлагает варианты, может авто-перебронировать. Реагирует на webhook | Самый "вау" агент — реагирует на реальные webhooks от Duffel; готовый сюжет для статьи "event-driven agents" | 1 |
| **Trip Modifier** | "Перенеси на неделю позже" — меняет / возвращает / добавляет с учётом политик | Multi-step с проверкой политик возвратов, естественно работает поверх booking saga | 1, 4 |
| **Price Watch** | Фоном следит за сохранёнными поисками, сигнализирует о падении цен | Длительный фоновой агент с памятью истории — отдельный класс паттернов (long-running agents), не покрытый основной четвёркой | 1 |

Решение по любому из этих можно принять в брейнштормить-сессии подпроекта 1, 2 или 4. Все эти агенты, если будут реализованы, ложатся на тот же MAF-стек (см. 6.2).

---

## 7. Скоуп флагмана (подпроект 1, Flights)

**Tier 2 — целевой**, **Tier 3 — только ADR ("как бы я подходил")**.

### 7.1. Tier 2 — реализуем (разбито на milestones для статей в блог)

**M1 — "Search и бронирование одного пассажира":**
- One-way + round-trip search
- Mixed bookable (Duffel) + deeplink (Travelpayouts) aggregation в едином fan-out
- Single-passenger booking
- Order management: view, cancel
- Webhook ingestion + outbox + idempotency
- AI-фича #1: NL-search
- Identity (Keycloak) + sandbox payment (Duffel test wallet) + email-уведомление о подтверждении
- Полный observability контур
- → 2-3 статьи в блог

**M2 — "Multi-passenger и multi-leg":**
- Multi-passenger booking
- Multi-leg / open-jaw search
- Saved travelers (с шифрованием PII)
- AI-фича #2: explainable ranking
- → 2 статьи

**M3 — "Ancillaries и refunds":**
- Seat selection (Duffel ancillaries)
- Bag add-ons
- Full refund flow с политиками
- AI-фича #3: fare-rules summary
- SSE-уведомление "статус заказа изменился" по webhook
- → 2 статьи

### 7.2. Тестовая стратегия (для всех milestones)

- **Unit** — xUnit v3 на доменную логику, value objects, scoring algorithms; быстрые, in-memory
- **Integration** — Testcontainers (PostgreSQL + Redis + NATS реальные); проверяют Marten projections, EF queries, Wolverine handlers, sagas end-to-end
- **Architecture** — ArchUnitNET tests на boundaries модулей, dependency direction, naming conventions; запускаются в CI
- **Contract** — Pact (consumer-driven) на границы `Travel.Host` ↔ `Travel.AI`
- **AI-evals** — собственный framework (см. 6.3), пишут regression-тесты для NL-search, ranking, summary
- **E2E** — Playwright против `aspire run` стенда; ключевые user journeys
- **Visual regression** — Playwright `toHaveScreenshot()` с masked dynamic regions; проверяет ключевые экраны

### 7.3. Tier 3 — только ADR

- Partial refunds
- Schedule changes / involuntary changes от перевозчика
- Currency switching (платить в EUR, видеть в RUB)
- Group bookings (10+ pax)
- Loyalty / FFP
- Fraud signals
- A/B testing of ranking algorithms

---

## 8. Деплой

**Стратегия A:** локальный first-class опыт + деплой на собственный VPS пользователя.

### 8.1. Локальный
- `aspire run` поднимает всё (монолит, AI с MAF, NATS, PostgreSQL+pgvector, Redis, Keycloak, Mailpit, OTel collector + dashboard, Grafana + Loki + Tempo)
- Devcontainer (основной "click and code" путь); Codespaces — opt-in, см. раздел 9.1
- README со скринкастами / GIF ключевых сценариев
- Цель: "clone and run" за 30 секунд

### 8.2. Production (на VPS пользователя)

**Текущая среда:** 4 GB RAM / 2 vCPU / Ubuntu 24.04 LTS. **Не жёсткое ограничение** — масштабируем VPS по факту нагрузки. Реалистичный bound для полного стека (Keycloak + Postgres + Travel.Host + Travel.AI + Caddy + OTel) — 8 GB. Если ставим прод-LGTM или хотим запас — 12-16 GB.

- **Plain docker-compose + Caddy** — основной путь. Минимум движущихся частей. Caddy с auto-TLS через Let's Encrypt (HTTP-01 per-subdomain).
- `aspire publish` → `docker-compose.yaml` + `.env` (это реальный output Aspire 13 CLI, не legacy `production.yml` — пост-процессим наложением `docker-compose.production.yml` оверлея с прод-настройками)
- Subdomain под существующий блог пользователя
- CI/CD через GitHub Actions: build → push образов в GHCR → SSH-deploy на VPS

**Sizing-realism (полный стек на проде):**
- Keycloak: ~1.5-2 GB (heap 1.25 GB + non-heap 300 MB) — закладываем как фиксированный budget
- PostgreSQL 17: ~300-500 MB (с pgvector + Marten + EF schemas)
- Travel.Host (модульный монолит): ~300-500 MB
- Travel.AI (MAF + custom Travel Advisor): ~300-500 MB
- Caddy + OTel collector + Mailpit: ~200 MB вместе
- **Итого "минимум для запуска" — около 4 GB**; для комфорта и burst-нагрузки — 8 GB; для полного LGTM на проде — 12-16 GB
- Полный observability стек (Grafana / Loki / Tempo) — по умолчанию только локально; на проде — OTel collector + Postgres-экспорт + опционально Grafana Cloud free tier как метрик-эндпойнт
- SSD обязателен (PostgreSQL и логи убьют HDD по IOPS)
- Swap минимум 2 GB как safety net

---

## 9. Стратегия использования секретов и BYO-keys

- Никаких ключей в репо.
- `.env.example` в каждом сервисе с пустыми значениями и комментариями.
- README раздел "Bring Your Own API Keys" с пошаговыми ссылками на регистрацию каждого провайдера.
- При отсутствии ключа — graceful degradation: соответствующий провайдер просто не подключается, в UI/логах честное "источник не настроен, добавь ключ X в .env".

Ключевой архитектурный приём: **опциональные провайдеры**. Это сам по себе showcase — не каждый sample показывает graceful degradation на уровне доступности интеграций.

### 9.1. Доступность провайдеров из РФ

Честная таблица для README — какие интеграции реально работают на каком тире из РФ (на май 2026). Это **не недостаток**, а источник нарратива: showcase демонстрирует graceful degradation как первоклассный архитектурный принцип, а не побочный эффект.

| Провайдер | Sandbox | Production | Блокер |
|---|---|---|---|
| **Duffel (Flights/Stays/Cars)** | ✅ self-service, RU-карта не требуется | ❌ KYC через Stripe — отказ для RU-частника | Sandbox forever, документируется в README/ADR |
| **Travelpayouts/Aviasales** | ✅ partner-программа без ограничений | ✅ — RU-юрлицо, без блокеров | Полный доступ |
| **LiteAPI** | ✅ self-service free | ❌ KYC + business entity | Sandbox-only; RU-coverage всё равно тонкая |
| **Amadeus Hotels** | ✅ self-service | ⚠️ approval-friction с 2022 | Sandbox-only по факту |
| **Yandex.Rasp** | ✅ email-confirmed key | ✅ — RU-провайдер | Полный доступ; TOS-ограничения по кэшированию |
| **DB open data + db-vendo-client** | ✅ no auth | ✅ no auth | Полный доступ; rate-limits |
| **Anthropic API (для AI-фич)** | ✅ self-service | ⚠️ требует не-RU карту для оплаты | Документируется в README |
| **GitHub Codespaces** | ⚠️ free тир есть, но billing-карты RU-эмиссии заблокированы Stripe | ❌ платный metered, RU-billing невозможен | Не primary; devcontainers (local Docker Desktop / WSL2 / Podman) — primary |
| **Mapbox** | ✅ free tier | ⚠️ оплата сверх free — не-RU карта | Документируется |
| **OpenRouteService** | ✅ free key 2k req/день | ⚠️ платные тарифы — не-RU карта | Free tier достаточен для showcase |

**Архитектурный вывод:** ни один из этих "блокеров" не ломает план. Они формируют нарратив: "BYO-keys + опциональные провайдеры + честная documentation реальных ограничений" — это часть сильных сторон showcase'а. Именно так выглядит честная картина международного travel-стека для RU-разработчика в 2026.

---

## 10. Открытые вопросы (для следующих сессий)

Будут разобраны в брейншторминг-сессиях соответствующих подпроектов:

- **Подпроект 0 (Foundation):** конкретная структура каталогов NX-монорепо для смешанного TS+.NET; точный набор стартовых ADR (минимум: `0001-modular-monolith`, `0002-ai-as-extracted-service`, `0003-wolverine-marten-stack`, `0007-marten-and-ef-coexistence`, `0010-keycloak-identity`, `0011-notifications-channels`, `0012-payments-strategy`, `0014-data-layer`, `0015-ui-library`, `0019-ai-eval-strategy`, `0020-maf-as-primary-agent-runtime`); конфигурация NX targets; конкретика devcontainer; CI pipeline; раскладка `prompts/`, `tests/ai-evals/`, `.claude/agents/`, `.claude/commands/`; раскладка тестов (unit/integration/architecture/contract/ai-evals/E2E/visual); конкретика Keycloak + Mailpit в Aspire манифесте; конкретика AI-harness — детализация custom-агентов (`domain-modeler`, `adr-writer`, `test-author`, `migration-author`, `integration-mapper`) с примерами входов/выходов; детализация slash commands (`/spec`, `/adr`, `/explore-domain`, `/test-this`, `/integration-from-openapi`); шаблон блог-поста в `docs/blog-template.md`.
- **Подпроект 1 (Flights):** дизайн `IFlightProvider` контракта; маппинг Duffel offer model → доменная `Offer`; событийная модель `BookingAggregate`; saga для booking lifecycle; конкретная реализация трёх AI-фич; UX/UI для mixed bookable + deeplink результатов; интеграция Duffel test wallet через `IPaymentGateway`; SSE-уведомление по webhooks; разбивка на milestones M1/M2/M3.
- **Подпроект 2 (Hotels):** дизайн `IHotelSupplier` × 3; алгоритм dedup; pluggable scoring strategies; latency budget пайплайн.
- **Подпроект 3 (Rail):** структура read-only CQRS; конкретика интеграции Yandex.Rasp + DB; обработка отсутствующих ключей.
- **Подпроект 4 (Trip Planning):** схема `TripAggregate`; конкретный дизайн четырёх агентов на MAF (Trip Planner Orchestrator, Flight/Hotel Search Agents, Itinerary Composer); как orchestrator делегирует sub-агентам через MAF group chat / handoffs; политика памяти; UI для итинерария; нотификации в trip lifecycle; точка принятия решения по агентам из Банка идей (раздел 6.4); возможное разбиение подпроекта на 4a (Trip aggregate + side-cars) и 4b (агенты), если объём станет неподъёмным.
- **Подпроект 5 (AI service core):** конкретный дизайн MAF-платформы (tool registry поверх Wolverine handlers, A2A через JetStream, observability hooks, eval-bridge); реализация Travel Advisor на собственном минимальном runtime как образовательная "deconstruction"; ADR `0020-maf-as-primary-agent-runtime.md` и `0021-deconstructed-runtime-rationale.md`; конкретика своего eval framework; cost ledger; MCP server (поздняя стрейч-цель).

---

## 11. Что зафиксировано

- Видение, аудитория, двухслойная модель продукта (Инфра / Ценность)
- Стратегия C: флагман + спутники, декомпозиция на 6 подпроектов с порядком 0 → 1 → ... → 5
- Выбор интеграций по доменам (с явной таблицей RU-доступности в разделе 9.1)
- Архитектурный стиль: modular monolith (`Travel.Host`) + один реально вынесенный сервис (`Travel.AI`); связь через Wolverine + NATS JetStream + PostgreSQL outbox; Aspire оркестрирует оба процесса
- Storage strategy: Marten для booking lifecycle (selective event sourcing), EF Core для всего остального; Marten и EF в одной PostgreSQL без пересечения схем
- Cross-cutting concerns: identity (Keycloak), notifications (email + SSE), payments (Duffel test wallet через `IPaymentGateway`)
- Полный стек: .NET 10 + Aspire + Wolverine + Marten + EF Core 10 + PostgreSQL 17 + NATS JetStream + Redis + custom endpoint framework + custom `Result<T>` + xUnit v3 + Verify + Testcontainers + ArchUnitNET; Angular 21 (с прицелом на v22) + httpResource + NgRx SignalStore + zoneless + hybrid SSR + Tailwind v4 + PrimeNG + Vitest + Playwright + Storybook 10; NX 22 + `@nx/dotnet` + heyAPI + Pact + Biome + CSharpier + Roslynator analyzers
- AI-стратегия: оба слоя (assisted dev + product features), MAF primary для 4 продуктовых агентов + собственный минимальный runtime для Travel Advisor как образовательная демонстрация, свой eval framework, pgvector через community-binding
- Скоуп флагмана разбит на milestones M1/M2/M3 для incremental delivery и серии статей
- Тестовая стратегия: unit / integration / architecture / contract / ai-evals / E2E / visual
- Стратегия деплоя: plain docker-compose + Caddy; VPS 4 GB как старт, scalable до 8-16 GB по факту нагрузки
- BYO-keys стратегия + опциональные провайдеры с graceful degradation
- Решения web-верифицированы по официальным источникам на 2026-05-03 (5 параллельных кластеров: AI / .NET / Angular / travel-провайдеры / инфра)

---

## 12. Что дальше

1. Документ финализирован после **трёх итераций ревью**, последняя из которых — параллельная web-верификация по 5 кластерам (AI-стек, .NET-экосистема, Angular/FE, travel-провайдеры, инфра/деплой) с проверкой каждого технического утверждения по официальным источникам на 2026-05-03.
2. Следующий брейншторминг-цикл — детальный спек подпроекта 0 (Foundation): структура каталогов NX-монорепо, стартовый набор ADR (~10 штук, см. раздел 10), конфигурация NX targets, devcontainer, CI pipeline, AI-harness в деталях, раскладка Keycloak + Mailpit в Aspire, тестовая инфраструктура.
3. После Foundation — цикл по подпроекту 1 (Flights флагман), сразу с разбивкой на M1/M2/M3 milestones.
4. Дальше — по порядку: Hotels (2), Rail (3), Trip Planning (4 — возможно 4a + 4b), AI service core (5).
5. **Этот документ — северная звезда.** Каждый последующий цикл может ссылаться на него, но не должен его дублировать. Если в спеке подпроекта появляется решение, противоречащее концепту — обновляем сначала концепт, потом продолжаем спек.

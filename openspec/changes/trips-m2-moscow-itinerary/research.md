# Trips M2: current state, gap map и источники

Дата проверки baseline:2026-10-20. Это сохранённая current-state/v1source map. После исследования21Oct2026 выбран пересмотр offlineOSM/modelled-demo;текущий design§2 и data-strategy-revision.md supersede v1source proposals ниже. Наблюдаемые факты отделены от предлагаемых решений.

## 1. Отправная точка и ancestry

- Общий checkout не переключался. Для M2 создан managed worktree `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-plan/travel-agency` от свежеполученного `origin/dev=30cd68d892cf72994c83047a22ceff782814796f`. HEAD совпадает; ancestry проверена. Rail использует другой worktree.
- PR58 implementation merge — тот же SHA. [PR59](https://github.com/svasorcery/travel-agency/pull/59) на первом live check открыт, head `3b95b749107da9cdde07d9d8ca173293ab38764c`. Его docs closure прочитана через `git show`, не скопирована в новый change.
- [M1 finalization at exact commit](https://github.com/svasorcery/travel-agency/blob/3b95b749107da9cdde07d9d8ca173293ab38764c/openspec/changes/archive/2026-10-20-trips-m1-structured-planning/finalization.md) связывает all21tasks, independent six-P2 correction/recheck, final PR head `143f6428d4a2c6d036c4b5596899be2eecc96dd4`, PR CI39457675085/1 и dev CI39466946450/1. Это прочитанная запись; CI не перезапускалась.
- Прочитаны full accepted [M1 contract at closure commit](https://github.com/svasorcery/travel-agency/blob/3b95b749107da9cdde07d9d8ca173293ab38764c/openspec/specs/trip-plan-workspace/spec.md), design (особенно §7), proposal, tasks и implementation-review из того же commit. Нет отдельного M1-корпуса в M2.
- Пока PR59 не вошёл в базу worktree, локальные current M1 ссылки ещё активные. Будущий integration берёт свежий origin/dev и сохраняет ancestry исходной свежей базы;canonical paths используются после mergePR59,до этого accepted closure читается по точному commit. Merge документационного PR не становится скрытым runtime prerequisite;M1 не переписывается для валидатора.

## 2. Проверенная реализация, а не roadmap

| Слой | Реальный consumer/контракт | Следствие для M2 |
|---|---|---|
| Core | [TripBrief](../../../modules/trips/Travel.Modules.Trips.Core/TripBrief.cs), [TripPlan](../../../modules/trips/Travel.Modules.Trips.Core/TripPlan.cs), [TripValues](../../../modules/trips/Travel.Modules.Trips.Core/TripValues.cs) | 1–14days,1–9adults, три city IDs; stable block IDs,8/day,112total, overlap warning, exact estimates; нет POI/fact/itinerary generation |
| Application | [TripPlanService](../../../modules/trips/Travel.Modules.Trips.Application/TripPlanService.cs), [ports](../../../modules/trips/Travel.Modules.Trips.Application/TripPlanContracts.cs) | Owner-before-decrypt; one-attempt replace/CAS;128KiB full canonical precommit cap; нового write orchestrator не требуется |
| Infrastructure | [codec](../../../modules/trips/Travel.Modules.Trips.Infrastructure/Serialization/TripPlanJsonCodec.cs), [store](../../../modules/trips/Travel.Modules.Trips.Infrastructure/Persistence/TripPlanStore.cs), [protection](../../../modules/trips/Travel.Modules.Trips.Infrastructure/Privacy/DataProtectionTripPlanProtector.cs) | Strict v1 snapshot не содержит source references; нельзя незаметно дописать trusted provenance в existing wire/persistence |
| Api/Host | [endpoints](../../../modules/trips/Travel.Modules.Trips.Api/Endpoints/TripPlanEndpoints.cs), [Composition](../../../modules/trips/Travel.Modules.Trips.Api/Composition/TripsModule.cs), [Host](../../../apps/Travel.Host/Program.cs) | Новый endpoint требует отдельной route policy поверх текущего facade:сейчас каждый POST проверяется как M1 create,а GET идёт в plan readiness. M2 body/availability/pre-auth-off ветки нужно явно выделить,сохранив M1 guards/Host order |
| TS/editor | [types](../../../shared/ts/api-client/src/trips.types.ts), [decoder](../../../shared/ts/api-client/src/trips.decoder.ts), [state](../../../apps/web/src/app/trips/trips-state.service.ts) | Уже есть session/plan/read epochs, dirty/form separation, monotonic revision, uncertain-write barriers; candidate должен участвовать в них, не обходить их |
| Tests | [Core tests](../../../tests/trips/Travel.Modules.Trips.Tests.Unit/TripPlanTests.cs), [service tests](../../../tests/trips/Travel.Modules.Trips.Tests.Unit/TripPlanServiceTests.cs), [HTTP](../../../tests/trips/Travel.Modules.Trips.Tests.Integration/TripsHttpTests.cs), [browser config](../../../tests/travel-e2e/playwright.trips-demo.config.ts), [.github CI](../../../.github/workflows/ci.yml) | Продолжать существующие seams и ordinary jobs, без новой параллельной DB topology |
| AI | [Travel.AI instructions](../../../apps/Travel.AI/AGENTS.md), [fare-summary limits](../../../apps/Travel.AI/FareSummary/README.md), [published leaf](../../../shared/dotnet/Travel.IntegrationContracts.AI/Travel.IntegrationContracts.AI.csproj) | Direct Anthropic/IChatClient уже есть; MAF/SK не runtime. Fare-summary доказывает bounded candidate, не Trips route quality. Этот M2 не имеет AI consumer |

M1 source/CI green не доказывает existing target schema/key readiness или enabled Trips Host/Keycloak acceptance. [Operations](../../../modules/trips/operations.md) отделяет эти gates. Никаких runtime проверок сейчас не было.

## 3. Недостающие части

1. Разрешённый реальный набор Moscow POI и directed walking connections с provenance/validity; city metadata не доказывает покрытие.
2. Проверяемая политика подбора под pace/interests без выдуманных часов/пути/цен.
3. Бounded candidate contract, отдельная ветка UI, honest partial/empty/unknown outcomes.
4. Human-quality protocol и источник реальных критериев маршрута, отдельно от fake structural tests.
5. Точная семантика переноса preview в существующий editor и сохранения намерений без новой системы доверенных supplier facts.

## 4. Read-only source feasibility, 2026-10-20

| Источник | Проверено | Решение/граница |
|---|---|---|
| [Wikidata licensing](https://www.wikidata.org/wiki/Wikidata:Licensing), [data access](https://www.wikidata.org/wiki/Wikidata:Data_access) | Structured data опубликованы CC0; другие namespaces имеют другие права | Выбрать для identity/названий/координат, вручную проверить конкретные entity IDs/revisions. Не переносить Wikipedia prose, фото, часы/цены из неразрешённых внешних страниц |
| [OpenStreetMap copyright](https://www.openstreetmap.org/copyright) | ODbL, attribution и отдельные service policies | Допустимая будущая альтернатива, но не выбирать OSM routing/tiles/API для первого среза. Это дополнительные obligations и эксплуатация, не бесплатный готовый маршрутный API |
| [Официальный Зарядье](https://www.zaryadyepark.ru/) | Прямое чтение завершилось timeout; поисковая выдача содержит старые материалы | Не подтверждает актуальные часы/права reuse. Ни один факт hours/access этого места не принят. Официальный URL сам по себе не лицензия |
| Собственная curated редакция | Сейчас не существует принятого пакета | Короткие оригинальные описания/tags/recommended dwell — собственная редакция; доступ/часы и реальные walking observations требуют конкретных evidence/прав. Нельзя назвать художественно придуманные walking minutes измерением |

Это оценка стратегии, не юридическое заключение и не data-acceptance receipt. Для каждой будущей записи отдельно фиксируются права, разрешённые поля, наблюдение/источник, дата проверки, горизонт и reviewer. Если достаточного разрешённого набора не получится, data gate остаётся failed: не подменять его вымышленными местами или молчаливым подключением платного provider.

## 5. Выбранное направление автора

Moscow centre, outdoor sightseeing,1–3days, существующая structured form; deterministic first. Источник идентичностей — проверенные CC0 Wikidata records; остальные factual поля только из собственных наблюдений либо источника с доказанным разрешением. Никакого scraping/import runtime.

Кандидат — порядок реальных остановок и проверенные пешие связи с ограничениями; precise arrival schedule не обещается. Цены/availability не подбираются. Food wishes, indoor visits, hotel/origin transfers и неподдержанные географии отмечаются unmet/out-of-scope. Их отсутствие нельзя скрыть словом complete.

Начальные численные пределы в design — решения автора для ограниченного пилота, не измеренные SLA. Реальная Москва, source freshness и human suitability ещё не приняты; это первые обязательные задачи после отдельного согласования.

## 6. Current-source Rail consumer evidence, 2026-10-21

The preceding sections are the historical planning/source research snapshot. Their "not implemented", initial source strategy and future checks are not current completion claims. Walking tasks now record17 prior accepted deliverables; after integrating a8721effd9056459d217966083816455fbea599d, current Rail/build/dependency proof is still separately required. Frozen walking inputs and the existing candidate delta are unchanged.

| Read source | Current evidence | Design consequence |
|---|---|---|
|[Rail published contract](../../specs/rail-schedule-search/spec.md)|Explicit stations/date/modes, four outcome kinds, current-profile temporal authority, mandatory attribution, no durable supplier retention/export/combination|Rail search is reusable; real data must remain transient. Only fictional rows can become automatic M1 intentions; real persistence needs future contract/rights work|
|[Rail types](../../../shared/ts/api-client/src/rail.types.ts)|RailRef, RailJourney, RailSearchResult/Suggestions carry source/profile/profileEpoch/catalogueGeneration, receivedAt/expiresAt; IANA/sourceUpdatedAt null; exact amount string|Use public TS types, preserve unknowns and source lifetime; never infer fare scope/timezone|
|[Rail strict decoder](../../../shared/ts/api-client/src/rail.decoder.ts)|Request/mode/count/identity authority, bounded refs, original expiry; source code≤128bytes, thread≤100UTF16units/400bytes, key≤1024bytes; damaged ticket TTL30s vs ordinary300s|Click-time recheck plus actual note/full-write preflight; maximal valid identity may not fit500scalars, reject rather than truncate|
|[Rail API](../../../shared/ts/api-client/src/rail-api.service.ts)|One public stations/search client, transferCache:false, strict decoding, bounded timeout|No new client/backend aggregator, no private Trips fields|
|[RailState](../../../apps/web/src/app/rail/rail-state.ts)|Independent field epochs, request sequence, profile authority, AbortControllers, expiry/dispose; same-profile failed reference response can leave previous reference retained|Reuse state, add narrow eligibility/failure invalidation so pending choice cannot reuse a stale successful bundle; no second algorithm|
|[Rail page](../../../apps/web/src/app/rail/rail-page.component.ts), [template](../../../apps/web/src/app/rail/rail-page.component.html)|Search form, browser-only adapter, exact banner URL allowlist, enforced CSP reaction, sandbox/no-referrer, current document-global option focus query|Extract one component with scoped IDs/focus; retain standalone regression and all attribution behavior|
|[Rail parser](../../../modules/rail/Travel.Modules.Rail.Infrastructure/YandexRailScheduleParser.cs)|Mode limited reasons page-limit and interval-service-unsupported; optional ticket damage is separately represented|Admit intact concrete rows only for these selected-mode limited reasons, or selected complete mode under mixed envelope|
|[Trips state](../../../apps/web/src/app/trips/trips-state.service.ts)|Opaque branded foundation in WeakMap, monotonic foundationVersion, session/read/edit ticket, cleanEmpty, decodeTripWrite before one edit|Refactor shared clean-saved foundation, retain empty-only walking wrapper; Rail nonempty append uses one atomic validated local edit|
|[Trips template](../../../apps/web/src/app/trips/trips-page.component.html)|Existing M1 form spans editor and walking preview|Place Rail as sibling outside M1 form; prove Enter/search causes0PUT|
|[Trips types](../../../shared/ts/api-client/src/trips.types.ts), [decoder](../../../shared/ts/api-client/src/trips.decoder.ts)|Ordinary block title/note/null times/null estimate, fixed M1 city catalogue and full-write bounds|Persist small fictional historical plain note only, preserve100/500scalars/8day/fullUTF8 and manual1–14days|

Implementation details and preflight are in [rail-refinement.md](rail-refinement.md). The controller owns [rail-dev-integration.md](rail-dev-integration.md); any completion status comes from its exact receipt, not this author. The initial fresh base remains e654c76915103a82083ef2abc01c0579117c9749, not current HEAD. No provider/model/Host/DB/network audit or Q-output execution occurred in this refinement.

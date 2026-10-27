# Design

## Context

Мотивация — [proposal](proposal.md). Исследование выполнено 26.10.2026 по свежей базе `11ba5a1572e53cd901ae4ec56b77b482ec95f699`; ветка `codex/trips-m22-saint-petersburg`. Все решения ниже — предложение автора плана, не уже принятая реализация.

Проверенные ограничения исходников:

| Место | Текущее поведение | Следствие |
| --- | --- | --- |
| `TripValues.cs`, `TripPlan.Restore`, `trips.decoder.ts` | Три city ID; сохранённая catalogueVersion должна быть `1` | СПб уже существует; bump версии сломает restore/TS и не нужен |
| `IItineraryFactSource.ReadAsync`, `ItineraryCandidateService.ScopeReason` | Один источник без destination; scope Moscow-only | Выбирать источник по авторитетному сохранённому destination до admission |
| `CuratedMoscowFacts/Release` | Moscow ID/zone/URL/bbox/file names/manifest pin | Вынести только действительно различающиеся параметры в доверенные city descriptors |
| `build-moscow.cs`, `moscow-refresh.mjs`, `PackageVerification.cs` | Московские bbox, metadata, attribution и release baseline | Один общий код с сохранённым московским форматом и явным SPb профилем |
| `TripsRequestGuardMiddleware`, availability endpoint | Availability запрещает query/body; candidate принимает только `{}` | Добавление города требует нового opt-in контракта guards/encoder/client |
| `trips-itinerary.decoder.ts/types.ts` | Moscow-only v1, фиксированный offer; unsupported reason зависит от city | Нельзя просто расширить enum существующего v1 |
| `trips-itinerary-state.service.ts` | Adoption связывает owner, revision, epochs и availability | В каждый dispatch/reread включить protocol/destination; сохранить A→B→A guards |
| `local-stack.mjs`, full-stack spec | E2E clock 2026-10-24, Moscow nine-stop assertions | SPb подготовится позже: старый clock не может валидировать новый build |

Основание завершённых московских работ — `docs/architecture/trips-completion-review.md` и архивы, указанные в handoff. Их не повторяем. Архивы TestResults, остановленное demo и формы Q01–Q12 не входят в изменения.

## Goals / Non-Goals

**Goals:** один второй consumer тех же policy/validator; независимая корректность двух package identities; строгая обратная совместимость запросов и сохранённых планов; измеримая стоимость подготовки; честные partial/empty/unknown.

**Non-Goals:** глобальный registry/provider framework, добавление городов в каталог, изменение beam/ranking/pace/model минут, полноценное расписание, ночная навигация, indoor/входы/билеты, питание, accessibility/safety claims, транспорт через Неву, M3/AI, migrations, target rollout.

## Decisions

### 1. Район и доказательство покрытия

Выбран компактный исторический центр на левом берегу Невы: Дворцовая/Адмиралтейская часть, Исаакиевская площадь и Мойка, центральный Невский до Фонтанки, Инженерная улица и Марсово поле. Предложенный bbox `[59.923,30.290,59.947,30.346]` — ограничитель подготовки, не обещание покрытия всей площади.

[Досье](source-dossier.md) задаёт 20 исследовательских кандидатов и конкретные соседства для проверки. Итоговый SPb v1 обязан иметь 18–20 уникальных POI, минимум три на каждый поддержанный тег architecture/culture/history/nature, и 60–100 направленных связей. Это более узкий срез существующих 18–48/60–256 caps. Не прошедший anchor/rights/path POI исключается с причиной; недостаток ниже минимума означает data gate failed, а не искусственные точки/теги. Замена внутри района допустима с явным dossier delta и независимым аудитом до заморозки Q; расширение района/ослабление caps требует пересмотра плана.

Публичный городской туристический портал подтверждает разумность районов/объектов, но его тексты, фотографии, готовые маршруты и заявленные минуты не копируются. Источник координат/identity — конкретные ревизии Wikidata CC0; граф — один сохранённый OSM XML/gzip BBBike с SHA256. Hours/access/cost остаются unknown. Описания/интересы/dwell — оригинальные редакционные рекомендации; food не подменяется названием магазина или видом ресторана.

Альтернативы: только ансамбль трёх площадей слишком тесен для 18–20 отдельных интересных остановок и трёх дней; Петропавловская крепость/Васильевский остров/Петергоф добавляют другой транспортный и access scope. Эти варианты отложены.

### 2. Общий builder и loader, две доверенные конфигурации

Сохранить `build-moscow.cs` как совместимую точку запуска общего кода; добавить явный `--destination saint-petersburg`. Без аргумента действует существующий Moscow profile. Не копировать routing, модели, policy или validator. Вынести параметры destination/zone/bbox/source URL/coverage/preparedBy/file names/attribution в небольшой закрытый набор из двух доверенных профилей, не из runtime JSON или request.

Moscow output должен побайтно воспроизводить все шесть прежних файлов, включая строки attribution и command. Его `profile-v1`, `pair-selector-v1`, `model-v1`, даты и pin остаются. SPb использует `saint-petersburg-v1.json`/manifest и отдельный `spb-outdoor-profile-v1`; selector nearest-five и модель 75/45 м/мин + crossing только в верхней границе остаются общими.

Для SPb нужны воспроизводимые исключения путей через Летний/Михайловский сады, дворцовые дворы и переправы через Неву, даже если OSM не содержит deny/conditional tags. Сначала из snapshot составляется проверенный список исключаемых way IDs; он хранится в `preparation-profile.json` с обоснованием/источником и входит в SPb manifest. Общий builder исключает эти ways до shortest-path/nearest-five выбора. Loader проверяет city/profile/hash и отсутствие этих way IDs в каждом предложенном пути. Это SPb-only дополнительный фильтр поверх существующей таблицы access/direction, без смягчения правил; Moscow набор файлов и алгоритмический результат неизменны. Досье аудита проверяет полноту исключений; отсутствие OSM запрета само по себе не доказывает доступ.

Сохраняются anchor <=50m, directed path evidence и все resource caps: 200 MiB compressed, 2 GiB expanded, 750000 indexed coordinates, 100000 graph nodes/50000 ways, пять минут и наблюдаемый 1 GiB process budget. Ни увеличение радиуса привязки, ни прямые линии, ни обходы ресурсов автоматически не разрешены. Координату здания нельзя незаметно переносить на тротуар: если выбранная identity coordinate не даёт разрешённый anchor, POI заменяется либо отдельно пересматривается evidence schema.

Два последовательных build с одним retained input, profile, inventory, helper hash и as-of сравниваются побайтно; затем реальный строгий loader через `package-check`. Новый snapshot не скачивается в обычном CI. Точные bytes/hash/число связей/ресурсы пока не измерены: подготовка данных относится к apply, не к этому плану.

Core ItineraryModelSource.Create сейчас допускает только profile-v1. Расширить этот закрытый набор двумя известными профилями и проверить связь profile с destination в ValidatedItineraryFacts; общий ItineraryCandidateValidator должен отвергать несовместимую пару. Не переносить provider URL/DTO в Core и не принимать произвольные строки профиля. Для legacy Moscow оставить profile-v1; spb-outdoor-profile-v1 допустим только для SPb. Добавить прямые positive/negative factory и validator tests, сохранив общий policy, minute model и resource limits.

### 3. Destination selection и разделение отказов

Application `IItineraryFactSource` получает `DestinationSelection`; Core по-прежнему получает нормализованные validated facts. Composition создаёт ровно два доверенных release descriptor: Moscow v1 и SPb v1 после data acceptance. Descriptor связывает destination/zone/catalogueVersion, dataset/version, manifest pin, bbox, source URL, profile, file set и offer. Не сканировать папки и не выбирать latest. Новые значения не приходят из пользователя/пакета.

Owner Get, exact revision и saved-empty проверяются раньше выбора пакета. Неподдержанная география/даты не читают source и не занимают admission. Для поддержанного destination отсутствие/expiry/corruption/withdrawal даёт unavailable, а не unsupported/empty. Ошибка SPb не загружает Moscow как fallback и не портит его readiness. Существующий глобальный Withdrawn остаётся общим выключателем; добавить destination-specific withdrawal в конфигурации внутри общего flag, без включения по умолчанию.

Сохраняются один общий процессный gate на две вычислительные операции, deadline 2s, beam16/20000 expansions, source matching, последняя owner/revision/expiry проверка и frozen encoder bytes. Два города не удваивают capacity. M1 readiness не зависит от источников маршрутов.

### 4. Явная совместимость HTTP/TS

Не создавать второй route family. На существующих маршрутах определить opt-in header:

| Запрос | Поведение |
| --- | --- |
| Header отсутствует | Весь прежний v1: availability без query — Moscow; candidate Moscow работает, SPb/Kaliningrad дают прежний unsupported v1 |
| `X-Trips-Itinerary-Version: 2`, GET availability | Ровно один case-sensitive `destinationId=moscow\|saint-petersburg\|kaliningrad`; без body |
| v2, POST `/{tripId}/itinerary-candidate` | Тот же <=1 KiB `{}`, без query; один strong If-Match; destination исключительно из сохранённого плана |
| Иная/повторная/списочная/пустая версия | 400, до package/plan computation; отсутствие header — единственный legacy default |
| v1 + любая query или v2 GET без/с дублем/неизвестным ключом/ID | 400; не угадывать город |

v2 ready и computed responses имеют прежнюю bounded структуру с `candidateVersion:2` и точной city/package tuple. Ready для Kaliningrad заменяется честным 200 ответом `{candidateVersion:2,status:"unsupported",destinationId:"kaliningrad",reason:"destinationUnsupported"}`, без dataset identity. Для поддержанного города unready остаётся typed503. HTTP404 disabled до auth/dependencies, 401/403, 409 nonempty, 412, 428, 429 Retry-After1, 504 сохраняются. Ответы не кэшируются; availability не требует plan DB/protection readiness.

Новый TS client запрашивает v2 для обоих городов; старые методы/fixtures без header сохраняют v1 поведение. Декодеры сначала разделяют protocol, затем проверяют точные shapes и допустимые destination/dataset/zone/source/profile/offer tuple. Не принимать произвольный dataset/URL потому, что он синтаксически валиден. Для v2 Moscow допускается только опубликованная Moscow v1 identity, для SPb — отдельно закреплённая SPb v1. Сохранить lossless revisions, unknown/duplicate-property refusal, dates/expiry, 128 KiB/3days/12blocks/96facts, 2 KiB availability, exact money и plain text.

В Angular availability запрашивается для destination сохранённого foundation, а не несохранённого select. Capture protocol+destination+owner+plan+local epoch; late A→B→A, logout, edits и navigation инвалидируют preview и adoption ticket. Перед transfer — owner GET и availability того же города с matching protocol/dataset/version/hash/policy/expiry. Transfer остаётся локальным; отдельный Save использует M1 CAS. Никаких автоматических повторов. Source offer берётся из проверенной tuple, не конструируется из произвольного URL. M1 и Rail guards сохраняются; в Rail spec меняется только устаревшая фраза Moscow-only walking.

Альтернатива «расширить v1 enum на месте» отвергнута: старый strict decoder отвергнет SPb candidate и перестанет понимать прежнюю unsupported семантику. Явный header сохраняет старую систему полностью и не требует отдельного endpoint.

### 5. Bootstrap и обслуживание

Расширить существующую maintenance-команду `--destination`; отсутствие аргумента сохраняет Moscow. Разрешённые URL фиксированы в профилях; никакого `--url` и redirects.

- `check` сообщает city, source metadata и installed identity/validity раздельно; для первого SPb baseline отсутствует (`not-installed`), а не сравнивается с Moscow.
- `prepare-initial --destination saint-petersburg --version 1 --output ...` разрешён только при отсутствии reviewed SPb release. Требует явного inventory/profile, допустимых прав, реальной публикации, одного ограниченного GET с SHA256 и двух сборок; «новее предыдущего» неприменимо без city baseline.
- После первого release обычный `prepare` требует новую версию, строго более новую публикацию и другие bytes именно этого города. Нельзя повторно bootstrap поверх release.
- `reproduce-current --destination ...` использует exact retained raw; baseline другого города не принимается. `package-check` требует явный destination и trusted tuple и сохраняет offline read-only boundary.
- Все успешные подготовки — `candidate-needs-review`, loader check — `validated-not-released`. Ни pins, ни assets, ни flags, ни saved plans не меняются.

Moscow real-clock reproduction разрешено только до исходного expiry. После него CI golden/fixture parity остаётся проверкой формата, но не называется свежей реальной репродукцией. Невозможность вновь собрать старый пакет не даёт права редактировать его время.

### 6. Saved-plan совместимость

`CityCatalogue.Version="1"`, protected snapshot schema и сериализованный M1 контракт неизменны. Добавить tests на старые Moscow и уже существовавшие SPb ручные snapshots, новые SPb candidate-derived intentions, plaintext/detail/TS/protection round trips. Чтение не нуждается в package и сохраняет historic displayName/zone/version.

До upgrade/package switch снять full owner GET и ciphertext/row/key fingerprints; после выбора другого пакета, expiry, corruption, withdrawal и полного restart сравнить IDs, order, title/note, exact amounts, owner, revision, timestamps, protected bytes и keys. Затем явная ручная правка должна сработать с обычным CAS. В mutable note нет доверенной provenance; сохранённый текст не доказывает текущую доступность.

### 7. Приёмка и время E2E

Сохранить старый Moscow full-stack сценарий целиком: fixed `2026-10-24T12:00:00Z`, nine stops, единственный original candidate POST, проверки adoption без write, реальные PKCE/Host/PostgreSQL, полный restart и owner/CAS refusals. Декодерная совместимость v1 проверяется отдельно; новая UI использует v2.

SPb full-stack проходит тем же runner и механизмом original-response capture с отдельным фиксированным application/browser instant, выбранным ПОСЛЕ реального build SPb и ДО expiry всех использованных facts через конец выбранных трёх локальных дат. Нельзя задним числом поставить build 24 октября. Добавить явные именованные E2E profiles Moscow/SPb (default Moscow для обратной совместимости), валидировать clock из checked-in profile, сохранять wall-clock Keycloak/JWT/key validation и запрет test-clock в Production.

В SPb run сохраняются оба плана: старый Moscow golden M1 content через реальные POST/PUT (без подмены ответов/БД) и новый generated SPb. Если их freshness windows не пересекаются, Moscow plan остаётся историческим; новая Moscow генерация доказана отдельным прежним профилем. После полного restart сравниваются обе authoritative representations/ciphertexts и keys, свежий login, чужой owner и stale revision для обеих поездок. Compatibility тест с payload, произведённым предыдущим кодеком, отдельно доказывает upgrade чтения; новый golden POST сам по себе этого не доказывает.

Обычный CI выполняет оба профиля последовательно на принадлежащих им ресурсах, aggregate gate зависит от обоих и cleanup; synthetic regressions не заменяют этот lane. Старые timeouts/retries/assertions не ослаблять. Privacy export сохраняет allowlist JSON/PNG и не включает секреты/архивы. Существующее пользовательское demo не запускать/не пересобирать.

Разделить результаты:
- **D_demo:** разрешённые входы, hash/bbox/profile, real two-build/strict validation/resource receipts.
- **S/Q_demo:** 12 SPb real-package cases (1/2/3 days, все pace, mixed/nature/food/none, границы дат) и отдельные 12 structural/negative fixtures. Заморозить входы/пороги до просмотра outputs, связать hash+policy+clock до запуска. Для заявленных positive targets требовать заполнение 2/3/4 stops/day без повтора, соблюдение caps и корректную provenance; food-only/expiry cases проверяют честное partial/empty, не «заполнение любой ценой». Для каждого заранее указать ожидаемый outcome/coverage/interest constraints. Все обязательные assertions должны пройти; failed cases не выкидывать и baseline не переименовывать после tuning.
- **Factual audit:** каждое identity/anchor/tag/path/exclusion проверяется по разрешённым материалам; это не обход на местности и не human rating. Отдельно проверять, что близкие остановки не являются дубликатами одного объекта.
- **Human A/B:** два независимых человека, существующие thresholds (каждый score >=3 и среднее двух >=4 по измерению для каждого из 12 cases), оценки остаются пустыми до участия людей. Moscow deferred не меняется.
- **CI / T / R:** source/local, ordinary PR/post-merge CI, target provisioning и real-user rollout — отдельные статусы. M2 task6.3 не закрывается этим change.

## Risks / Trade-offs

- 97 MiB compressed не доказывает fit expanded/graph/time/memory → измерить реальный input до интеграции; отказ не обходить повышением caps.
- Центральные POI могут иметь удалённые от улицы Wikidata coordinates, у трёх nature-кандидатов ещё не подтверждены anchor/identity → gate на конкретные records; при нехватке пересмотреть inventory, не подделывать coverage.
- Не все закрытые/условные территории размечены OSM → SPb exclusion profile плюс полный path audit; без текущей калибровки всё остаётся modelled.
- Две версии протокола увеличивают тестовую матрицу → сохранить старый golden корпус и узкие таблицы, без двух generators.
- Сроки пакетов могут не пересечься → отдельные clock profiles и исторический Moscow content в SPb preservation run.
- Стоимость curation больше стоимости API → вести реальные затраты из dossier и не расширять географию автоматически.
- Отдельный default-off source pass может восприниматься как готовность туристического продукта → показывать D/S/Q_demo/Factual/Human/T/R отдельно.

## Migration Plan

DB/key/schema migration не нужна. После отдельного approval: подготовить и проверить SPb candidate, провести source acceptance, добавить его trusted descriptor/assets в source change вместе с v2 code/tests; publication и rollout требуют собственных разрешений. Moscow v1 files/URLs сохраняются. Rollback новым городом: withdrawal SPb или общий itinerary flag, без удаления plans/keys; M1 и Moscow остаются. Откат binary к базе сохраняет readability обоих M1 city snapshots, но SPb generation снова unsupported.

## Open Questions

Нет нерешённого продуктового выбора, блокирующего рассмотрение плана: район, версия протокола и форма первого пакета предложены явно. На apply нужно измерить exact Wikidata revisions/anchors, excluded-way coverage, число/связность paths, hashes, ресурсы и часы curation. Если эти проверки провалятся, остановить соответствующий gate и представить ограниченное изменение inventory/профиля; не менять scope скрытно.

# Hotels M2: Duffel + Hotelbeds

## Context

**Статус2026-10-19: утверждённый code/autonomous scope реализован агентом `gpt-6.1-sol` и лично проверен основным агентом;22/25 задач, открыты6.1–6.3.** Утверждены Duffel + Hotelbeds, полный booking второго поставщика и все решения ниже после саморевью. Партнёрского доступа нет. Разрешённый sync M1 выполнен; relational/ordinary Host/privacy proofs, CI, external runtime, миграции, нагрузка и deployment остаются отдельными gates. Итоговые receipts — в [verification.md](verification.md).

Исходная проверенная база `dev = origin/dev = 75dfba532c0f321286630e3d04fbea70347903a7` новее M1 checkpoint. В этой базе M1 имел один default-off Test-only Duffel adapter, Angular booking, защищённые reservation/quote/attempt и ограниченное observation по известному booking ID. Search был последовательным; provider-key в capacity ещё не разделял counters. HotelProperty не содержал structured matching evidence; cancellation/board были в основном display facts. Fake проверяет контракт, не является второй внешней интеграцией. Эти исходные пробелы являются основанием M2, а не описанием итогового изменённого кода.

Основания: [M1 design](../hotels-m1-duffel-booking/design.md), [M1 verification](../hotels-m1-duffel-booking/verification.md), [current-state](../../../docs/architecture/current-state.md), [концепт/roadmap](../../../docs/superpowers/specs/2026-05-03-travel-platform-concept.md), root/module AGENTS; ADR0001/0006/0007/0008/0013/0014/0023/0024. [Supplier research](research.md) сохраняет официальные источники, альтернативы и границы выводов. Dependency [advisories](../../../docs/operations/2026-10-16-dependency-advisories.md) и [braces backport](../../../docs/operations/2026-10-16-braces-security-backport.md) не пересматриваются.

## Goals / Non-Goals

**Согласованный результат:** два настоящих source adapters, проверяемых без supplier traffic; параллельный поиск; безопасная общая карточка отеля; честное сравнение; явный выбор предложения; свежие условия и покупка у выбранного источника; private result/reload и сохранение Unknown без повторной покупки.

Границы: один номер/1–2 взрослых, будущие local stay dates в существующих admission bounds; London/Paris/Rome с существующим disclosed radius и отдельной fictional destination; небольшой module-owned каталог. Первый slice — одна fictional matched пара и Hotelbeds happy path; следующие — все негативные случаи и bounded curated набор. Полный каталог, fuzzy/ML auto-merge, дети/multi-room, customer payments, FX, наценки, автоматическая отмена/возврат, SSE поиска и третий source не входят.

## Decisions

### D1. Исходники и настоящий доступ — разные этапы

Разработать оригинальный Hotelbeds adapter по публичным endpoint docs, проверять собственными fictional HTTP fixtures. Профиль default-off/Test-only, Production исключён. Только отдельная явная fictional конфигурация подменяет HTTP transport; production code не возвращает fake при ошибке внешнего источника.

Для настоящего Duffel M2 остаётся вопрос применимости metasearch ограничения; историческое разрешение сравнения Flights не переносится на Hotels. Hotelbeds явно запрещает mapping в Test/evaluation/preproduction. Ни отсутствие хостинга, ни локальный запуск с test token не снимают условий использования API. Настоящая агрегация не активируется до применимого доступа/прав обоих источников и отдельного разрешения владельца. Не требуем этих прав как предварительного условия написания собственного автономного кода. Гарантия отсутствия любых претензий исследованием не устанавливается.

Конкретный guard: `Hotels:Aggregation:Enabled=false`, `Hotels:Aggregation:ExternalUseApproved=false` по умолчанию. При включённом M2 actual transport любого source требует явно заданного ExternalUseApproved и документированного account/environment/use-scope approval record для пары; иначе preflight обоих источников — Unsupported/RightsNotCleared/attempted=false,0 supplier dispatch. Флаг фиксирует ручное решение, сам юридических прав не создаёт. Fictional profile допускает только owned transport/content и не может молча открыть actual client. Выключенный M2 оставляет прежний isolated M1 scope. Guard применяется к новым search/rates/quote и повторно к **новому purchase admission**; подготовленная ранее quote не обходит отзыв доступа. Replay/read/receive/drain уже admitted attempt не становятся новой покупкой и не требуют нового admission approval. Не нужен общий permissions framework или автоматическое чтение договоров.

### D2. Hotelbeds: точный bounded subset

Документированный путь: Availability → CheckRate, когда rate требует RECHECK или нужны полные material conditions → один Booking. BOOKABLE сам по себе не доказывает полноту налогов/условий. Content enrichment — только из bounded versioned module-owned fictional каталога для автономных tests; настоящий content import/refresh потребует отдельного доступа и разрешения. Нет нового глобального importer/cache service.

Профиль первого M2: Hotelbeds **Net**, `AT_WEB`, один номер/1–2 взрослых, без packaging/qualified/member offers, карт/PSP/платёжного расписания, с установленными обязательствами. `hotelMandatory=true`, commissionable/retail pricing, `AT_HOTEL`, неизвестный pricing profile, неизвестные условия/доплаты и дополнительные обязательные private inputs дают явный Unsupported purchase; не подставлять паспорт, nationality, возраст30 или IP по умолчанию. Search может показать такой тариф с причиной недоступности покупки. Net не объявляется розничной ценой путешественника.

`rateKey` — opaque adapter context, не разбирается и не используется как публичный selection key/RateId. До quote он находится только в server-only части конечного snapshot entry (D4); после Prepare минимальный durable context защищается шифрованием и имеет версию. `clientReference` — correlation, не доказанный supplier idempotency key. Booking effect только в Create; Prepare не покупает. Known booking reference допускает обычный optional reader; потерянный ответ без reference → Unknown/manual, без BookingList discovery или resend. Новый known-operation framework не нужен.

Transport следует текущей [mTLS policy](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/mutual-authentication/): Test endpoint `https://api-mtls.test.hotelbeds.com`, API key + SHA256 signature + client certificate. Options/config validation, redaction и certificate selection — Infrastructure. Выпуск/чтение настоящих ключей/сертификатов сейчас не выполняются. No redirects/cookies/body logging; нет глобальных retries/hedging. Fictional transport проверяет сформированный protocol; не доказывает supplier TLS handshake, account entitlement или certification.

Wire source: [workflow](https://developer.hotelbeds.com/documentation/hotels/booking-api/workflow/), [reference](https://developer.hotelbeds.com/documentation/hotels/booking-api/api-reference/), official [Postman collection](https://www.postman.com/clientapi/apitude-by-hotelbeds/documentation/h58zuu9/bookingapi). На этапе реализации основной агент также проверил полный публичный machine-readable OpenAPI; URL/hash/date, потребляемый subset и publisher inconsistencies зафиксированы в [verification.md](verification.md). Пробелы в required/nullability/semantics не заменяются придуманными DTO. Если обязательный field/protocol нельзя установить публично — остановить соответствующий шаг и предъявить конкретный пробел. В частности,400 validation не считается доказательством Hotelbeds no-creation.

### D3. Параллельный поиск, пределы и исходы

Два source executions стартуют независимо после offline validation/support/config guard. Один конечный response; без фонового догружания или SSE. Согласованные начальные local budgets (это защитные bounds, **не supplier quota/сертифицированная ёмкость**):

| Работа | Начальный предел |
|---|---|
| Search/Rates/Prepare одного source | 15s на всю execution, включая все её read HTTP steps, decode/normalization; каждый следующий step получает оставшийся срок, без reset/retry |
| Aggregate search/rates | 17s от admission, включая merge; child cancellation при deadline |
| Module foreground aggregate admission | 2 одновременных запроса, без очереди |
| Каждый из двух source foreground budgets | 1 active HTTP operation, 24 starts/min; отдельные counters |
| Каждый source recovery | 1 active, 6 starts/min отдельно от foreground; M1 allowance3 reads/5min сохранён |
| Create HTTP | Duffel существующий140s; Hotelbeds90s (официальная рекомендация минимум60s), без retry |
| Application sender / сохранение результата | Существующий application cancellation budget145s, отдельно completion-store budget10s; это не HTTP timeout и не обещание end-to-end SLA. Browser abort не обрывает admitted sender |
| Материализация одного source | 2MiB raw body, ≤100 properties/≤200 rate variants |
| Aggregate | ≤100 property groups/≤200 leaf variants; rates snapshot ≤200 offers; ≤2MiB целого serialized entry (public response + server-only context) |
| Search/rates cache вместе | 128 whole entries, 16MiB UTF-8 accounted key+public+private payload; TTL≤300s и не позже самой ранней source validity |
| Mapping/catalog | ≤100 reviewed pairs/≤200 source properties; конечные versioned records, без автоматического расширения |

Foreground source budget включает Search/Rates/Prepare/Create. Координатор search/rates держит только module permit; каждый read HTTP step берёт/освобождает source permit внутри adapter. Следующий step не сбрасывает15s execution deadline. Для Create source permit берёт application **до durable claim**, adapter повторно не захватывает его; для recovery permit держит observation runner, reader не захватывает его второй раз. Неиспользованный из-за проигранного claim допуск всё равно может расходовать local starts budget — это не счётчик оплаченных supplier calls. Короткий Search не получает90s booking timeout. Recovery counters действительно маршрутизируются по provider. Перегрузка не блокирует private GET.

Отмена/deadline запрещают новый search/rates result и отменяют дочерние reads; permit остаётся занят до фактического завершения underlying operation. Возврат ответа/таймаут не освобождает слот работающего child. Невыполняющий cancellation transport не создаёт бесконечные detached tasks или новый допуск после потери владельца lease. Тесты отдельно доказывают release ровно один раз после завершения child. Для admitted Create сохраняются M1 spent claim и bounded observation/manual review; истечение любого cancellation budget не означает no-creation или разрешение на повтор.

Каждый response содержит оба запланированных source status: `kind` (существующие Unsupported/Complete/Limited/Failed), фиксированный `reason`, `attempted`. Unsupported/Disabled/Unconfigured/RightsNotCleared — skipped; capacity refusal — Failed/LocalCapacity/attempted=false; supplier timeout/network/decode/auth/rate-limit — Failed с разными reason/attempted=true. Complete0 — успешный пустой; Limited0 — неполный пустой. Caller cancellation закрывает interaction без пригодного snapshot.

Aggregate truth table, независимо от items.Count: оба Complete и нет локального ограничения → Complete, включая0; любой Limited → Limited; хотя бы один Complete и другой skipped/failed → Limited, включая0; ни Complete/Limited и есть Failed → Failed; все Unsupported/skipped → Unsupported. Byte/count truncation даёт Limited с причиной. Complete означает полноту только в заявленном curated scope.

Полностью валидный bounded source body декодируется до использования. Oversized/malformed body не режется посреди JSON: source Failed/ResponseTooLarge либо Decode. Для валидного ответа с item cap сначала canonical stable sort, затем bounded prefix с Limited; опоздавший source не изменяет frozen snapshot. Истёкшие варианты исключаются до freeze с Limited/Expired; невозможно выпустить уже просроченный snapshot. Aggregate public+private byte overflow удаляет whole ranked groups и связанные server-only contexts с Limited, никогда не отрезает statuses/conditions; если даже минимальный status envelope не помещается — fixed error без snapshot. Ни одного silent truncation accepted terms.

### D4. API, snapshot и UI

Сохранить существующие routes. Search response расширяется `schemaVersion=2`, `groups`, полным набором leaf variants, source statuses, limits и `rankingPolicyVersion`. Старый `result.items` остаётся набором source-specific leaves, не «лучшим тарифом отеля». Public selection key — случайный выданный локально token; внутри snapshot он разрешает exact provider/account/environment/source property/room/rate и scope. Supplier rateKey/credentials не выходят в API.

Cache entry состоит из public projection и server-only sidecar: issued token → normalized opaque selection handle для конкретного provider/account/environment/profile. Wire DTO в cache не сохраняется. Hotelbeds handle содержит точный текущий rateKey; его предел4096 UTF-8 bytes, превышение изолирует offer как Limited/ContextTooLarge без обрезания key. Public RateId — безопасная source-specific alias/fingerprint, а не rateKey. Adapter получает handle при LoadRates/Prepare через существующую capability boundary; Application не разбирает его значение. Sidecar входит в те же2MiB/16MiB accounting/TTL и whole-entry eviction, отдельных неограниченных dictionaries нет. HTTP projection никогда не сериализует sidecar. Если handle утрачен, требуется новый поиск; admitted reservation использует свой protected durable context и от cache не зависит.

`POST /api/hotels/rates` сохраняет `(searchId, selectionKey)` для одного leaf; добавляет альтернативный `(searchId, propertyGroupKey)` для максимум двух source variants проверенной группы. Ровно один selector; пустой/оба/невыданный selector → validation error без supplier access. Group request проходит D3 и возвращает неизменный rates snapshot со всеми leaf offers/statuses. Один skipped leaf не становится empty success группы. Существующий `PUT /api/hotels/reservations/{id}/quote` продолжает брать `(ratesId, selectionKey)` конкретного rate; group ID никогда не purchase key. In-memory snapshots не переживают restart; сохранённые reservation от cache независимы.

API/TS decoder/OpenAPI изменяются вместе; поля с unknown не заполняются ложными defaults. Два источника не могут столкнуться одинаковым rate/property ID. Search ID + snapshot version + criteria/UI epoch защищают от старого ответа; смена dates/guests/destination, logout или permission loss очищают selection/consent/private guest state. Истечение/eviction требует свежего поиска. Source account/config/mapping revision проверяются перед quote; admitted purchase не переписывается.

Общая карточка показывает проверенную группировку и отдельные предложения/источники. Главные факты берутся из стабильного source reference карточки (ordinal provider/account/environment/property key), с атрибуцией; конфликтующие адреса/имена видны, а не смешиваются в «улучшенный» отель. Непроверенные похожие отели остаются отдельными. UI показывает «не опрошен», «нет вариантов», «частичный поиск», «не удалось получить» с понятными причинами, отдельно от недоступности покупки тарифа. Рейтинг, фото/завтрак одного источника не приписываются другому.

### D5. Четыре идентичности и проверенное сопоставление

| Сущность | Идентичность/назначение |
|---|---|
| Physical property | canonical group ID только для проверенного физического отеля; не booking authority |
| Source property | provider + account boundary + environment + source property ID |
| Room / rate | source-specific room identity; rate identity плюс board/payment/eligibility/cancellation и scope; похожее имя не equivalence |
| Offer | source-bound rate snapshot для точных dates/occupancy/money/validity; ephemeral local selection token |

Первый slice использует reviewed versioned crosswalk с ID обеих сторон, независимыми address/coordinate/property evidence, reviewer/reason, checkedAt/validUntil и permitted-use scope. Для fictional data права принадлежат проекту; это не evidence настоящего matching permission. Matching не извлекается из supplier text или public name. Один name, proximity, stars, либо одинаковый source ID в разных namespaces не дают auto-merge.

Trusted pair соединяется только при непросроченном непротиворечивом evidence для точных namespaces. Неизвестный/candidate/ambiguous/conflicting/expired → отдельные карточки с фиксированным статусом. Никакой транзитивной склейки конфликтующих цепочек; конфигурация с duplicate/conflicting membership отклоняется целиком, affected entries остаются раздельными. Mapping change инвалидирует незавершённые selections/quotes, но не saved accepted facts и не admitted attempt.

Дедупликация property cards не дедуплицирует cross-source offers. Внутри source можно схлопнуть только точный duplicate identity с идентичными нормализованными money/material facts. Совпавший key с разными фактами — Conflict, без выбора самого дешёвого; искажение source изолируется как Limited с причиной, затронутый вариант не selectable. Room-equivalence model/admin mapping UI/new persistence service не нужны.

### D6. Деньги и условия без выдуманных значений

Нормализовать money как exact decimal string + ISO currency + whole-stay/room/guest scope + источник и pricing basis. Сохранить отдельные `supplierPayable`, optional `travellerPrice`, included breakdown, known/unknown mandatory local obligations и completeness. Existing `StayTerms.Total` остаётся авторитетной суммой supplier debit для consent/actual matching; не заменяется ценой для ranking. Net HBX отображается «Сумма поставщику за всё проживание», retail неизвестен: «Цена продажи путешественнику не определена». Не вводится собственная наценка и не обещается конечная цена клиенту.

HBX Net использует documented net/totalNet в правильном scope; commission не вычитается повторно. `sellingRate`/hotelMandatory и commissionable profile не угадываются из присутствия поля; неизвестный profile не пригоден покупке. [Pricing models](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/pricing-models/) разделяют эти значения. Duffel existing total semantics сохраняются; добавлять ему travellerPrice можно только по прямо документированному полю/смыслу, не по сравнению sample numbers. Public display conversion source `clientAmount` не меняет actual local currency.

В автономном срезе сравниваем **сумму поставщику + известные отдельные обязательные доплаты** при доказанно одинаковом price basis, dates и occupancy. Это стоимость обязательств выбранного source profile, не выдуманная retail цена. Когда basis не доказан — значения показываются отдельно без «дешевле». Included tax уже в total не прибавляется; net200 + included20 остаётся200; отдельный EUR20 при GBP200 остаётся двумя суммами. Missing tax feature/пустое поле не доказывает ноль. HBX tax breakdown может требовать account activation; без достаточного evidence local obligations Unknown и purchase blocked.

Structured cancellation содержит тип, суммы/валюты/временные интервалы со смещением, применимость и source evidence; board — вид и известный guest/night scope. Display strings не парсятся назад. Missing policy отличается от explicit non-refundable; timezone не берётся из браузера, unknown coverage не обещает завтрак всем. Во время Prepare обновляются money/eligibility/полные conditions; material change при той же цене требует consent. Неизвестные существенные обязательства не допускаются к покупке. Из отменяемости не следует доступная cancellation action или customer payout.

### D7. Детерминированный порядок `hotel-cost-first-v1`

Область сравнения — одна search request; ranking не провозглашает равенство room/rate. Полностью известные obligations одной currency и доказанного pricing basis образуют comparable bucket. Supplier cost и traveller price не попадают в один bucket. Mixed-currency/unknown obligations/basis — отдельные помеченные incomparable sections. Currency sections идут ordinal ISO code для навигации без глобального победителя; known supplier-cost section до unknown — полнота данных, не обещание более низкой цены.

В comparable bucket total = whole-stay supplierPayable + только отдельные обязательные суммы той же валюты. Порядок: total ascending, затем stable ordinal source semantic identity `(provider, account-bound opaque public alias, environment, propertyId, roomId, rateId, normalized material fingerprint)`. Fingerprint строится из canonical normalized facts, не из случайного token, прибытия ответа, host culture, сырого текста/PII или текущего времени. В incomparable sections — stable identity, без скрытого price ordering. Не использовать commission, brand, bookability, stars/review scales или скорость ответа как бонусы.

Если source не выдаёт самостоятельный stable room/rate ID, semantic identity использует documented room code и canonical fingerprint normalized rate facts. Нельзя разбирать opaque rateKey или использовать его меняющуюся search-specific часть как преимущество/устойчивый rate ID; минимальный protected context связывает semantic leaf с точным текущим rateKey.

Для group card минимум рассчитывается отдельно в каждом comparable bucket; group с EUR и GBP не получает одну «цену от». **Одна physical group — одна карточка.** Primary bucket карточки — ordinal minimum `(pricingBasis,currency)` среди её comparable buckets; если их нет — отдельная incomparable section. Секции идут по stable primary-bucket key; внутри секции — минимум именно primary bucket, затем stable group identity. Все прочие scoped minima и offers остаются на той же карточке. Выбор primary bucket — правило навигации, не утверждение о выгоде валют и не глобальный winner; разные primary sections не сравниваются экономически. Filtered-out/unsupported purchase status не скрывается.

Каждое объяснение сообщает факты и предел: «210 GBP за всё проживание, включая известные20 GBP доплаты», «ниже цена среди сопоставимых вариантов в GBP», «нет данных о доплате — общую сумму сравнить нельзя», «одинаковая цена; технический порядок». Board/cancellation сравниваются видимыми фактами, без произвольного денежного веса; нет обещания «лучший» при разных условиях. Изменение comparator требует policy version и обновления golden scenarios.

### D8. Покупка: источник — часть согласия

И selected offer, и quote/consent, protected context, attempt, source account/environment связаны между собой. Пользователь может до admission явно выбрать другой source, получить новую quote revision и повторно согласиться, даже при той же цене/отеле. Агрегатор никогда не выбирает «следующего доступного» при Book, timeout, rate expiry, provider disablement или более дешёвом ответе.

У M2 quote сохраняется normalized **AdmissionBinding v2** в существующем StateJson, вне accepted StayTerms и opaque supplier DTO: execution profile (Fictional/External), provider/account/environment, relevant config/pricing/approval revision, referenced mapping pair/revision и validity. Quote ValidUntil ограничен также этим validity. Config/transport/catalog profile фиксируется при composition процесса; hot reload не добавляется. Перед новым admission Application повторно сверяет binding с текущим profile/approval и source namespace, а после preparation/protection/capacity — свежий срок непосредственно перед claim. Изменение/отсутствие M2 binding, отзыв внешнего допуска, смена аккаунта или Fictional→External требуют refresh/refusal с0 create/0 claims. Нельзя искать этот binding в уже истёкшем cache или разбирать provider context в Application. Legacy persisted M1 без v2 распознаётся только как прежний Duffel/context-v1 scope и обслуживается по M1; отсутствие binding у новой M2 quote не является legacy fallback.

Эта проверка относится к неотправленной покупке. Сначала выполняется replay уже сохранённой attempt без нового admission/permit. После admission исходный transport/account binding не заменяется текущей конфигурацией: reader при несовпадении binding даёт ObservationUnavailable/review с0 HTTP, не обращается в другой account/environment. Private GET остаётся local. Для допустимого сохранённого binding отключение writer/новой агрегации не стирает existing receive/read/drain.

Текущий source binding предоставляется Application/runner как normalized Hotels descriptor через capability boundary с реальными потребителями Duffel/HBX. Application не читает Infrastructure options, certificate data или supplier DTO; новый Shared registry не нужен. Дополнительный admission metadata сохраняется существующим StateJson и явно исключается из сравнения legacy accepted terms.

Сохранить capacity-before-claim, atomic admission, одну bounded Create, replay без нового permit и бессрочный до разрешения uncertainty spent barrier. Browser abort не разрешает resend. После Unknown нельзя купить у другого поставщика как продолжение той же покупки; UI объясняет риск двух броней и не предлагает кнопку автоматической замены. Отдельная независимая новая покупка не создаётся системой из ошибки; принятые M1 правила deliberate new resource после положительного no-creation сохраняются.

Одна purchase flow сохраняет один reservation ID, включая выбор другого source до admission. После uncertain результата UI не выпускает новый resource/action ID как способ продолжить этот flow: ведёт к retained private result. Барьер относится к этой покупке, а не к глобальному запрету любых независимых броней похожего отеля; не заявляем дедупликацию намерений разных ресурсов/вкладок по name/dates/PII и не добавляем глобальный booking-intent registry.

Hotelbeds confirmed facts сравниваются с exact accepted source property и semantic rate/scope/money, а не canonical card или буквальным равенством stage identifiers/rateKey. Идентификаторы Availability/CheckRate/Booking могут различаться при доказанной correlation; это не повод придумывать actual facts. Late/mismatched facts сохраняют positive reservation и review-required, без автоматического исправления покупкой/отменой. Details read только по известному attributable booking reference, прежние3 reads/5min, durable allowance не reset при restart. ReaderDisable/writerDisable не стирают attempts и не останавливают уже нужное observation при сохранённом допустимом binding; общий Duffel route/ownership/inboxes неизменны. Для HBX новый webhook не требуется.

### D9. Приватность, совместимость, размещение

Не расширять guest schema сверх уже нужных names/lead contacts/adult confirmations. Authorization до decrypt/effects; minimal protected context/party, no-store, никаких PII/tokens/rateKey/certificates в logs/URLs/browser storage/SSR. Owner GET/list остаются local/key-independent metadata без supplier calls. Конечная lifetime search cache не переносится на unresolved purchase. Existing retention/tombstone/barrier semantics сохранены.

Добавленные comparison metadata не переинтерпретируют старые M1 JSON accepted terms. Для absent legacy fields — Unknown/Legacy display, не выдуманные defaults, не изменение consent equality или accepted-to-actual match. Старые Duffel context version и recovery читаются; новый HBX context имеет собственную версию. New normalized facts хранятся в существующих JSON snapshots/terms; reviewed map — module config/content. Новая relational schema/migration по этому проекту не ожидается; если обнаружена необходимость, это явный отдельный gate, не hidden workaround.

Core/Application: normalized identity, comparison, ranking и source-bound state; Infrastructure: DTO/client/auth/certificates/content mapping/protocol/errors; Api: validation/contracts/composition; Angular: presentation/epochs. Только Hotels `Api.Composition` виден Host. Общие примитивы reuse при совпадении смысла, Flights internals не импортируются. Новый Shared ranking/provider registry не нужен. TimeProvider и ErrorOr остаются обязательными.

## Risks / Trade-offs

- Автономные tests доказывают source protocol against documented fixtures, не доступ, лицензию, supplier mTLS/booking или реальное покрытие. До настоящего запуска права и account profile проверяются заново.
- Малый reviewed каталог и консервативный matching оставят похожие отели отдельными; ложное совпадение опаснее пропущенной группировки. Supplier content не коммитится как public fixture.
- Net profile не определяет traveller retail price. Если позже нужна продажная цена/markup/commissionable flow, это отдельное изменение pricing/consent, а не default0.
- 15s read budgets и отсутствие retry могут уменьшить inventory; Limited/reasons сохраняются. Начальные bounds не претендуют на доказанную производительность.
- No-reference Unknown требует manual review; это осознанная цена защиты от двойной покупки. Не вводить lookup/resend без документированного безопасного контракта и нового решения.

## Migration Plan

1. Proposal/design/specs/tasks и review завершены, спецификация утверждена владельцем2026-10-19. [research.md](research.md) — provenance, не конкурирующий plan. Product/dependencies не менялись.
2. Отдельно разрешённый sync M1 выполнен2026-10-19: три main specs содержат все33 requirements/75 scenarios M1; исходные delta/verification/tasks и точный failed memory результат16/17 сохранены. M2 deltas сверены с main. M1 не архивирован; M2 в main не применён. При последующем дрейфе baseline reconcile по фактам без отката ветки.
3. Отдельное поручение выполнено: implementation агентом `gpt-6.1-sol`, затем личный review основным агентом, source checks и fictional browser. Спецификация уже утверждена; повторно согласовывать неизменённые решения и sync не требуется. Relational/ordinary Host/privacy tasks6.1–6.3, CI/load/migrations/publication остаются отдельными gates.
4. Отдельно возможна настоящая supplier acceptance: доступ/terms/approval обоих источников, точный Net/account/content/tax profile, quotas/costs/certificates и разрешённые environments. Без этого внешнее сравнение выключено. Это не задачи source acceptance и не разрешение сейчас обращаться к providers.
5. Rollback — writer-disable без потери protected accepted state, barriers, receiving/read/drain. Source-ready/ordinary CI/fictional E2E/supplier acceptance/target rollout фиксируются отдельно. M1 memory failure остаётся failure; никакого наследованного waiver M2.

## Open Questions

Для source-only плана нет оставленных исполнителю неразрешённых бизнес-выборов. **Владелец2026-10-19 утвердил всю спецификацию**, включая Net/supplier-cost без розничной наценки, reviewed mapping, comparator, начальные budgets и уточнения саморевью. Согласованный перенос M1/reconciliation выполнен. Tasks остаются будущей реализацией; отдельное поручение на её начало ещё не дано.

**Только для будущего внешнего этапа:** индивидуальные права Duffel/HBX, permitted mapping environment, account Net/tax/settlement profile, реальные лимиты/стоимость/сертификация. Публичное исследование их не устанавливает; они не маскируются «настроить ключи и запустить».

# Design: Trips M2 Moscow itinerary candidates

## Context

Мотивация — [proposal](proposal.md), проверенный baseline и gaps — [research](research.md),новое исследование — [data-source-research](data-source-research.md). Все решения ниже, кроме Москвы и общего пользовательского сценария, — предложения автора в рамках «остальное сам реши». План v1согласован21Oct2026. После исследования пользователь выбрал пересмотр под offlineOSM/modelled estimates и уточнил demo цель. Ниже принятый v2 walking design с добавочным Rail consumer (§9). Walking source implementation, v2 adaptation и 17 задач уже имеют исторические local receipts на e654c769; текущие source/dependency доказательства после dev a8721eff ещё требуются. Новые Rail tasks не реализованы.

M1 хранит защищённый строгий full snapshot с owner/CAS; его DTO и сериализация не имеют source/fact fields. UI уже защищает dirty edits и поздние ответы. M2 проектируется как отдельная default-off capability над этой основой. Один change содержит весь planning corpus; дублирующего плана в docs/superpowers нет.

## Goals / Non-Goals

**Goals:** первый содержательный предложенный маршрут по реальным местам Москвы; объяснимый подбор по пожеланиям; честные источники/неизвестные; отдельный preview, правки и явное сохранение без потери M1 данных.

**Non-Goals:** runtime router/map service,interactive map,новыйDB,LLM,полевые измерения как demo prerequisite,гарантированная выполнимость, precise arrival timetable, билеты/резервирование, полный бюджет, weather/live closures, доступность для ограничений мобильности, transfers от origin/hotel, дети, несколько городов, regeneration поверх непустого календаря, pin/merge, постоянный trusted POI provenance в плане, M3 text parser, общий AI engine.

## Decisions

### 1. Первый полезный срез и альтернативы

Выбрано автором: центр Москвы, наружный осмотр/прогулки, 1–3 включительных календарных дня, 1–9 взрослых, даты от сегодняшнего Moscow local date до +30 дней включительно. Конечная дата тоже внутри горизонта. Существующие другие города, исторические даты и4–14days остаются валидными для ручного M1, но generation возвращает unsupported. Название «центр» не геокодируется: coverage — явный allowlist POI IDs с описанием района в versioned manifest.

| Подход | Польза | Цена/ограничение | Решение |
|---|---|---|---|
| Wikidata identity + offlineOSM pedestrian graph + bounded ranking | Реальная география,воспроизводимые modelled transitions,нет runtimeAPI/LLM | Один offline helper,ODbL provenance,явные assumptions | Выбранный demoM2v2 |
| AI ранжирует те же разрешённые IDs | Может лучше объяснять выбор/пожелания | Нужны отдельные payload/deadline/cost/model/quality gates; не создаёт отсутствующие факты | После измерения проблемы deterministic baseline, отдельное изменение |
| Live POI/routing providers + AI | Широкое покрытие и свежесть | Права, цена, SLA, runtime credentials, новый ACL consumer | Позднее |
| Шаблон с вымышленными местами | Дешёвый demo | Не выполняет real-city цель | Только явно вымышленные structural tests |

Travel.AI не изменяется. Модель/provider/profile не выбирается, потому что у этого M2 нет model consumer; это осознанный выбор способа генерации, не забытый вопрос. Если позже возникнет AI consumer, нормализованный brief + exact revision + минимальные разрешённые fact IDs будут отдельным versioned leaf contract без Host DB/tokens/passenger PII/mutation tools. MAF/SK не prerequisite.

### 2. Данные v2: один bounded offline build для демо

User выбрал пересмотр после [источникового исследования](data-source-research.md):достаточных разрешённых свежих measured walking ranges не найдено. Источник переходов теперь offlineOSM graph;минуты — явно uncalibrated математические сценарии,не LLM и не фактические наблюдения. Чужие routing-provider minutes/карты не заимствуются.

Scope:Moscow centre,18–48real POI identities,60–256directed derived walking edges,1–3days. Runtime Trips читает immutable JSON;не содержит OSM parser,graph/router/tiles,sourceAPI,refreshjob или новых schema/services. Подготовка — явная одноразовая build-time команда.

#### 2.1. Источники и offline helper

Identity/location — Wikidata CC0 entity+exact revision+coordinates,оригинальные titles/tags/dwell. Первый lookup18records дал3missing и1безcoordinates;это кандидаты,не accepted18POI. Проверить canonical redirects,географию,однозначность item/station/organization и coordinates вbbox. Не копировать фотографии/Wikipedia prose.

Topology — [BBBike Moscow XMLgzip](https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz) и [CHECKSUM](https://download.bbbike.org/osm/bbbike/Moscow/CHECKSUM.txt);listing показывает155MB. Сам helper сети не имеет:читает заранее скачанный input. Записать exact compressed SHA256,URL,Last-Modified того же download response,downloadedAt. Provider checksum дополнительный;не заменяет SHA256. Export/publication не объявлять survey/датой каждого OSMобъекта.

tools/trips/build-moscow.cs — один .NET10file-based helper(dotnet run --file ... -- args),BCL GZipStream/XmlReader/PriorityQueue/JSON/SHA256. Никаких NuGet parser packages,Java,GraphHopper/OSRM,Pythonruntime или generic SDK. Если bounded implementation не работает в caps,вернуть measured blocker/options,не строить routing platform.

Входы:локальный .osm/.osm.gz,POI inventory,QID/revision/coordinates/tags/dwell/anchor selections,input-manifest,frozen buildAsOfUtc/datasetId/version,output directory. Caps:gzip≤200MiB,expanded≤2GiB,temporary raw-coordinate index≤750000records,retained eligible pedestrian graph≤100000nodes/50000ways,build≤5min. Offline process budget≤1GiB includes parser/index/graph/routing/temporary copies;sampled WorkingSet+recorded peak reject before release,not an OS-hard ceiling. Optional offline operator budgets may only tighten these ceilings via --max-wall-ms/--max-memory-mib;they do not change hashed data. Profile-irrelevant tags are streamed/skipped;all relevant deny/conditional keys retained. Raw cap applies during indexing,graph cap during edge admission;unreferenced source nodes removed before routing. Initial100kgraph cap remains;separate raw-stage cap adopted after actual597838rawbboxnodes vs71507eligiblegraphnodes and independent osm-input-cap-assessment.md. Measured first build20POI/90paths/13915ms/218087424peakbytes is preliminary source-prep evidence,not D_demo/Q_demo/runtime acceptance. XmlReader запрещает DTD/entities,XmlResolver=null;corrupt/unsupported/oversize input fail без частичного release. Contributor names/uids/personal metadata не выходят в bundle.

#### 2.2. Фиксированный pedestrian profile и пары

BBox:[55.72,37.57]–[55.78,37.66]. Сохранять segment только если оба mapped nodes внутриbbox и допустимы;не сшивать gaps/границы.

| Rule | Profile-v1 |
|---|---|
|Way highway|footway,pedestrian,path,steps базово;residential,living_street,service,tertiary,unclassified,cycleway,track только при foot=yes/designated/permissive ИЛИ sidewalk=yes/both/left/right;прочее исключено|
|Access/foot|absent или yes/designated/permissive разрешены моделью;no/private/customers/destination/agricultural/forestry и неподдержанные непустые значения исключены;deny/unknown wins даже при другом allow|
|Unsupported tags|construction/proposed,area=yes,access:conditional,foot:conditional,oneway:foot:conditional,foot:forward/backward исключают соответствующий way/node;общего condition parser нет|
|Node barrier|absent/bollard допустимы после access checks;gate/lift_gate/kissing_gate/cycle_barrier/stile только при explicit allowed foot;остальныеbarriers запрещены;edge через запрещённыйendpoint не создаётся|
|Foot direction|oneway:foot absent/no/0 → оба;yes/1/true → nd order;'-1' → reverse;прочее/conditional → reject. Car oneway наfoot не переносится|
|Crossing|crossing=no исключаетnode;count ниже учитывает только eligible highway=crossing nodes|

crossingCount=число DISTINCT path nodeIDs включая endpoints с highway=crossing. Тег footway=crossing наway сам ничего не добавляет;node+wayrepresentation не удваивается. Missing crossingtags не превращаются в измеренную нулевую задержку. Tinyfixtures:node+way duplicate,endpoints,crossing=no,barrier default,deny-vs-allow,все direction/conditional branches. Profile — conservative mapped eligibility,не обещание текущего доступа/безопасности.

Anchor — выбранный mapped pedestrian node+OSMID+distance from sourcePOIcoordinate. Nearest candidate только≤50m,помечен approximate;нетvalidanchor→POI excluded. Straightline proximity — только bound выбораanchor,не walk segment через здание/реку. Никаких entrance/accessibility/видимости фасада claims. Связность/недостаток остаются explicit partial/empty;не придумыватьanchors/edges.

Mapped segment length — spherical haversine(radius6371008.8m),round to integer mm MidpointAwayFromZero,min1mm для coincident mappednodes;checked integer path sum. Dijkstra использует stable numeric node/way traversal/PQ ties;равныйdistance не перезаписывает выбранныйpredecessor. Это model geometry length,не прямая POI→POI и не measurement.

PairSelector-v1:sourcePOIordinal order;вычислить reachable directed targets,отбросить self/invalidanchor/profilefailure/modelupper>120;sort(pathLengthMillimetres,targetPOIIDordinal),взять≤5outgoing/source. Union окончателен:18fully connectedanchors→90из306пар;48→≤240≤256. Reverse отдельно вычисляется;shortfall до60не дополняется. После отбора обязательны≥60edges и реальныйcoverage/Q_demo proof,иначе fail. Shuffled-inputfixture сравнивает selected pairs/paths(не hash изменившихся inputbytes).

#### 2.3. Явно модельные минуты

Model-v1:lower=max(1,ceil(pathMetres/75));upper=max(lower,ceil(pathMetres/45)+crossingCount). Реализация целочисленно от mm при rates75000/45000mm/min. Скорости75/45m/min и1min/mappedcrossing — сценарные допущения автора,не observations,empirical confidence interval или гарантия upperbound. Upper>120→edge excluded. Path IDs/digest,lengthmm,crossings,profile/pairSelector/model versions,inputhash сохраняются для воспроизведения.

UI:«Оценка по карте: X–Yмин. Без полевой проверки». Показать модельную дистанцию,snapshot date,assumptions и OSMcredit. Не утверждать live traffic/weather/elevation/unmapped crossings. POIhours/access unknown без отдельного разрешённого verifiedsource;foot/access tags вgraph не доказывают POIopening. ArrivalHH:mm и monetary estimates остаютсяnull.

#### 2.4. Bytes,clock,rights и review

Immutable data/moscow-v1.json≤256KiB,manifest≤2KiB,walking-evidence.json≤4MiB. DatasetHash=SHA256exactUTF8/LFbytes withoutBOM,no embeddedselfdigest. Manifest связывает payload/evidence/attribution hashes и fixed local names,no URL/path traversal. Runtime graph/evidence не импортирует для расчёта. Любое изменениеfacts/model/rights/validity→newversion/hash;withdrawal отдельно.

Method-specific evidence:source-record/observed/modelled;walking всегдаmodelled+ODbL+modelVersion+extract/path hashes,length,crossings. ObservedAt=null,calibrationStatus=not-evaluated;retrieved/processed/publication timestamps отдельны. Dataset review:dataMode=osm-modelled-demo,preparedBy=softwareID,humanCurator=null,humanReviewer=null,humanQuality=not-evaluated. Core валидирует структуру,не создаёт подписи людей. Initialv1Core72tests/review сохранены как история; v2 adaptation task2.1 отмечена выполненной. Это не новое доказательство текущей post-integration базы.

Freshness-v1:model source-age origin=snapshotPublishedAtUtc из Last-Modified ТОГОЖЕ successful download response,связанного exactSHA. Это публикацияextract,не survey/всеOSMupdates. Publication отсутствует/invalid/future или metadata не связаныinputSHA→releasefail;fallback кdownload/buildtime нет. FrozenUTC published≤downloaded≤buildAsOf. ModelValidUntil=published+30days;runtimeinstant должен быть<expiry,конец selected Moscow-localday≤expiry. Rebuild/copy/read неизменного старогоinput не продлевает срок.

Input-manifest и frozen buildAsOfUtc/datasetID/version содержат все digest-affecting timestamps;DateTime.Now вpayload запрещён. Actual execution clock/elapsed/memory — отдельный receipt без влияниянаhash. Identical bytes+metadata+versions→identical outputhashes;старыйsnapshot при currentexecution всё равно проверяется наexpiry. Identitylease≤90days отpinnedsource retrieval;knownaccess≤7days отactualevidence,еслиесть;demoaccessunknown. ReleaseValidUntil≤min(modellease,identityleases),previewexpiry≤releaseexpiry.

HEAD feasibility21Oct2026:200,162060454bytes,Last-Modified18Oct2026 03:55:13UTC. Это НЕ downloadbinding/пакет и НЕ physicalsurvey;будущийdownload сохраняет свои metadata.

OSM-derived walks/bundle распространяются с ODbL/credit и машиночитаемыми derived data/reproduction inputs;никакого обещанного share-alike exemption. Совместить CC0identity/original editorial fields без приписывания licence privateplans. Compliance не зависит от editable note:preview/footer+historical note показывают credit/licence reference;versioned public JSON/evidence/licence offer копируется из того же canonicaldata dir в reviewed staticwebassetpath,без privatefields/dynamicAPI. Build/tests проверяют offerpath/hash;publicuse только после отдельно разрешённой публикации,локальнаяподготовка не утверждает live-offer.

D_demo=точные sources/rights/hashes,reproducible eligiblepaths/anchors/model,реальные18identities/≥60derivededges иcoverage. Download/synthetic tests не заменяют realbuildproof. Fieldcalibration/two-humanQ остаются D_live/Q_live,не fakepass и не prerequisite честного technicaldemo.

Demo source default-off/TestOnly;Production исключает его через existingguard/composition,manualM1не меняется. Никакого enablement из текущей реализации;source/demoacceptance не равна live/Productionready.

### 3. Детерминированный маршрут и неизвестные

Вход — сохранённые нормализованные TripBrief/DestinationSelection, exact accepted revision и validated package. DestinationId/IANA пакета обязаны совпасть с сохранённым destination snapshot; Moscow scope выбирается в Application/Composition, ranking Core не читает live catalogue и не содержит специальных веток названий городов. title/note/origin не отправляются источникам; origin не используется как transfer constraint. Принятый saved календарь должен быть пустым во все дни. Nonempty=409 Trips.ItineraryRequiresEmptyPlan. Dirty editor/form нельзя генерировать до явного save/discard.

Pace targets и ограничения автора:

| pace | Остановок/день | Максимум walking scenario upper sum/день | Dwell+walking scenario upper sum/день |
|---|---:|---:|---:|
| relaxed |2|60min|240min|
| balanced |3|90min|360min|
| active |4|120min|480min|

Это editorial suitability policy, не физиологическая гарантия. Дни независимы по дороге: start/end города, hotel и междневные перемещения не включены. Между соседними остановками внутри дня нужна разрешённая directed edge с допустимым modelled estimate для этого snapshot/date; разрыв не заполняется straight-line расчётом. Known closed/all-day access exclusion убирает POI при отдельном действительном source-record;в demo POIaccess обычноunknown. Unknown hours допускает остановку с явной неопределённостью; начальные arrival/end HH:mm всегда null, поэтому проверка open-at-arrival не заявляется.

Порядок: dates ascending; на каждом дне bounded beam search простых путей до target,width16,общий максимум20000расширений на весь запрос. Исключать повторы POI по всей поездке,недопустимые edges,лимиты выше и known-closed dates. Стартовые POI и расширения обходятся по stable POI ID ordinal; beam сортируется до отсечения,чтобы порядок JSON arrays не выбирал результат.

Точная оценка пути лексикографическая: (1) число РАЗЛИЧНЫХ requested supported interests,покрытых union уже выбранных дней и текущего пути; (2) больше заполненных stops текущего дня,не выше pace target; (3) меньше walking scenario upper sum; (4) ordinal последовательность POI IDs. Повтор одного architecture tag на пяти местах не считается пятью interests и не перекрывает отсутствующий nature. Food всегда unmet; при interests=[] первый score=0. При исчерпании expansion cap выбрать лучший уже найденный путь и явно search-limited partial; отсутствие global optimum не скрывается.

Проверяемая верхняя граница при текущих valid bounds:если expansion=considered seed/next-POI attempt,то48POI+beam16+target4+3days дают≤3×(48+3×16×48)=7056attempts. Глобальный20000cap сохраняется как защитный потолок,но validcurrentinput не может его исчерпать. Тест проверяет этот более строгий bound,общий счётчик и cancellation;не вводит test-onlybudgetAPI и не переименовывает edge scans вexpansions ради coverage. Search-limited branch не объявляется эмпирически проверенным;изменение bounds потребует новогоproof/policyVersion.

Один asOfUtc захватывается через TimeProvider перед проверкой supported scope и используется для horizon/freshness/outcome validation всего расчёта. Воспроизводимость относится к одинаковым normalized brief+payload hash+policyVersion+asOfUtc и тем же пределам. Разные clock/admission/deadline outcomes не выдаются за недетерминизм маршрута. У server policy есть явная policyVersion: любое изменение ranking/limits требует новой версии,source/quality receipts и инвалидирует старый preview при availability recheck.

Для дня без валидного пути остаются пустые blocks. Каждый день brief всё равно представлен. Полностью нулевой результат=empty с причинами; хотя бы одна остановка=candidate, coverage=filled только если все target slots заполнены и все выбранные interests представлены, иначе partial. Любой unknown access/hours и будущие ограничения показываются независимо от coverage: filled никогда не означает feasible/complete trip/within budget. Если origin/budget задан, always explicit exclusions transferNotPlanned/totalCostUnknown.

Генератор может показать один POI как самостоятельный day без edge, но это partial при target≥2. Нельзя прятать нехватку данных повторением POI или автоматически уменьшать target. Детерминизм сравнивает route/fact IDs/reasons при зафиксированном asOfUtc; новые UUID/request ID/generatedAt не входят в route ranking.

### 4. HTTP candidate boundary и ресурсы

Предлагаемый endpoint: POST /api/trips/{tripId}/itinerary-candidate, JSON {} (unknown properties rejected), mandatory strong If-Match с той же грамматикой M1. Он вычисляет preview, не изменяет БД/revision/timestamp и не создаёт receipt/token table. Owner principal проверяется до decrypt/data processing. TripPlanService.GetAsync — retained read consumer. Owner из body/query запрещён.

Порядок: disabled guard404 до auth/dependencies → authenticated owner → body/precondition checks → owner load404/503 → revision412 → empty-plan409 → capture asOfUtc → supported scope → package validation → admission → generation → full output validation/size → повторный owner Get и exact revision check перед ответом. Последний read не устраняет race после ответа: final M1 PUT с исходным If-Match решает CAS.

200 discriminated outcomes:
- candidate: baseRevision, destinationId, dataset identity/hash, policyVersion, generatedAt/expiresAt, days/blocks, per-stop source facts/unknowns, used edges, coverage, unmetInterests, reasons/exclusions;
- empty: та же basis без blocks, bounded причины нехватки;
- unsupported: scope reason (city/range/historical/horizon), без fake partial route.

Application result refinements(implementation seam):ItineraryCandidateContracts.cs owns a small discriminated ItineraryCandidateResult with BaseRevision and either ScopeUnsupported(saved destination,bounded scope reason,policyVersion/asOf) or Computed(Core ItineraryOutcome). ScopeUnsupported is constructed before source read and contains no fabricated dataset/hash/preparation/model facts. Core outcome currently requires provided validated facts even for its defensive date-unsupported branch;do not invent a package or move Moscow selection into Core to satisfy that shape. Independent result validation applies to computed Core results. Every success,including source-free unsupported,retains the original expected revision and repeats owner GET before response;deletion404/change412. API maps scope result to its minimal strict unsupported wire branch,withoutadoptablepreview/sourceidentity.

Actual output-order seam:one Application-owned IItineraryCandidateResponseEncoder port is implemented by the API DTO codec,as M1 already uses an Application codec port. Its input is the small App result union;Computed carries immutable ValidatedItineraryFacts only as ephemeral context for projecting referenced POI/evidence/source metadata,never the entire package on the wire. Service awaits EncodeAsync(result,ct),checks nonempty complete UTF8 bytes≤128KiB and ordinary TripPlan.Restore/ITripPlanCodec.MeasureDetailBytes(...,true),freezes owned response bytes BEFORE final owner GET/revision+expiry recheck,and returns PreparedCandidateResponse(exact bytes). Endpoint writes those bytes with application/json without reserialization. No HTTP/Api types/imports in Application,no new registry/cache/persistence/generator engine. The async encoder is cooperative;no WaitAsync/fire-and-forget that releases admission while work still runs. Admission remains one small process-wide two-slot/no-queue guard;no extra generator test port is needed. Source-free Unsupported also goes through encoder+finalownercheck without fabricated facts.

Ошибки:401/403/404 как M1,428 missing If-Match,400 malformed/unknown input,412 stale,409 nonempty,503 package/storage/protection unavailable,429 busy с Retry-After:1,504 computation deadline. No-store на всех ответах/ошибках. RFC7807 code extension через текущий TripsProblems, без body/private text.

В CandidateBlock: stable candidate POI ID; proposedBlockId UUID; original title; local date; startTime/endTime=null; estimate=null; original note содержит короткую historical reference (datasetId/version/asOf, POI ID), а не trusted badge. Fact display: type/state, bounded source label+HTTPS URL≤256chars, reviewedAt/validity и значение либо unknown reason. URL приходит только из reviewed package, без user source URL/fetch. Рендер text/link с безопасной схемой, без HTML/image/embed; внешние links с rel=noopener noreferrer,без prefetch/автоматического запроса к источнику. Fact IDs должны существовать и иметь правильный type/POI/edge scope. Duplicate/invented/expired-or-modelled-as-observed references, reversed edge, лишний block/time/price, скрытый unknown или mismatched bounds отклоняют весь candidate как unavailable; не отдавать «починенный» результат. Modelled estimate обязан иметь method/model/calibrationStatus/source-path identity;отсутствие metadata — invalid result.

Новая response schema candidateVersion=1, strict unknown/duplicate-property rejection, max depth16; numbers only bounded counts/minutes, revision/money lossless strings. Request body≤1KiB; response complete canonical UTF-8≤128KiB, максимум12blocks/3days,96referenced facts,48edges/source display entries. Dataset titles/notes и references входят в итоговый128KiB cap. Пределы не расширяют M1. До ответа codec проверяет, что предложенный обычный ReplaceTrip payload вместе с server-field reserve проходит существующий M1 full-detail limit. Oversize — typed unavailable, без truncation; M1 PUT повторяет свой precommit check после пользовательских правок.

Deadline2seconds from admission, max2active computations/process, no queue/retry/background work; expansion cap тоже обязателен. Не принимать busy call до генератора. Отключение network в тесте доказывает0external calls. Per-process limits не объявлять глобальным account ceiling/SLA; replicas здесь не имеют paid effect. Cancel/timeout завершают bounded computation, endpoint не запускает fire-and-forget.

expiresAt=min(generatedAt+15minutes, payload releaseValidUntil). Перед success response повторно проверить expiry по часам; уже истёкший preview не возвращать как успешный. Применение preview UI запрещено после expiry; часы через инъецированный clock. Известность каждого displayed fact отдельно ограничена его validity для дат поездки, а не общей expiry.

#### 4.1. Generation availability и identity пакета

Отдельный retained read consumer: GET /api/trips/itinerary-availability, без body/query/private plan fields. Он не использует M1 destinations/detail как источник generation state. Когда Trips или itinerary flag off, оба M2 routes (этот GET и candidate POST) возвращают404 до auth, DB/protection/package resolution. При enabled GET требует trusted owner principal:401/403 как M1, без чтения/дешифрования плана. Не использует Host health как readiness proof и не меняет M1 readiness.

Ready200, strict bounded JSON:
{ "candidateVersion": 1, "status": "ready", "destinationId": "moscow", "datasetId": "<reviewed-id>", "datasetVersion": "<release-version>", "datasetHash": "<sha256>", "policyVersion": "<policy-version>", "dataMode": "osm-modelled-demo", "humanQuality": "not-evaluated", "releaseValidUntil": "<UTC timestamp>" }.

Dataset ID/version и policyVersion — original ASCII identifiers1–64characters, hash —64lowercase hex, timestamp — canonical UTC. Этот GET выдаёт только identity текущего целиком validated/released package; нет facts, owner, TripId или private wishes. Полный body≤2KiB, depth≤4, unknown/duplicate fields запрещены; Cache-Control:no-store на200и errors, никакого transferCache/browser storage. Expired/missing/corrupt/withdrawn package —503 RFC7807 code=Trips.ItineraryUnavailable без старой identity. Unexpected error/timeout/transport failure не превращается в ready/empty.

UI generation availability:unknown/checking/ready/disabled404/unauthenticated401/forbidden403/unavailable503-or-read-failure. Эти states независимы от M1 availability. Feature off блокирует новые previews/adoption; unavailable оставляет ручной M1 editor работающим. Ready identity должна точно совпасть с candidate datasetId/version/hash/policyVersion/dataMode, releaseValidUntil должна быть ещё в будущем; candidate expiresAt также проверяется заново непосредственно перед переносом. Различие срока/identity, invalid response и read failure блокируют перенос и инвалидируют пригодность preview без потери editor/form content. Нет автоматической новой генерации или retry.

Одна HTTP-observation отражает текущий package данного process/target, а не глобальную транзакцию со всеми репликами: mixed versions могут дать stale/unavailable и требуют согласованного package rollout. Отзыв/feature-off после последнего успешного check может ещё race с локальным переносом; такая гонка явно остаётся best-effort. Перенос/последующий PUT сохраняют только пользовательские намерения, не право использовать факты как актуальные или обойти rollback.

#### 4.2. Встраивание в существующий HTTP pipeline

Проверенный M1 guard сейчас валидирует каждый POST как create с title/brief,а любой GET ведёт в ITripsReadiness; response policy находится до Authentication/Authorization,request guard — после них (Host Program). Поэтому добавления endpoint недостаточно: новый POST{} иначе получит400,availability вызовет schema/key readiness,а feature-off check после Authorization может дать401вместо404.

В существующей Api response policy добавить только классификацию двух M2 routes и itinerary-off404 ДО auth; no-store остаётся на всех Trips paths. Классификация согласована с router по ordinal-ignore-case и допустимому trailing slash,не является broad skip /api/trips. Host middleware order/map не переставлять. Простые M1/M2 flags регистрируются до disabled-return в Composition и доступны policy всегда;это не разрешает resolve/register package/DB/protection при feature-off.

После trusted owner проверки request guard использует явную route policy:
| Route kind | Body/precondition | Readiness/next |
|---|---|---|
| Candidate POST | bounded raw1KiB включая unknown Content-Length/chunked,UTF-8 JSON ровно{},no duplicate/extra fields,strong If-Match | Обычная Trips schema/protection readiness,затем candidate service; никогда не M1 create-body validator |
| Availability GET | no query/body/private plan/precondition | Только validated package state; не разрешать/вызывать ITripsReadiness,store,codec или protector |
| Existing M1 routes | Существующие strict create/replace/delete/list guards без ослабления | Существующий ReadyAndNext |

Дополнить TripsProblems.Status точными maps:Trips.ItineraryRequiresEmptyPlan=409,Trips.ItineraryUnavailable=503,Trips.ItineraryBusy=429,Trips.ItineraryDeadline=504; Retry-After:1 только для Busy. Existing Trips.InvalidInput=400,RevisionMismatch=412,PreconditionRequired=428 и M1 mappings сохраняются. Новый ErrorOr code не должен попасть в default400. Unknown routes/HTTP methods не используют availability bypass.

Доказательство — будущие lean HTTP tests через реальный тот же middleware порядок,не прямой вызов handler:POST{} доходит до candidate,POST/title brief остаётся M1;off+anonymous404;ready metadata при бросающем ITripsReadiness/DB/protection sentinel;candidate при unready Trips=503;case/trailing-slash/chunked boundary и все typed codes;0M2dependency resolutions when off. Сейчас pipeline и fixtures не меняются.

### 5. Preview, принятие и preservation M1

UI flow: существующая form → явное сохранение пустой основы → «Предложить прогулочный маршрут» → preview источников/ограничений → «Перенести в редактор» → правки → явное «Сохранить». До последнего PUT server plan остаётся пустым. Принять неизменённый вариант можно теми же двумя явными действиями; никакого скрытого autosave.

Candidate существует только в памяти текущей сессии/плана. Request captures owner/plan/read generation, exact saved revision, local edit version, formDirty и generator request epoch. Пока идёт генерация, ручные правки разрешены; ответ не копирует их поверх. Если любой captured guard изменился, результат discard/stale. Generate disabled при dirty, existing mutation, barrier или любом saved block. Единственный candidate request pending; двойной клик не даёт второй запрос.

Перенос в editor допускается только при clean/empty buffer, unchanged baseRevision/epochs, неистёкшем candidate и той же ready dataset identity. Действие «Перенести» обязательно перечитывает owner GET /api/trips/{id} и GET /api/trips/itinerary-availability из §4.1; они могут выполняться параллельно, но ни один не заменяет другой. Общий adoption ticket захватывает session/plan/edit/form/baseRevision/candidate request epoch; pending=checking-adoption блокирует повторный перенос. Только после успешных обеих проверок, повторной проверки ticket/clean state и часов выполняется синхронное копирование в buffer. Revision mismatch/deletion,package/policy switch/withdrawal/expiry,feature-off,401/403или failure любого read блокируют перенос; dataset mismatch помечает candidate stale,disabled/unavailable остаются отдельными состояниями. Поздний ответ или edit/logout/navigation во время любого read не меняют buffer и не разрешают adoption. Никакого hidden save/retry/generation. Изменение после последнего наблюдения ограничено best-effort правилом §4.1; final PUT всё равно проходит M1 CAS.

Перенос не меняет title/brief и создаёт обычные M1 blocks с candidate proposed UUIDs, null times/estimate и исторической note≤500scalars. Note описывает происхождение предложенного текста, но редактируема; backend никогда не использует её как trusted fact. Owner может менять title/note/порядок/дни/оценки; они становятся его намерениями. Не сохраняются скрытые structured facts/badges, не добавляются поля в v1 decoder/codec.

Принятие — обычный M1 PUT с исходной baseRevision. При stale412 local candidate-derived edits остаются для сравнения, но UI инвалидирует candidate и не переносит их автоматически на новую revision; пользователь явно сверяет/правит как обычные намерения. При lost PUT действует M1 uncertainty: GET current state, не proof lost-command commit, no retry. Поздние save replies сохраняют более новые editor/form changes.

После save/reload остаются обычные M1 planning blocks и текстовая historical reference; исчезает preview fact panel. UI не говорит, что часы/путь проверены или что user-edited text по-прежнему сгенерирован/подтверждён. Persistent authoritative provenance потребовал бы нового versioned read/write/persistence design и реального consumer — это поздняя граница, а не скрытое ограничение.

При nonempty saved plan generation недоступна; UI объясняет «Первый маршрут доступен для пустой основы». Чтобы начать заново, владелец явно создаёт отдельную основу либо вручную удаляет блоки и сохраняет; система сама не очищает/копирует/заменяет блоки. Pinning не имеет consumer в таком M2 и не вводится.

Logout/owner/plan change/delete очищает candidate и aborts request. Нет localStorage/sessionStorage/IndexedDB/SSR/service-worker private content. Empty/unsupported/partial/busy/unavailable/stale/generating/uncertain сохраняют разные UX состояния. Sources/unknowns доступны с клавиатуры; status announced, focus recovery сохраняет buffer. Никакого текста «забронировано», «поездка оплачена», «маршрут гарантирован» или «в бюджете».

### 6. Архитектурная и файловая граница

| Предлагаемый файл/место | Ответственность |
|---|---|
| Core/Itineraries/ItineraryFacts.cs | Только нормализованные immutable facts/unknowns/validity и domain factories ErrorOr |
| Core/Itineraries/ItineraryCandidate.cs, ItineraryPolicy.cs | Candidate domain shape, IDs/invariants, pace/ranking policy и bounded algorithm; TimeProvider не заменяется DateTime.Now |
| Application/Itineraries/ItineraryCandidateService.cs, ItineraryCandidateContracts.cs | Get owner plan, expected revision, data port, deadline/admission orchestration и independent result validation |
| Infrastructure/Itineraries/CuratedMoscowFacts.cs, data/moscow-v1.json + moscow-v1.manifest.json + walking-evidence.json | Exact-byte digest/immutable release, reviewed package loading, digest/rights/schema/domain mapping; никаких runtime clients или persistence entities |
| Api/Endpoints/ItineraryCandidateEndpoint.cs, ItineraryAvailabilityEndpoint.cs, Contracts/ItineraryCandidateDtos.cs | Candidate POST и availability GET §4.1, strict request/result DTO/status/no-store; Composition регистрирует только при nested opt-in |
| shared/ts/api-client/src/trips-itinerary.types.ts, trips-itinerary.decoder.ts, trips-itinerary-api.service.ts | TS candidate/availability consumer с точными схемами §4/4.1, strict bounds; existing Trips v1 DTO unchanged |
| apps/web/src/app/trips/trips-itinerary-state.service.ts и trips-page.component.* | Preview state и explicit stage/save через текущий TripsStateService; session/edit epochs интегрируются, не дублируется editor |
| tools/trips/build-moscow.cs + CLI tests | Один bounded offline-only build helper,не runtime service |
| tests/trips/* + web unit/TS decoder + tests/travel-e2e/trips-demo | Failure-oriented tests по tasks; walking historical receipts сохранены, current integration/Rail проверяются отдельно |

Таблица фиксирует walking decomposition, теперь реализованную по отмеченным tasks; история planning не заменяет current-source verification. Api вне Composition не импортирует Infrastructure. No cross-module internals/shared domain logic. Host сохраняет единственное endpoint map и глобальные builders, project closure не меняется. Нет Trips→AI contract, потому что нет AI consumer; соседние TTL/permissions не заимствуются.

### 7. Acceptance и порядок gates

| Gate | Что доказывает | Критерий/статус сейчас |
|---|---|---|
| P — planning | coherent source-grounded plan + independent audit/recheck | Walking approved; Rail refinement проходит новый независимый plan review до реализации |
| D — real data | права/ID/freshness/покрытие реальной Москвы | D_demo:bytes/rights/graph/modelproof;D_livehumanfieldacceptance позже |
| S — structural source | детерминизм, bounded facts, privacy/revision/editor;0model/provider calls | Исторические walking receipts приняты; 4.5–4.8 проверяют Rail и текущую интеграцию заново |
| Q_demo / Q_live | Demo integrity vs human travel suitability | Automated real-source scenario checks дляdemo;humanQ_liveниже остаётсяpending |
| C — CI | exact-head ordinary PR/postmerge checks | Отдельная публикация после авторизации; M1 green не заменяет M2 CI |
| T — target | ready existing keys/schema, enabled real Host/Keycloak private UX, ingress | Отдельное разрешение и receipts; никакого provisioning сейчас |
| R — rollout | принятое D_live/S/Q_live/C/T и enable/rollback решение | Default-off до отдельного разрешения |

Dossier1.2/offlinebuild1.4 предшествуют packageintegration3.1. Source-readydemo требует D_demo+S+Q_demo+independentfullaudit;CI только после отдельногоpush. Q_demo=realpaths/model/rights/outcomes+stage/edit/save/reload,без humanscores,≥11/12filledtargets;все12Qrequests/12Scases вdenominator. D_live/Q_live/T/R отдельноpending;не заявлять live-ready.

Ниже сохранён Q_liveprotocolv1,не автоматически принятый после demoрешения. Q_demoиспользует frozeninputfamilies с отдельным source/modelrunbinding,без humanratings.

Quality protocol имеет две последовательные фиксации.1.3 до просмотра outputs замораживает24case IDs,конкретные briefs/expected families/rubric и case-set hash.3.1 после построения реального package,до первого eval output,добавляет отдельный immutable run-binding с payload hash,policyVersion,source/build SHA и asOfUtc; сами cases/rubric не редактируются. Unit/schema/ID checks не считаются просмотром Q outputs. Без обеих фиксаций Q не запускается.

Denominators раздельны: Q=12реальных supported route cases (1/2/3days×3paces плюс3mixed-supported-interest cases) на неизменённом принятом D package; S-edge=6 (food-only,food+history,unknown hours,expired edge,disconnected graph,insufficient distinct POI); S-negative=6 (foreign owner,stale revision,other city,4days,historical,outside horizon). Для induced expired/disconnected и прочих edge states допускаются явно вымышленные/изменённые S fixtures; они НЕ становятся accepted real package и НЕ прибавляют12human quality cases. Совокупные24/24 structural/outcome expectations проверяются и репортятся с этим разбиением; ошибка в любом case остаётся failure.

Для каждого из12Q cases два независимых человека отдельно ставят1–5 по geographic continuity,pace burden,interests,clarity of unknowns; они не видят score генератора. Acceptance для КАЖДОЙ пары(case,dimension):оба individual scores≥3 и их среднее≥4. Это24исходных score по каждому из4dimensions,не median по корпусу: высокие оценки других маршрутов не маскируют плохой. Disagreements>1 обсуждаются с сохранением originals; не заменять провальные original scores согласованными постфактум. Дополнительно≥11/12Q cases заполняют stop targets;empty/partial/failed case остаётся в denominator и не получает выдуманную human оценку. Любой empty/failed Q case не проходит per-case rubric. Для всех24cases:0invented facts,0known-closed-as-open,0unsupported-as-success и100%заданных outcome/structural assertions.

После tuning увиденные Q results становятся development; нужен новый отдельно frozen12supported-case confirmatory set с тем же run-binding и per-case thresholds до Q acceptance. Все12S edge/negative regressions повторяются на изменившемся source/policy. Failed results сохраняются,thresholds задним числом не снижаются. Сейчас curator/reviewers не назначены и scores отсутствуют; self/agent audit плана не является human route evaluation.

### 8. Расширение географии после этого M2 — направление, не текущие задачи

В change теперь 27 задач: walking generation остаётся Moscow-only, Rail добавляет consumer существующего timetable API. Его география — опубликованные станции РФ, а M1 сохраняет существующие города и 1–14 дней; это не расширение walking coverage. Новый walking city/package будет отдельным OpenSpec change; новые города/источники/registry/runtime сейчас не добавляются. Составление этой дорожки не разрешает следующий source или rollout.

Первую проверку данных второго города можно исследовать параллельно позднему M2,когда схема dossier уже стабилизирована. Начинать реализацию второй географии разумно после Moscow D_demo+S+Q_demo и exact-head ordinary CI(C),когда показан сквозной demoсценарий и измерено обслуживание данных;это не human/liveприёмка. Source/CI-only green недостаточен. Если target T пока pending,можно отдельно планировать/реализовывать default-off source следующего города после соответствующего поручения,но реальные пользователи обоих городов ждут T/R. Ждать M3 text parsing,AI framework,Flights/Hotels/Rail или общего booking rollout не требуется.

Последовательность:
1. Один дополнительный город из уже существующего M1 catalogue (saint-petersburg или kaliningrad),с тем же1–3day/outdoor scope. Для него отдельный versioned package,район coverage,источники/права/стоимость/обновление и Q_demo,с отдельным pendingQ_live. Moscow pass не засчитывается другому городу.
2. Реальный второй consumer даёт повод передать destination в fact-source selection/availability и выбрать один активный package на город. Общий ranking/validator по-прежнему получает normalized facts+destination zone; без копирования Moscow generator и без универсального provider registry. Текущие candidate/availability v1 ограничены moscow и no-query: поддержка второго города требует явного API/TS/guard/spec compatibility изменения,а не скрытого добавления query/enum. Сохранять Moscow поведение и owners/CAS.
3. Для городов ВНЕ M1 catalogue нужен отдельный совместимый city metadata change. Сейчас Core/TS имеют триcity IDs,две IANA zones и catalogueVersion=1. TripPlan.Restore сравнивает сохранённую version с текущей CityCatalogue.Version; TS decoder принимает только1. Простое bump Version сломает чтение старых защищённых snapshots. Нужны acceptance старых versions/ID/zone semantics при чтении,создание новой metadata по принятой версии и regression старых plaintext/ciphertext/detail/TS round-trips; без silent переписывания старых планов и без отключения strict validation.
4. Новые страны/зоны требуют IANA-based local dates/horizon/validity,holidays/seasonal access,языка оригинальных описаний и проверенных источников. Уже сохранённый destination snapshot остаётся историческим. Не переносить Europe/Moscow как глобальный default,не вычислять зону по названию города. Если валюты расширяются,это отдельные exact-money правила,без float/implicit FX.
5. После второго города измерить часы curation/recheck на city/month,долю partial/empty/unknown,reason counts и human quality;privacy diagnostics без private briefs/owner labels. Если ручные пакеты не обеспечивают freshness/стоимость при реальном спросе,отдельно сравнить разрешённый data feed/provider с текущей curated стоимостью. Только тогда вводить необходимый Infrastructure ACL под существующий consumer,его rights/cost/runtime gates; не включать paid API автоматически и не обещать покрытие «всего мира».

Добавить city в select не означает route coverage. Выбор будущего города делается по реальному спросу и доступному разрешённому набору,а не по наличию городского названия. Увеличение географии не обязано одновременно расширять itinerary duration,indoor bookings,transport modes или AI: каждое такое изменение имеет свои критерии.

Primary cross-geography references: [Wikidata structured-data licensing](https://www.wikidata.org/wiki/Wikidata:Licensing) для права выбранных полей и [IANA Time Zone Database](https://www.iana.org/time-zones) для location-specific rules,которые меняются с решениями о zones/offsets/DST. Эти страницы не доказывают права/качество каждого POI/edge и не означают уже выполненную интеграцию.

## Risks / Trade-offs

- Малое coverage и отсутствие food/indoor/arrival schedule → явно перечислить exclusions; если основной сценарий не полезен по Q, пересмотреть срез до runtime expansion.
- Offline graph/profile/model может не обеспечить coverage/caps → measured failure/revision,не выдумки;API=0не означает нулевую стоимостьподготовки. Uncalibrated labels и будущая field/humanприёмка сохраняются.
- Clock freshness не гарантирует отсутствие внезапного закрытия → known/unknown и историческая validity, без live feasibility claim.
- Ephemeral provenance исчезает как trusted structure после save → explicit historical note и M1 intention semantics; не рисовать verified badge после reload.
- Слишком много unknowns ухудшают качество → separate Q gate, а не подавление предупреждений.
- Succeeding generation может устареть до PUT → baseRevision+CAS и local epoch protection; без hidden merge/retry.
- Совместная разработка Rail/Trips меняет inventory → isolation и интеграция от актуального принятого dev; сохранять ancestry и не исправлять чужой checkout.

## Migration Plan

Новой схемы, EF migration, protection schema version, persistent candidate row или внешнего provisioning не планируется. Protected M1 v1 snapshots и таблицы остаются читаемыми без backfill. Историческая reference — обычная bounded note.

После реализации/аудита:закрыть D_demo/source tasks;enable/publication отдельно; default-off Trips:Itinerary:Enabled (работает только вместе с Trips:Enabled), package hash и отдельная readiness. Disabled candidate route404 до auth/DB/package resolution; M1 ручные функции сохраняются при candidate-off/unready. Candidate package failure не делает M1 readiness unhealthy и не меняет существующую Host readiness политику.

Публикация/target provisioning/enablement требуют своих разрешений. Rollback: выключить только itinerary flag; сохранить все пользовательские M1 plans, не отзывать/удалять keys и не менять DB. Пакет можно отозвать для новых previews, но принятые намерения не переписываются.

## Open Questions

Реальная offline-сборка и strict loader подтвердили20POI/90directed paths и resource caps; доказательства coverage сохранены в data-dossier.md и offline-helper-implementation.md. Текущая интеграция4.5–4.8 и первый bound Q_demo5.2 приняты:24complete records,12/12filled Q targets,13actual-body Node checks. Tuning не потребовался; human actors/field calibration/Q_live остаются pending. Fresh full-source audit6.1, одна волна исправлений четырёх замечаний и scoped recheck приняты: SOURCE/LOCALDEMO,SPEC,QUALITY PASS. Актуальный статус — verification.md; публикация и target не разрешены. Решения, меняющие scope/источник/API/качество, требуют обновить change и аудит до реализации.

### 9. Добавочный Rail consumer

Нормативные детали реализации, решения по valid limited rows, M1 foundation/append, bounded historical projection, lifetime, форме, privacy и dependency preflight находятся в [rail-refinement.md](rail-refinement.md); пользовательский контракт — [trip-rail-intentions](specs/trip-rail-intentions/spec.md). Это часть единого design этого change.

Rail переиспользует public TS RailApiService/strict DTO/RailState. После обновления dev до4ccdcaf1b9e1819f4532c0afe014ea611b550186 самостоятельная /rail сохраняет принятый Rail M2 интерфейс фильтров и подробностей маршрута; компактный RailSearchComponent остаётся потребителем Trips. Общими остаются public transport/decoder/state, без второго алгоритма поиска. Сервис backend-агрегации, новый persistence contract, .NET internal imports, framework и новый CAS machine не нужны. Existing empty-only walking wrapper остаётся над общим clean-saved opaque foundation. Rail append допускает непустые дни, но только для явно fictional profile; real-source panel transient и stage запрещён опубликованным retention contract.

17 исторически принятых walking задач не выдаются за current proof.1.5/4.5–4.8 теперь приняты. Q5.2 PHASE A связал harness с a8721effd9056459d217966083816455fbea599d, сохранив frozen fixture asOf2026-10-21T14:16:33.0000000Z и неизменные12Q+12S inputs/thresholds; binding предшествовал единственному исполнению. Rail fictional cases вне walking denominators. Финальный6.1 включает fresh most-capable full-source review и одну волну fixes со scoped recheck. Позднее пользователь22Oct2026 отдельно разрешил публикацию, mergePR65 и post-merge CI, затем sync/archive без включения.6.2 принята по exact PR/post-merge receipts;26/27задач завершены.6.3target/human/live/rollout остаётся открытой; исходные quality inputs и phase-bound receipts не переопределяются. См. [archive verification](archive-verification.md).

### Publication integration on22Oct2026

The newly fetched dev4ccdcaf includes accepted Rail M2 and Storybook10.6.3. This dated adaptation supersedes only the standalone-wrapper implementation choice in rail-refinement.md. Preserve standalone filters/details/focus and shared state cancellation/expiry, while the Trips picker retains strict fictional-only selection/admission. Detail-only expiry must not revoke an otherwise current selection; search/reference expiry must revoke it and abort pending details. No Trips route-details UI, durable real-source facts, extra provider call or model/geography scope is added. Historical a872 source/quality receipts retain their original identity; new integration verification is recorded separately in verification.md and PR65.

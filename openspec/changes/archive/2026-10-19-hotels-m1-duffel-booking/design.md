# Design

Архивировано2026-10-19:22/22 задач приняты владельцем с сохранённым failed memory результатом16/17. Следующие разделы — исходная история M1; текущие source/CI/archive итоги и границы приведены в [archive-verification.md](archive-verification.md).

## Context

**Draft for owner review, revised 2026-10-16.** Мотивация и единственная граница текущих полномочий — [proposal.md](proposal.md). Это переработка того же change по порученному аудиту, не реализация и не второй план.

Подтверждены: полный UI flow; один внешний Duffel Stays в M1; один номер на 1–2 взрослых; общий webhook-вход Flights/Hotels. Уточнения аудита: реальный общий Hotels contract, late identity recovery, v2/historical webhook compatibility, единая таблица состояний, простое хранение, короткий load proof и сквозной порядок задач. Полное product/source approval не выводится из завершённости planning.

Проверенная база после fetch: `HEAD = dev = origin/dev = 894c40f895b9e864263686f814ac0993bdbd3e8b`. До переработки в дереве только этот untracked change; product files не изменены. Hotels — четыре scaffold projects/marker и два пустых test projects, вне runtime Host.

Основания: [root](../../../../AGENTS.md) и [Hotels AGENTS](../../../../modules/hotels/AGENTS.md), [current-state](../../../../docs/architecture/current-state.md), [концепт](../../../../docs/superpowers/specs/2026-05-03-travel-platform-concept.md), ADR [0001](../../../../docs/adr/0001-modular-monolith.md), [0006](../../../../docs/adr/0006-testing-strategy.md), [0007](../../../../docs/adr/0007-storage-strategy-marten-ef-coexistence.md), [0008](../../../../docs/adr/0008-result-pattern-error-or.md), [0010](../../../../docs/adr/0010-keycloak-identity.md), [0023](../../../../docs/adr/0023-module-api-facades-and-cross-cutting-ownership.md), privacy precedent [0024](../../../../docs/adr/0024-flights-pii-protection.md). Майский концепт задаёт направление, а не подтверждённые supplier/runtime facts.

[Статья автора, строки 90–131](D:/blog/blog.vsinyavsky.ru/channels/blog/drafts/s03a03/article-s03a03-design.md:90) требует проверять обязанность контракта на обычном вызове и существенном пограничном случае: cache сохраняет готовый ответ, а unsupported отличается от successful-empty. Flights `GetSupport` полезен как форма; защитную ветку Travelpayouts с пустым списком для unsupported не копируем. [Dependency advisories](../../../../docs/operations/2026-10-16-dependency-advisories.md) и [braces backport](../../../../docs/operations/2026-10-16-braces-security-backport.md) сохраняются; OpenSpec остаётся pinned1.14.0, dependency/artifact bytes не меняются.

### Актуальные внешние основания

Официальные страницы проверены без аккаунтов и provider API calls. Они не доказывают доступ нашего аккаунта или реальное sandbox acceptance.

| Источник | Значение для проекта |
|---|---|
| Duffel [Stays guide](https://duffel.com/docs/guides/getting-started-with-stays), [test hotels](https://duffel.com/docs/guides/test-hotels), [quotes](https://duffel.com/docs/api/v2/quotes/create-quote), [bookings](https://duffel.com/docs/api/v2/bookings) | Отдельный Stays access; search/rates/quote/book flow, test scenarios. Выбран внешний adapter M1. |
| Duffel [response handling](https://duffel.com/docs/api/overview/response-handling) | Отложенное появление resource возможно спустя часы. Created связывает source quote и booking; admission+10m не может запрещать поздний detail read. |
| Duffel [v2 webhooks](https://duffel.com/docs/api/v2/webhooks), [event schema](https://duffel.com/docs/api/v2/webhook-events/schema) | Один URL на organisation/live_mode, `data.object`, mode и current event names; at-least-once/out-of-order delivery. |
| Hotelbeds [workflow](https://developer.hotelbeds.com/documentation/hotels/booking-api/workflow/) | `rateKey` и разные BOOKABLE/RECHECK пути: supplier quote ID не общий invariant. [Evaluation](https://developer.hotelbeds.com/documentation/getting-started/) имеет50req/day и отдельную certification. Только кандидат M2. |
| LiteAPI [terms §3/§5](https://liteapi.travel/terms/), [pricing](https://docs.liteapi.travel/reference/api-pricing-usage-costs) | Запрет cross-source mapping и отдельные платные endpoints. Наличие mapping API не даёт прав. |
| Amadeus [portal](https://admin.developers.amadeus.com/), [archived guides](https://github.com/amadeus4dev/developer-guides) | Self-Service закрыт; старый бесплатный sandbox не закладывается в M1/M2. Enterprise требует доступа. |

[Supplier-data terms Duffel §3](https://duffel.com/services-agreement) ограничивают использование/показ и требуют обновлять контент; supplier images не скачиваются/не хостятся у нас. Public repo/demo использует fictional fixtures/собственные assets. Права cross-source mapping/storage/display проверяются при выборе второго поставщика; country/account access не предполагается.

## Goals / Non-Goals

**Goals:** понятный сквозной demo UI, собственная модель Hotels, переносимый в пределах supported subset application scenario, durable single-dispatch, truthful asynchronous facts и измеримая single-process устойчивость.

**Non-Goals:** real-money/Production booking, карты/PSP, cancellation/refund/amendment actions, operator resolver/admin UI, multi-room/дети, saved guests, Support order links, AI, global catalogue, fuzzy/ML mapping. Нет общего booking engine/vault, Hotels ES, shared durable inbox, plugin registry, новых сервисов, distributed limiter или scheduler/resilience framework.

## Decisions

### D1. Модель и пользовательский результат

| Понятие | Граница |
|---|---|
| Property | Физический объект; supplier+environment ref в M1, без canonical cross-source identity. |
| Room / rate | Описание номера отдельно от board, eligibility, payment timing и cancellation terms. Имя не доказывает одинаковый продукт. |
| Stay / occupancy | Local DateOnly check-in и exclusive checkout, календарные ночи, одна allocation/1–2 adults. Ночь не равна24 UTC-часам. |
| Offer | Временное предложение для точных stay/occupancy, provenance/expiry если дан. Не inventory hold. |
| Local quote | Наша revision принимаемых условий и локальный срок пригодности; supplier preparation resource может отсутствовать. |
| Reservation / attempt | Owner-bound resource до отправки и единственный допущенный create; source booking ID/hotel reference — отдельные факты. |

Core владеет датами, составом покупки, деньгами, consent/revision и переходами D4. ErrorOr imports явные, factories возвращают ожидаемые ошибки, время — TimeProvider. Нужные события в прошедшем времени реализуют IDomainEvent; event store/публикация каждого изменения не требуются.

UI: curated geographic destination -> поиск -> property/room/rate -> login -> свежая local quote -> review/guest names+lead contacts -> явная покупка -> private detail/list. London/Paris/Rome — предложенный небольшой список без paid geocoding; fictional destination выделен. После auth redirect выбор/quote обновляются, PII из storage не восстанавливается.

До согласия видны whole-stay price, отдельные локальные суммы/валюты, питание с guest scope, ограничения/отмена с временем и зоной. Included tax не добавляется второй раз; unknown не равно zero; нет FX/повторного умножения на ночи/adults. После submit UI использует D4, не сообщает о customer charge/bank credit и не показывает claim/inbox/reconciliation. Loading/empty/failure/limited/expired, same-price terms change, mobile/keyboard и stale/logout cases проверяются в своём UI slice.

Предлагаемый M1 subset: тестовое немедленное бронирование без карт/customer PSP с известными debit/local mandatory amounts и eligibility. У Duffel это pay_now/balance; это не термин общего контракта. Иные случаи доступны для просмотра с причиной недоступности покупки. Empty Duffel cancellation timeline -> non-refundable; missing/malformed -> unknown: mapping, не универсальная Core эвристика.

### D2. Контракт Hotels и обязанности caller

Это формы будущих module-local типов, не написанный код или обещание подключить любое API без развития модели. API DTO отдельно; domain/application не видят provider JSON/HTTP/credentials. Поддержанный subset проверяется двумя semantic adapters.

| Способность | Существенный вход / результат | Обязанность caller |
|---|---|---|
| Search: IHotelSearchProvider | GetSupport(SearchRequest); SearchAsync -> SearchExecution; LoadRatesAsync(Selection) -> RatesExecution | Проверить поддержку без сети, сохранить skipped reason; передать весь result в snapshot/API/UI. Search-only source не получает buying methods. |
| Purchase: IHotelBookingProvider | GetSupport(SelectedRate, TestPurchase); PrepareAsync -> Ready(PreparedStay)/Unavailable/Unsupported(reason)/Failed(code); CreateAsync -> BookingOutcome | Создать local revision из facts, получить consent, защитить party и сохранить claim до единственного create. |
| Optional observation: IHotelReservationReader | ReadAsync(KnownBookingIdentity) -> NormalizedObservation | Вызывать лишь при наличии этой реально нужной способности, trustworthy ID и durable budget. Synchronous adapter не обязан иметь webhook/reader. |

Это роли caller, не interface на HTTP endpoint. Adapter реализует только Search, Search+Purchase или ещё Reader; webhook — отдельный seam D5. Обычные DI registrations выбирают capabilities по сохранённому provider key внутри Hotels, без универсального registry.

```text
SearchRequest = geographic area + StayDates + one RoomOccupancy + display preferences
Selection = local token + request binding + property/rate selection
SearchExecution / RatesExecution =
  Unsupported(reason) | Complete(offers) | Limited(offers, reason) | Failed(safeCode)
SearchSnapshot = SearchId + normalized request + offers + provider statuses + completeness + expiry
PreparedStay = normalized terms + local freshness bound + optional source expiry + ProviderContextHandle
LocalQuote = ReservationId + revision + accepted terms + ValidUntil + ProviderContextHandle
CreateInput = retained attempt ref + immutable accepted purchase + validated guest party in RAM
BookingOutcome = Confirmed(facts) | ReservedWithoutDetails(evidence) | Pending(evidence)
               | NotCreated(positiveEvidence) | Unknown(safeDiagnostic)
NormalizedObservation = attributable current facts / no-effect evidence / inconclusive
```

Complete([]) — successful-empty. Limited, включая provider partial response, не становится Complete даже при нуле offers. Disabled/unconfigured — skipped со своей причиной; request unsupported — skipped с его причиной; attempted failure — failed. GetSupport и defensive branches Search/LoadRates/Prepare используют одно правило и request-scoped config: прямой unsupported вызов тоже не делает HTTP и возвращает Unsupported, а не пустой success.

ProviderContextHandle — закрытая ссылка на bounded versioned adapter-owned context, связанная с adapter/environment/selection и затем quote revision. Transient context входит в selection snapshot. Ready передаёт persistence boundary не только handle, но и bounded opaque serialized state с adapter/environment/format-version binding; эта state сохраняется в local quote record той же транзакцией. Application её не декодирует: не public dictionary, DTO bag, URL или обязательный quote_id. Infrastructure создаёт свою minimal typed state (Duffel preparation refs или fake offer key), без raw response, credentials и guest data. После restart handle разрешается из saved state, не только RAM. Safe source refs отдельно определены D3.

```text
support = source.GetSupport(request)
execution = support.supported ? await source.SearchAsync(request) : Unsupported(support.reason)
reply = mapCompleteExecution(execution, request)
selectionCache.Store(reply)    // offers + skipped/failure/limited вместе
prepared = await purchasingCapability.PrepareAsync(validatedSelection)
quote = saveOwnedRevision(prepared.terms, prepared.context)
accepted = validateConsent(quote, owner, request.revision)
if existingAuthorizedAttempt(accepted): return replayWithoutNewProviderPermit()
permit = tryAcquireProviderCapacity()  // refusal: no new durable claim
if not permit.acquired: return safeCapacityRefusal(quoteUnsubmitted)
try:
  admission = trySaveProtectedPurchaseAndClaim(accepted)
  if not admission.won: return replayOrConflict(admission.retainedAttempt)
  attempt = admission.attempt
  outcome = await purchasingCapability.CreateAsync(attempt.purchase, guestPartyInRam)
  saveDecision(Core.Apply(outcome))  // adapter нормализует facts, Core выбирает переход
finally: release(permit)
```

Край:2 adults допустимы Hotels, но альтернативный fake поддерживает только1. Handler сохраняет skipped reason; прямой SearchAsync даёт тот же Unsupported без сети. Отдельные случаи Complete([])/Failed/Limited переживают cache round-trip. Supported fake готовит terms без supplier quote ID и синхронно подтверждает бронь без webhook/reader: тот же quote/consent/claim/UI flow проходит без if(provider == Duffel). Это небольшой test substitute, не runtime simulation API или реальная вторая интеграция.

Отказ от общего интерфейса из пяти Duffel calls убирает source protocol из caller. Отказ от universal abstraction сохраняет право развить модель при другом бизнес-scenario. Контракт обязан лишь поддерживать и проверять описанный subset.

### D3. Одно relational authority и минимальная диагностика

EF Core/schema hotels, без Marten для Hotels. Source of truth — committed reservation, не cache/browser/webhook reply. Число таблиц определяется constraints/retention, не количеством имён в обсуждении.

| Логическое содержимое | Где и зачем |
|---|---|
| Accepted terms/revision/context | В reservation: заморозить покупку; до admission возможны revision-checked replacements. |
| Single attempt/action/claim/correlation | Неизменяемая часть той же reservation: one sender и recovery после response loss/restart. Receipt — safe read DTO, не подсистема. |
| Current outcome/review/facts/read counters | Там же с concurrency revision: conditional transitions D4 и единый durable read budget. |
| Guest/contact private content | Protected envelope в строке либо отдельной owned части лишь для удобства physical removal; не vault. |
| Observation inbox/existing outbox | Отдельны из-за provider event dedup/доставки/atomic acceptance. Не shared inbox. |

Самостоятельного journal нет: достаточно bounded source/time/provenance/conflict facts reservation. Conflict sticky до будущего resolver; raw payload history для него не нужна. Очистка private content оставляет минимальную reservation/attempt identity и spent claim в retained записи, без отдельной tombstone subsystem. Дополнительная таблица обосновывается конкретным uniqueness/cleanup invariant.

Unique owner/reservation/action, один claimed attempt, conditional revision и immutable accepted context исключают вторую отправку этой покупки. Для Duffel дополнительно unique source quote binding в receiver/account/mode; общего quote_id invariant нет. После admission context/revision не заменяются. Даже после NotCreated новое явное приобретение требует нового resource, свежих условий/consent; старое действие возвращает старый receipt.

Порядок: authorize/validate -> разрешить existing-attempt replay без нового provider permit -> для новой попытки получить необходимый provider capacity permit -> protect guests -> атомарно сохранить purchase/attempt/dispatch claim и escalation message через existing EF outbox -> только выигравший inline sender делает create -> conditional save нормализованных facts. Safe capacity refusal оставляет новую покупку неотправленной, quote без нового claim и не расходует durable creation barrier. После получения permit транзакция повторно проверяет revision/отсутствие competing attempt; проигрыш гонки возвращает существующую attempt или conflict без отправки.

Permit удерживается на ограниченном sending lifetime и освобождается при failed commit, проигрыше гонки или завершении вызова, включая исключение/timeout. Освобождение permit никогда не сбрасывает committed claim. Нет background create/retry. Browser abort не отменяет bounded sender. Crash после claim до sending может оставить Unknown с нулём sends — допустимая цена демо, а не право повторить create. External success/local save failure разрешается лишь attributable observation.

Два разных integration proofs A8: при доступной capacity для всех50 валидных contenders и исправных store/fake supplier гонка обязана дать ровно1 create и одну retained attempt с результатом; остальные дают replay/conflict без effects. Capacity в этой контролируемой fixture не отсекает contenders, поэтому ноль create не является проходом. Отдельный capacity-exhaustion case новой покупки требует0 create/0 new claims и сохранённой неотправленной quote. Failed commit/losing race дополнительно проверяют освобождение permit; replay существующей attempt работает и при исчерпанной capacity без нового permit. Это доказательства existing limiter/store поведения, не новый admission framework.

Replay сравнивает authorized normalized content с saved protected envelope внутри trusted boundary. Changed content конфликтует; key outage не допускает plaintext fallback; metadata GET без decrypt. Cache TTL/новый key не меняют claim. Глобальное распознавание двух намеренно созданных человеком одинаковых покупок не обещается.

Safe diagnostic summary: ReservationId, AttemptId, provider/environment/receiver binding, bounded request_id при наличии, opaque offer/preparation/booking refs, admitted/observed time и фиксированная outcome/error classification. Source refs — разрешённые metadata identifiers, не DTO dictionary; длина/символы ограничены. Они доступны в operator-safe logs/runbook evidence, не через public/foreign reads. Tokens, guests/contacts, raw provider errors/messages/bodies и arbitrary headers ради диагностики не сохраняются. Duffel request/quote refs остаются пригодны для разбора Unknown.

Runbook: прочитать local metadata/attempt; найти source correlation; отличить business facts от delivery failure; при unknown ID/исчерпанных reads обратиться к поставщику по refs. Нет DB reset, resend, payout claims или owner override. Resolver/admin UI вне M1.

### D4. Одна таблица состояний и late identity recovery

Outcome — факт брони, ReviewReasons — bounded причины проверки, не второй workflow. EverReserved/сохранённые факты переживают cancellation, но Cancelled не является active booking. Confirmed означает доказанную фактически созданную бронь; отличия accepted/actual видны и требуют review, а не обещания исполнить accepted terms.

| Нормализованный факт / текущее состояние | Следующий Outcome | Review и UI |
|---|---|---|
| Fresh accepted purchase / Quoted | Submitted | «Бронируем»; claim сохранён |
| Полное attributable confirmation / Submitted, Pending, Unknown, ReservedAwaitingDetails | Confirmed | Matching complete facts снимают лишь Deadline/MissingDetails/TransportUnknown; «Бронь подтверждена» |
| Достоверное резервирование без деталей | ReservedAwaitingDetails | «Поставщик сообщил о брони, детали уточняются»; reference не выдумывается |
| Поставщик определяет исход | Pending | «Ожидаем ответ поставщика»; create не повторяется |
| Потеря ответа/неопределённость после возможной отправки | Unknown, если positive facts ещё нет | «Результат пока неизвестен»; prior positive outcome не стирается |
| Доказанное no-creation, sender завершён, positive reservation evidence нет | NotCreated | «Бронь не создана»; старая attempt/claim сохраняется |
| Доказанная внешняя отмена exact booking / любое attributable состояние | Cancelled | «Бронь отменена поставщиком»; не active, без refund/credit promise |
| Admission+escalation time, исход/детали не установлены | прежний outcome | Добавить Deadline; «Нужна проверка»; время не доказывает no-effect |
| Confirmation с отличающимися существенными terms | Confirmed | Required(TermsMismatch), accepted/actual differences; без cancel/refund/rebook |
| Поздние согласованные complete facts / только причины ожидания | соответствующий Confirmed или Cancelled | Снять лишь действительно разрешённые Deadline/MissingDetails/TransportUnknown; claim не снимается |
| NotCreated -> positive поздняя бронь/отмена, либо success -> conflicting no-effect | Positive Confirmed/Cancelled имеет приоритет над NotCreated | Required(Conflict); later matching GET conflict не стирает |
| Cancelled -> late created/старый Confirmed той же брони | Cancelled | Не воскресить; новый contradictory current fact даёт Required(Conflict) |

Остальные недоказанные/несовместимые переходы сохраняют установленные facts и добавляют conflict/unavailable reason. Duplicate facts идемпотентны; identity mismatch ничего не применяет. Cancelled с неполными historical details может сохранять MissingDetails; cancellation не доказывает money outcome. Conflict/TermsMismatch автоматически не очищаются. При неизменных facts не растут revision/retention/deadlines от дублей.

**Два разных времени, один маленький механизм:**

- Escalation time фиксируется при admission (начально +10m). Одно durable сообщение меняет review, не делает supplier calls при unknown booking ID.
- Первая trustworthy booking identity привязывается атомарно, в том числе через часы после escalation. Один раз сохраняются boundBookingId, firstKnownAt, readsClaimed, readUntil. Начальный budget: максимум3 GET за5min от firstKnownAt, delays0/30/120s.
- Read claim/counter сохраняются до HTTP и work через existing outbox. Дубли/restart не сбрасывают budget/window. Capacity denial до HTTP не расходует read claim; bounded reschedule лишь до той же readUntil, без новой очереди/бесконечного цикла. Crash после read claim консервативно расходует slot.
- Late first ID после +10m разрешает этот единственный read window. Другой второй ID — Conflict, не новый budget. Исчерпанный ID/duplicate created не открывает ещё три reads. No ID -> ноль GET, без list/discovery.
- Новый full trustworthy callback уточняет facts и после исчерпания reads: это evidence, не restart polling. Owner GET/list всегда локальны. Synchronous adapter без Reader завершает supported happy path direct outcome; uncertain результат при отсутствии доступной observation capability остаётся review.

Достаточно outbox escalation/read-known-reservation messages и малых handlers с persisted counters. Scheduler framework и операторский workflow не добавляются.

### D5. Duffel mapping и общий ingress

Duffel adapter использует current search/rates/quote/book APIs. Quote_id — private adapter context/correlation, не local quote identity. Только decoder знает: complete201 -> Confirmed, documented200 -> ReservedAwaitingDetails, 202/already-attempted -> Pending, полное первоначальное provider no-creation503 -> NotCreated evidence; timeout/malformed/proxy503/generic failure -> Unknown. Код HTTP вне provider contract/последующий read error не доказывает no-effect. Pay_now/balance, source expiry и подпись остаются здесь, не в Core.

Prepare даёт normalized terms/local freshness; supplier quote resource не обязателен. Duffel Ready проверяет свой source rate expiry и не обещает supported Ready при missing обязательном факте. Другой compatible adapter использует свою valid preparation rule. Local freshness не обещает inventory hold.

Один `/webhooks/duffel`: Host проверяет RAM bound/single signature и передаёт exact bytes через BCL raw Probe/Accept seam. Два Infrastructure receivers через Api.Composition; Host не разбирает Duffel JSON/не импортирует internals. Probe валидирует источник/family без DB/PII writes; Accept получает только один owner. Two owners -> conflict/zero writes. Trusted canonical receiver configuration/key задаёт account/mode; optional signed organisation не выдумывается. NotApplicable — authenticated чужая family, не key failure/unknown quote/disabled writer.

Own inbox+outbox commit/duplicate -> ACK; own storage/protection failure -> non-2xx/redelivery. Нет foreign inbox copies, shared durable inbox или cross-module transaction. Unrelated PII-key/DB outage не мешает своему owner. Unknown Hotels quote остаётся Hotels-owned bounded unmatched evidence, без heuristic attachment. Existing route/signature-error responses сохраняются; malformed new messages дают fixed errors. Required receivers/drain проверяются независимо от BookingEnabled: отсутствующий owner не превращает event в ignored.

#### Новая network delivery и сохранённая история

Исходные [reader](../../../../modules/flights/Travel.Modules.Flights.Infrastructure/Webhooks/DuffelWebhookPayloadReader.cs), [DTO](../../../../modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/Dto/DuffelWebhookEventDto.cs) и ProtectedWebhookPayloadCodec.ProviderOrderId ищут root object/исторические event names. Текущая документация использует data.object и order.airline_initiated_change_detected. Перенос route обязан устранить разрыв в пределах этого receiver, не исправлять все Flights integrations.

| Вход | Ограниченное правило |
|---|---|
| Новая network delivery | Строгий v2: id/type/api_version, data.object, live_mode, source time и optional organisation binding. Нет root-object fallback; конфликт двух shapes отклоняется. Signature по original bytes. |
| Новые supported Flights events | order.created -> существующие подтверждённые ticket facts; order.airline_initiated_change_detected -> существующий change notice без нового financial outcome. Другие current events явно ignored/unsupported вне отдельного scope. |
| Новые Stays events | stays.booking.created, stays.booking_creation_failed, stays.booking.cancelled с exact positive correlation. |
| Saved historical Flights inbox | Original Source/event IDs, ciphertext purposes/routing binding и root-object читаются compatibility path; старые order.airline_initiated_change и .cancelled сохраняют смысл. Bytes не переписываются, mode/new fields задним числом не выдумываются. |

Новые entries после restart читает v2-aware codec/reader; historical protected/legacy entries — original saved semantics. Discriminator — сохранённый формат/метаданные, не weakened network fallback. Любая повторная network delivery сначала проходит текущие signature/v2/mode проверки и только затем dedup по прежнему Source/event ID. Валидный v2 duplicate не переписывает сохранённый historical envelope; root-object bytes со знакомым ID на сети всё равно отклоняются. Historical bytes читаются только из saved inbox. Оба пути тестируются fictional values реальной структуры; отсутствующие business facts не выводятся из одного event name. Изменение ограничено ingress/reader/codec/fixtures; все Duffel events не реализуются.

### D6. Настройки и измеримый single-process load proof

Числа ниже — **начальные configurable settings**, не измеренная производительность или квота всей Duffel organisation. Используются .NET/ASP.NET limiters, Host pipeline и existing outbox; нет новых сервисов/multi-replica coordination. Настройки валидируются, повышение лимита не считается исправлением проваленной приёмки.

| Настройка | Единица и поведение |
|---|---|
| 2 concurrent /30 provider starts за настроенное60s окно | Foreground search/rates/prepare/create admission: до24/min,1 active. Known-ID recovery: защищённые6/min,1 active. Общий envelope30/2, без permit queue; private status/ingress ACK не берут provider permit. Реальные rate-limit headers могут ужесточить допуск. |
| Search/prepare/read15s, create140s | Bound каждого вызова; original sender ограничен независимо от browser abort. Duffel требует client timeout≥130s; create не retry/hedge. Долгая покупка может отказать новому foreground search, но не блокирует status/inbox acceptance. |
| Cache128 entries /16MiB accounted payload | Entry = один **полный SearchSnapshot или RatesSnapshot**, не hotel/card. Оба вида входят в общий cap. Учитываются normalized UTF-8 payload+key до admission; managed-object overhead измеряется отдельно. |
| Snapshot TTL5m/source expiry; response2MiB /100 properties /200 rates | Entry удаляется целиком. Eviction/restart -> selection expired/новый search, без воздействия на admitted purchase. Truncated JSON -> failure; лишь complete decoded subset может быть Limited. Failures/terms не усекаются молча. |
| Request16KiB, signed ingress2MiB RAM | Guard до binder/дорогих действий, без disk spill и raw-value diagnostics. |
| Escalation10m; first identity window5m/3reads | Initial D4 settings сохраняются при admission/first binding и не сбрасываются restart. |

SearchSnapshot содержит normalized criteria, offers, provider statuses/skips/failures и completeness. RatesSnapshot — selection/request binding и полный result каталога. Hit возвращает весь первоначальный result, не реконструирует его из offers. Отдельные entries связаны scope/expiry; размер карточки не считается отдельным cache entry. Raw provider body не кешируется.

Date/geography intake первоначально future check-in +1..330 UTC calendar days,1..99 nights,radius1..20km: это начальный admission profile и Duffel support bounds, не universal Core rules. Stay dates остаются accommodation-local calendar dates. Adapter offline support проверяет свои лимиты. Quote freshness первоначально5m, ограничена supplied source validity; compatible adapter без source expiry всё равно может подготовить допустимую local quote.

**L1 — один короткий будущий воспроизводимый тест:** production Host test process с настоящими Hotels/shared-ingress pipeline, disposable PostgreSQL/outbox, fictional protection keys/test identity и fake HTTP supplier; без внешней сети. Reference budget:2 logical CPU,2GiB доступной RAM, Host working-set ceiling1GiB. До запуска receipt фиксирует SHA/.NET/OS, фактические CPU/RAM/container limits, DB/runtime versions, настройки, seed/dataset. Несовпадающее окружение объявляется заранее. Профиль/критерии не меняются после результатов без нового review.

| Фаза | Время и offered load |
|---|---|
| Warm-up |30s, исключается из latency gates |
| Normal |120s: равномерно supplier operations search10/min,rates4/min,prepare3/min,create3/min,known-ID reads2/min; private status5/s,signed observation admission2/min; supplier100ms, валидный непустой dataset/cache misses |
| Degraded |60s тот же mix:30% read/preparation replies delayed3s,10% safe failures; один create теряет reply через закрытие соединения≤1s после fake-effect, без retry. Полный140s timeout проверяется отдельно fake clock/failure tests, не удерживает slot этого короткого прогона. |
| Burst |30s:40 search requests/s поверх status5/s и callbacks1/s для seeded purchases; supplier500ms. Валидная50-contender purchase race проверяется отдельно A8, без зависимости от перегруженного лимитера этого прогона. |
| Recovery |120s normal mix/supplier100ms после снятия всплеска: заранее выделены первые60s восстановления admission window и последние60s steady recovery. Все offered requests/отказы обоих интервалов учитываются отдельно и в общем denominator. |

Полный denominator включает отказанные запросы. По фазам записываются offered/completed/полезный throughput, p95 отдельно search/status/ACK и всех admission responses, доли429/5xx/limited, supplier starts/max active, inbox/outbox lag, cache count/bytes, Host RSS/managed heap/CPU и recovery time. Полезный search — валидный Complete/Limited, не быстрый429; fake normal/recovery dataset непустой. Cache hit отмечается отдельно, не подменяет external capacity.

Предлагаемые gates:

- Normal и последние60s заранее заданной Recovery:≥95% запланированных valid search и purchase scenarios успешны, search p95≤1s,status/ACK p95≤500ms. Первые60s Recovery не исключаются из evidence: отдельно видны offered/completed/refusals и общий результат всей фазы. Это не postselection после запуска и не429-only proof.
- Burst: status/ACK≥95% успеха,p95≤1s; уже принятые наблюдения не голодают за поиском. Provider starts/active соблюдают настроенные window/dedicated shares; никакой unbounded permit queue.
- Cache≤128 entries/16MiB accounted payload; RSS≤1GiB. При одинаковом bounded retained dataset recovery working set не имеет продолжающегося роста, post-warm-up baseline tolerance128MiB; object overhead не выдаётся за payload bytes.
- Каждая admitted local operation достигает bounded outcome/review. Отдельный A8 proof требует ровно1create/1retained attempt/result при валидной гонке с доступными permits; capacity-exhaustion proof требует0create/0new claims. L1 не заменяет эти assertions формальным «максимум1», допускающим отказ всем. После снятия нагрузки полезный success rate возвращается к normal gate≤75s, учитывая60s admission window; delivery lag возвращается к baseline, нет утечки pending work. Скорость recovery измеряется по offered requests, а не reset лимитера. Total L1 duration360s включает warm-up и оба recovery интервала.

Dataset достаточен, чтобы реально проверить entry/byte bounds; memory сравнивается при одинаковой retained cardinality. Проход одного sanity/resilience теста не доказывает production capacity или статистику реального p95. Failure сохраняется как failure, без фильтрации429/повышения timeout. Real supplier latency/quota — отдельная sandbox acceptance.

**Принятое ограничение завершения M1 — 2026-10-17.** После завершённых L1 и отдельной диагностики пользователь явно принял ограничение проверки памяти и поручил завершить M1 без новых прогонов. Для этого M1 допускается завершение source/fictional acceptance с сохранённым результатом **16/17**, `workingSetRecovery=false`, `passed=false`, exit1. Это решение о принятии ограничения, а не изменение измерения или подтверждение успешности непройденного критерия.

Исходный strict RSS recovery gate, численные пороги, профиль и raw evidence сохраняются. Полная сопоставимость удерживаемых данных и отсутствие утечки не доказаны: росли данные имитатора/отложенных сообщений, а во время диагностической нагрузки не было естественной Gen2сборки. Лимит RSS1GiB и остальные16проверок пройдены в измеренном окружении; hard2GiBreference capacity остаётся непроверенной. Диагностический прогон не заменяет исходную приёмку. Обоснование и прямое решение пользователя — в [verification.md](verification.md#принятое-пользователем-ограничение-и-завершение-m1--2026-10-17). Исключение ограничено завершением этого M1, не меняет будущие критерии и не разрешает новые тесты, supplier calls, provisioning или rollout.

### D7. Privacy, retention и composition

Owner — valid GUID principal/audience и hotels:book; authorize до decrypt/effects, foreign/missing одинаково скрыты, no-store в том числе401/403. M1: имена гостей/lead email+phone и explicit adulthood/check-in acknowledgement, без passport/DOB/cards/free-form requests. Unsupported eligibility не угадывается. Tokens/PII/consent/action bodies не входят в storage/URLs/SSR/logs; navigation только ReservationId. Logout/scope loss/stale callbacks очищают RAM, не разрешая resend.

Hotels Data Protection purpose/application name Travel.Hotels.Pii.v1, durable certificate-protected ring, explicit key-only initialization, без normal bootstrap. Binding: format/owner/reservation/quote revision/guest count. Защита до persistence/outbox; raw sensitive callbacks, если их хранение необходимо, шифруются с receiver/routing binding. Metadata GET без PII decrypt. Нет переиспользования внутренних Flights ring/provider или общего vault.

Предложенный demo retention: drafts24h; conclusively finished private content90days после max(checkout,terminal outcome); bounded physical removal без decrypt. Минимальные consumed identities/claim/safe correlation остаются в reservation record D3; old POST не становится fresh create. Unresolved identity/barrier не истекают. Эти fictional defaults не являются утверждённой real-traffic retention policy.

Layer boundaries: Core -> Application -> Infrastructure -> Api, единственная Host module surface — Api.Composition. DTO/client/mapping/decoder/resilience — Infrastructure; process builders/security order/endpoint map — Host. Shared charters сохраняются; raw delivery seam имеет двух реальных consumers. Чужие Money/booking/idempotency/crypto не импортируются. Exact allowlists расширяются только вместе с implemented Hotels facade, guards не ослабляются.

Anonymous search/rates/private quote/book/list/detail — /api/hotels/*; общий webhook — /webhooks/duffel. API отдаёт наши typed results/states. Test-only default-off adapter проверяет duffel_test_ credential form/mode до HTTP, без redirects/endpoint override/TestOnly Production. Writer availability отделена от receiver/drain. Будущий schema config owner-qualified, startup Production validation-only ADR0007; сейчас Host не меняется.

### D8. Traceability и сквозной порядок

S/B/W — точные requirement names в трёх delta specs; A — acceptance evidence. Проверяется связь каждой строки, не только наличие headings.

| Requirement | Decision | Task | Acceptance |
|---|---|---|---|
| S: Supported stay and occupancy | D1/D6 | 1.1,1.3,2.1 | A1 calendar/occupancy/intake/source support |
| S: Bounded geographical destination search | D1 | 1.3 | A1 curated UI/invalid geography/no paid location |
| S: Explicit supplier availability and failure | D2 | 1.1,1.2,2.1 | A2 offline/direct Unsupported, Complete-empty/Failed/Limited |
| S: Property room and rate distinction | D1 | 1.3,2.1 | A3 scoped room/board/ratings |
| S: Whole-stay exact prices and separate obligations | D1 | 1.1,2.1,3.2 | A3 no double count/FX/unknown-zero confusion |
| S: Current selection and indicative availability | D1/D2/D6 | 1.2,1.3,3.1 | A4 full snapshot round-trip/binding/expiry/eviction |
| S: Eligibility and source restrictions | D1/D5 | 2.1,2.3,3.2 | A3 supported subset/content permissions |
| S: Bounded safe search interaction | D6/D7 | 1.2,2.2,3.6 | A5 body/response bounds/safe diagnostics |
| S: Useful bounded single-process resilience | D6 | 5.2 | A14 L1 throughput/p95/refusals/resources/recovery |
| B: Provider-neutral accepted purchase | D2 | 1.1,1.4,3.1 | A6 same flow/two semantic adapters/search-only has no buy |
| B: Owner-bound reservation and quote | D3/D7 | 3.1,3.6 | A7 auth/owner/no-store/current quote |
| B: Exact current purchase consent | D1/D2 | 3.1,3.2 | A7 same-price terms change/concurrent revision |
| B: Supported test purchase and complete obligations | D1/D5 | 2.3,3.2 | A3/A6 test funding without generic balance |
| B: Faithful cancellation and check-in disclosures | D1/D5 | 2.1,3.2 | A3 source scope/timezone/empty vs missing |
| B: Durable single booking dispatch | D3/D6 | 3.3,4.3 | A8 permit-before-claim; valid50->1create/1attempt/result; exhausted->0create/0claims; commit/race release; crash |
| B: Retained action identity and safe replay | D3/D7 | 3.3,3.5 | A8/A9 replay без нового permit/cleanup/key outage |
| B: Truthful supplier outcomes | D4/D5 | 3.3,3.4 | A10 canonical table/provider mapping/UI |
| B: Accepted-to-actual matching and conflicts | D4 | 3.4,4.4 | A10 differences/terminal cancellation/sticky conflict |
| B: Authenticated correlated supplier observations | D4/D5 | 4.2,4.3 | A11 binding/signature/atomic inbox-outbox |
| B: Monotonic evidence and late events | D4 | 4.3,4.4 | A10/A12 late created/cancelled/conflict |
| B: Bounded observation and explicit manual limit | D4 | 4.3,4.4 | A12 200->+10m->late first ID, max3/no reset/list/create |
| B: Protected minimal guest details | D3/D7 | 3.2,3.3 | A9 bound ciphertext/no plaintext persistence/logs |
| B: Private authoritative reads and browser lifetime | D1/D7 | 3.4,3.6,4.4 | A7/A13 reload/logout/stale, zero supplier status reads |
| B: Bounded retention without replay revival | D3/D7 | 3.5 | A9 private cleanup retains consumed identity/barrier |
| B: Safe correlation and diagnostic recovery | D3 | 3.3,4.5 | A11 safe refs/runbook/no raw errors/resolver |
| B: Complete fictional UI and separate acceptance | D1/D7 | 3.6,5.1,5.3 | A13 1/2-adult keyboard/mobile/demo/no real API |
| W: One bounded compatible public entry | D5 | 4.1 | A11 one route/raw bytes/errors/RAM bound |
| W: Authenticated unique module ownership | D5 | 4.1,4.2 | A11 two owners->zero writes/unmatched owned |
| W: Atomic owner acceptance and truthful acknowledgement | D5 | 4.2 | A11 commit/duplicate/rollback/ACK loss |
| W: Receiver mode and account binding | D5 | 4.1,4.2 | A11 signature/mode/account mismatch |
| W: Current wire format and historical saved entries | D5 | 4.2 | A11 strict v2 до network dedup; valid v2 duplicate без rewrite; old root object только saved reader |
| W: Isolated module evidence and historical compatibility | D5 | 4.2 | A11 zero foreign rows/old Source-purpose-dedup |
| W: Receiver continuity after writer disablement | D5/D7 | 4.1,4.5 | A11 receiving/drain independent from writer |

Порядок: contracts/fakes/search UI -> Duffel read mapping на fake HTTP -> quote/PII/durable buy/private result с UI -> shared webhook/lost response/restart/late facts/historical Flights -> whole flow/load/demo evidence. Тесты/notes входят в свой slice, не откладываются до финала. После отдельного implementer gpt-6.1-sol основной агент лично проверяет результат.

## Risks / Trade-offs

- Контракт subset не universal compatibility -> semantic fake проверяет counterexample; real multi-provider readiness лишь после настоящей второй интеграции.
- Crash после claim может дать zero-send Unknown -> barrier/safe diagnostics, без retry creator.
- Strict financial subset уменьшает доступность -> truthful unsupported, без ослабления money/PII guarantees.
- Missing ID/exhausted reads/conflict могут остаться ReviewRequired -> короткий runbook без resolver; late first identity больше не закрыта admission deadline.
- L1 settings ещё не измерены -> predeclared run/полный denominator, не429-only throughput.
- V2/historical gap -> два parsing paths с fixture evidence, без history rewrite/ремонта всех Flights API.

## Migration Plan

Сначала отдельное поручение на реализацию/допустимый disposable contour и отдельная авторизация source migration generation. Additive SQL review не означает target apply. Source facade/guards/realm fixture меняются при той реализации; target schema/keys/grants/subscription/доступ Duffel/publication — отдельные gates согласно proposal.

Shared ingress с прежним URL внедряется до Stays subscription; оба compatible receivers работают вместе. После writes rollback сохраняет reader/receiver/drain, минимальные identities/correlation и ключи. Down/удаление evidence не обычный rollback. Startup Production validation-only, TestOnly исключён из Production.

Roadmap: M1 — один внешний Duffel и semantic fake proof; M2 — второй реальный допустимый provider, bounded fan-out, partial/skipped/failures, verified mappings/dedup и deterministic ranking. Третий provider нужен для нового существенного случая, не расширяет M1. Multi-room/children/post-booking/AI — самостоятельные будущие решения.

## Open Questions

Позже проверяются Stays access/account quotas/terms и sandbox examples: недоступность блокирует runtime acceptance, а не автоматически меняет provider. До L1 фиксируется фактическое reference environment. Второй provider/права mapping-storage выбираются в M2.

На full review остаются предложенные product limits: curated destinations, strict price/eligibility subset, отсутствие cancellation/manual resolver и demo retention. D6 numbers — configurable settings, не доказанная производительность. Уточнения audit contract/late facts/v2 compatibility/вертикального порядка включены в тот же corpus без нового product scope.

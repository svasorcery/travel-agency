# Design

Статус на 17 ноября 2026 года — дизайн принят с пятью правками саморевью. Согласованный предел — до четырёх полных авиазаказов на поездку.

## Context

Мотивация и продуктовый scope — в [proposal](proposal.md). Исследован чистый `dev` на `6d9625c` до создания данного change.

| Текущее поведение | Источник | Следствие для дизайна |
| --- | --- | --- |
| TripPlan содержит brief, дни и обычные текстовые блоки; доверенной ссылки на booking нет | [TripPlan.cs](../../../../modules/trips/Travel.Modules.Trips.Core/TripPlan.cs) | Связь нельзя кодировать текстом заметки или распознавать из неё |
| Полный canonical detail одновременно является plaintext защищённого snapshot и HTTP-ответом | [TripPlanJsonCodec.cs](../../../../modules/trips/Travel.Modules.Trips.Infrastructure/Serialization/TripPlanJsonCodec.cs), [контракты](../../../../modules/trips/Travel.Modules.Trips.Application/TripPlanContracts.cs) | Добавление поля затронет строгое чтение, size reserve, старые ciphertext и клиентов |
| У календаря отдельные owner/revision/CAS, uncertain-write barrier и защита локальных правок | [TripPlanService.cs](../../../../modules/trips/Travel.Modules.Trips.Application/TripPlanService.cs), [Angular state](../../../../apps/web/src/app/trips/trips-state.service.ts) | Связи должны иметь собственный ресурс и не конкурировать с сохранением календаря |
| Таблица Trips имеет owner issuer+subject, protected payload и положительную revision | [TripsDbContext.cs](../../../../modules/trips/Travel.Modules.Trips.Infrastructure/Persistence/TripsDbContext.cs) | Можно добавить дочернюю коллекцию с FK, сохранив существующие строки и протокол |
| Flights Get/List читают eventual EF projection | [OrderQueries.cs](../../../../modules/flights/Travel.Modules.Flights.Application/Queries/OrderQueries.cs), [GetOrderHandler.cs](../../../../modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/GetOrderHandler.cs) | Picker не является доказательством source ownership; недавно созданный заказ может появиться позже |
| Support получает source-stream proof через опубликованный leaf-contract | [контракт](../../../../shared/dotnet/Travel.IntegrationContracts.Flights/SupportOrderAccess.cs), [реализация](../../../../modules/flights/Travel.Modules.Flights.Application/Support/FlightsSupportOrderAccess.cs), [triage](../../../../modules/flights/Travel.Modules.Flights.Application/Support/FlightsSupportTriageContext.cs) | Для Trips добавить самостоятельный минимальный контракт, не расширять Support DTO посторонними полями |
| Flights aggregate хранит GUID владельца, Itinerary и status; segments содержат DateTimeOffset, но не IANA zone | [BookingAggregate.cs](../../../../modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs), [Segment.cs](../../../../modules/flights/Travel.Modules.Flights.Core/ValueObjects/Segment.cs) | Использовать доверенный subject текущего единственного issuer; не восстанавливать IANA по offset |
| Architecture policy сейчас разрешает Flights integration leaf только текущим конкретным producer/consumer | [LayerArchitecturePolicy.cs](../../../../tests/Travel.Tests.Architecture/Support/LayerArchitecturePolicy.cs), [contract tests](../../../../tests/Travel.Tests.Architecture/SupportOrderAccessContractTests.cs) | Изменить project/IL/type allowlists адресно, сохранив прежние ограничения Support |
| Trips middleware классифицирует специальные маршруты до M1 query/body/ETag checks | [TripsRequestGuardMiddleware.cs](../../../../modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs) | Новый ресурс требует своего classifier и guard; нельзя передать его ETag парсеру календаря |
| Local-stack helper готовит Trips EF. В Development Host запускает упорядоченные инициализаторы Flights EF, Marten и Wolverine | [Trips helper](../../../../tools/trips/Travel.Trips.LocalStack/LocalDatabase.cs), [Flights EF](../../../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Initialization/FlightsEfInitializer.cs), [Marten](../../../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Initialization/FlightsMartenInitializer.cs), [Wolverine](../../../../apps/Travel.Host/Persistence/Initialization/WolverineMessageStoreInitializer.cs) | Профиль создаёт вымышленные события после готовности Host и использует обычную проекцию Flights |

## Goals / Non-Goals

**Goals:**

- Отдельный приватный ресурс связей; существующий план остаётся пригодным для offline-from-Flights чтения и ручного редактирования.
- Небольшой опубликованный межмодульный контракт без внутренних DTO и без внешних вызовов.
- Проверяемое разделение сохранённого пользовательского выбора, текущего наблюдения source stream и eventual списка кандидатов.
- Прежние M1 bytes/purposes/ETag и поведение двух city profiles сохраняются.

**Non-Goals:**

- Общий Trip booking aggregate, межмодульная финансовая транзакция и подписка на изменения заказов.
- Универсальная платформа service links для Hotels/Rail и новый механизм идентификации.
- Доказательство supplier/live/model/target готовности. Полный список продуктовых исключений — в proposal.

## Decisions

### 1. Коллекция ссылок рядом с календарём

Согласовано до 4 разных полных авиазаказов на один сохранённый план. OrderId — внутренний Flights aggregate ID, никогда provider reference. Один заказ может присутствовать в нескольких планах того же владельца: связь — организация личного материала, а не эксклюзивная бронь. Повтор одного ID в коллекции не допускается. Нет ссылки на отдельный leg и привязки к конкретному TripDay.

Дата/город/число взрослых не фильтруют заказы и не определяют возможность привязки: поездка может включать отдельно оформленные вылет и возвращение, исторический заказ или перелёт за границей прогулочных дней. Карточка показывает целый заказ и его party count; интерфейс явно объясняет, что соответствие плану проверяет человек. Автоматических compatibility warnings, city↔airport mapping и calendar expansion в этом этапе нет.

Рассмотренные варианты:

| Вариант | Плюс | Ограничение |
| --- | --- | --- |
| Текстовая заметка / обычный URL | Очень малый объём изменений | Нет доказанной связи, контроля владельца и обновления состояния |
| Полная копия заказа в plan snapshot | Самодостаточное историческое чтение | Дублирует приватные данные, устаревает; меняет строгий M1 protocol |
| **Отдельная защищённая коллекция ID + чтение Flights** | Старый календарь сохраняется; статус принадлежит Flights | Дополнительный ресурс, таблица и отдельная ревизия |

Принят третий вариант. Для хранения коллекции потребуется отдельная таблица Trips.

### 2. Два независимых авторитетных ресурса

`TripPlan` сохраняет свой HTTP/canonical payload, положительную revision, UpdatedAt, encryption binding и размер 128 KiB. Связи в этот payload не добавляются. Обычный PUT календаря сохраняет существующую коллекцию без её чтения и изменения. Добавление/удаление связи не меняет revision/UpdatedAt календаря и порядок списка поездок. UI не выдаёт ETag коллекции за ETag плана.

Новый `TripFlightLinks` — один защищённый bounded документ на TripId с собственной монотонной `linksRevision`. В plaintext только `{ schemaVersion: 1, links: [{ orderId, linkedAt }] }`, максимум 4 элемента. `linkedAt` — серверное UTC-время пользовательской привязки, не время покупки или source freshness. Коллекция сохраняет порядок добавления; удалить и заново добавить можно отдельными действиями. Никакой автоматической сортировки по временам перелёта.

Дочерняя EF-строка: TripId PK/FK, owner issuer+subject, положительная linksRevision и ciphertext. Отдельный purpose включает тип ресурса, schemaVersion, issuer, subject, TripId и linksRevision; существующий Trips key ring используется без изменения старого purpose. Protected payload ≤ 8 KiB, полное metadata response ≤ 16 KiB. Подмена строки между владельцами/планами/ревизиями отклоняется.

До первой записи отсутствующая дочерняя строка представляется виртуальной пустой коллекцией с revision `0`. GET сначала доказывает существование родительского плана текущего владельца. После первого изменения пустая коллекция сохраняет строку и свою положительную revision: это не позволяет старому запросу с `0` снова стать применимым после удаления последней ссылки. При исчерпании Int64 revision изменение отклоняется без переполнения.

В транзакции link store получает owner-qualified parent key-share lock, затем проверяет собственный CAS и пишет одну коллекцию. FK `ON DELETE CASCADE` и один порядок блокировок предотвращают orphan/recreation при гонке с удалением плана. Конкурентная первая вставка с revision `0` имеет одного победителя; другой получает 412. Удаление плана не требует decrypt дочерней строки и удаляет её даже при выключенном флаге связей. Сам план удаляется по своему обычному ETag.

### 3. Опубликованный контракт Flights для Trips

В существующем BCL+ErrorOr leaf `Travel.IntegrationContracts.Flights` добавить самостоятельный `TripsOrderAccess.cs`, с отдельным namespace `Travel.IntegrationContracts.Flights.Trips`. Предлагаемый порт `IFlightsTripsOrderAccess` имеет две read-only операции:

- `ListCandidatesAsync(ownerSubject, offset, cancellationToken)` — максимум 21 проекционная запись для страницы из 20; наружу только safe summaries и has-more/nextOffset. Маппинг выполняется в Flights Application, не в Trips.
- `ReadAsync(ownerSubject, orderId, cancellationToken)` — один authoritative stream read, возвращает owner/order proof, положительную stream version, observedAt, booking status, reviewRequired и nullable route summary. Trips использует его перед добавлением и при обновлении карточек.

Producer — `Flights.Application.Trips`; consumer — `Trips.Application.FlightLinks`. Только необходимые Composition types регистрируют порт; Host не импортирует реализации. Trips Core знает только собственные ID/collection invariants. Support namespaces и contracts не меняют смысл. Обновить project references, leaf exported-type expectations и IL allowlists для конкретных новых типов, а не разрешать весь Flights leaf каждому модулю. IntegrationContracts никогда не импортирует Core, Marten, EF, HTTP или provider DTO.

Проверка `ReadAsync` повторяет source ownership guards Support: непустые IDs, exact aggregate ID, тот же владелец, consistent mutation owner, materialized state Held/Confirmed/Ticketed/Cancelled/Refunded и валидная stream version. Quote-only/отсутствующий/чужой заказ → concealed not-found; ошибка источника → typed unavailable. Штатные cancellation tokens распространяются; query не делает save, reconcile, bus dispatch, decrypt passenger party, provider call или outbox запись.

Flights хранит только GUID владельца, Trips — issuer+GUID. Этот этап использует один уже проверяемый Host JWT issuer и его normalized subject; port не принимает owner из браузера и не расширяет multi-issuer trust. Внутри нового application use case применим только OwnerIdentity, полученный из существующего trusted principal. Поддержка второго issuer с пересечением subjects потребует отдельного изменения Flights ownership model, а не допущения данного контракта.

Source ownership подтверждает возможность сохранить личную ссылку, а не актуальность статуса на момент commit. Owner неизменяем в принятом Flights contract; состояние может измениться между чтением и сохранением связи. Не требуется распределённая транзакция: следующая отдельная status read получит новое состояние.

Trips также проверяет ответ порта: exact requested OrderId/OwnerId, положительную source version, допустимый UTC observedAt и bounded shape. Misbound/malformed proof даёт unavailable без сохранения связи или раскрытия payload; доверие к producer не отменяет эту проверку на границе.

### 4. Минимальная карточка, честные статусы и picker

Route summary содержит до 4 legs в исходном порядке. Для каждого leg — origin/destination IATA и departure первого / arrival последнего segment с исходными offsets. Все legs сохраняются; чрезмерный/повреждённый itinerary даёт `routeUnavailable`, а не усечённый успешный маршрут. Не публикуются passengers, контакты, tickets, supplier IDs, деньги, документы или cancellation/refund terms. Party count не вычисляется из числа билетов. Не вводятся derived IANA zones, city-local HH:mm или обещание актуального расписания.

Status response сообщает `bookingStatus`, `reviewRequired`, `streamVersion`, `observedAt` и nullable route. Unresolved creation/confirmation/cancellation/report barriers и expired/неизвестный Held expiry дают reviewRequired; активный статус без warning не выводится из старой проекции. Дополнительно ещё не закрытый/отменённый заказ с `CreationBlocksConfirmation`, включая `CreatedWithDifferences`, всегда требует review: это materialized Held, но `HasUnresolvedCreation` для него false. Нельзя просто скопировать проверку Support. После принятой отмены исходное расхождение не держит вечный warning само по себе; незакрытые cancellation/supplier-report/booking-confirmation barriers остаются самостоятельными основаниями review. Самостоятельный lifecycle клиентского возврата, включая его `NeedsManualReview`/`ManualReviewRequired`, не входит в этот boolean и не импортируется в Trips: детали доступны на странице заказа. `Refunded` подписывается как сохранённый статус Flights, с отсутствием доказательства зачисления денег; ссылка ведёт к деталям. Ни один статус не обещает фактическое движение рейса или свежий опрос поставщика.

Picker использует отдельный minimal projection listing. Порядок — existing BookedAt DESC / AggregateId DESC, 20 элементов + lookahead, offset 0–10000, без total/snapshot guarantee. UI deduplicates IDs и предлагает обновить список; `nextOffset = offset + 20` только при lookahead и допустимом следующем offset, иначе page-bound warning. Fresh hold до проекции может отсутствовать; отсутствие в странице не называется отсутствием заказа. Прямой ввод ID и retry-search нового hold в этом этапе не нужны. Данные picker явно проекционные; attach всегда проверяет source stream заново.

Malformed candidate itinerary не превращается в пустой результат: минимальная owner-scoped запись сохраняет ID/status и `routeUnavailable`. Нарушение envelope/невалидный ID/ошибка списка дают typed unavailable всей страницы. В UI для неизвестного маршрута есть явная подпись, без выдуманных legs.

### 5. HTTP surface и preconditions

Новые маршруты классифицируются до общих M1 guards, включая case/trailing-slash variants. Существующий Trusted JWT owner и no-store policy обязательны.

| Метод / путь | Смысл | Ответ / precondition |
| --- | --- | --- |
| GET `/api/trips/flight-links/availability` | Public configuration для authenticated Trips owner | `{ enabled }`, ≤ 2 KiB; без DB/protection/Flights resolution |
| GET `/api/trips/{tripId}/flight-links` | Сохранённая коллекция, без Flights lookup | `{ tripId, linksRevision, links }`, ETag `"fl-N"` |
| GET `/api/trips/{tripId}/flight-links/status` | Текущие наблюдения для сохранённых ID | `{ tripId, linksRevision, items }`, ≤ 32 KiB; не conditional/cacheable representation metadata |
| GET `/api/trips/{tripId}/flight-order-candidates?offset=N` | Одна bounded страница собственных заказов | ≤ 64 KiB, до 20 items и nullable nextOffset; без query по owner/order/status |
| PATCH `/api/trips/{tripId}/flight-links` | Добавить или убрать один заказ | `Content-Type: application/json`, одна операция и `orderId` в теле до 1 KiB, без query. `If-Match: "fl-N"` обязателен. Ответ 200 содержит представление коллекции в формате GET и её ETag |

Revision N — exact decimal string 0…Int64.MaxValue, без leading zeros кроме `0`. Missing If-Match → 428; malformed/wildcard/weak/multiple/plan ETag → 400; stale links ETag → 412. Missing/foreign plan → identical 404 before any Flights read. Attempt to add absent/foreign order → identical 404. Source/store/protection failure → typed 503; bounded request deadline → typed 504; invalid/unknown/duplicate JSON/query/header input → 400; oversized request → 413; fifth distinct link → typed 409. Все ошибки no-store, без исходного payload. Никакой новой booking scope endpoint не требует и не выдаёт.

Клиент получает ETag через GET и отправляет его на тот же URI коллекции. Тело PATCH — один объект вида `{"op":"add","orderId":"<UUID>"}` или `{"op":"remove","orderId":"<UUID>"}`. Имена и значения `op` регистрозависимы, `orderId` — непустой GUID. Массивы, отсутствующие, лишние и повторяющиеся поля отклоняются с 400 до обращения к Flights. Правила размера и precondition приведены выше.

Новый `add` требует проверки владельца заказа в Flights. Для повторного `add` уже связанного заказа и `remove` отсутствующей связи сервер проверяет CAS и возвращает текущую коллекцию с прежним ETag. `remove` использует только хранилище Trips. Проверка лимита применяется к новому `add`. При записи хранилище повторно проверяет CAS и существование плана. Отказ проверки владельца оставляет коллекцию в прежнем состоянии.

Дедлайн link/candidate/status HTTP use case — 5 s от входа в feature guard, включая body/read/source/store; тот же cancellation token и оставшийся budget передаются вниз. Status делает не более 4 sequential source reads, только для IDs сохранённой коллекции, без фонового fan-out/retry. Readiness и DB command timeout используют конечные бюджеты; не оставлять detached continuation с disposed scope. Timeout не означает, что mutation гарантированно не состоялась.

Status чтение сначала получает одну сохранённую collection revision. При недоступности одного source item возвращается его `unavailable`; foreign/absent materialized source → `notFound`, без приватных полей. Уже завершённые результаты могут быть возвращены с недоступными оставшимися при исчерпании общего бюджета; ошибка самого коллекционного store → общий 503/504. В обоих случаях saved collection не меняется. Наблюдения не являются atomic snapshot всех заказов.

### 6. UI и отдельные состояния редактора

Раздел находится рядом с календарём, вне формы сохранения M1. После загрузки плана и положительной availability клиент читает metadata, а для непустой коллекции — status. Кнопка «Обновить сведения» начинает новое поколение чтения и выполняет те же шаги. После GET metadata клиент сразу применяет состав и ревизию, удаляет карточки исключённых заказов и пропускает status для пустого списка. Ответ status принимается только для текущего поколения, той же `linksRevision` и в точности того же набора ID. Если сервер вернул другую ревизию из-за нового изменения в другой вкладке, клиент отбрасывает этот ответ, помечает сведения как требующие обновления и предлагает снова нажать кнопку. На одно нажатие приходится один metadata GET и максимум один status GET. При подтверждённом `add` ответ PATCH уже содержит сохранённую коллекцию и позволяет отдельно запросить status. Picker открывается по явному действию пользователя.

Минимальный текстовый набросок поведения:

```text
Авиазаказы этой поездки                              2 из 4
LED → SVO   12 июня 09:00 +03:00  → 10:25 +03:00
Заказ: Ticketed · По данным Flights на 11:42
[Открыть заказ] [Убрать из поездки]

SVO → LED   Сведения сейчас недоступны
Связь сохранена. Последнее наблюдение не считается текущим.
[Обновить сведения] [Убрать из поездки]

[Прикрепить авиазаказ]
Связь не меняет даты, участников и бюджет плана.
```

Это behavior sketch для проверки задачи, не утверждённый визуальный дизайн. Existing Flights order page при переходе использует собственные auth/permission rules; Trips не передаёт токен в URL и не выдаёт booking grants. Общий Keycloak SSO может помочь повторному входу, но отсутствие новой проверки входа не обещается. Текущий [FlightsAuthService](../../../../apps/web/src/app/flights/flights-auth.service.ts) запрашивает `flights:book` и передаёт same-origin `/flights/orders/{orderId}` как `redirect_uri` настроенному доверенному Keycloak. Предложение сохраняет этот поток: internal order GUID в этом route/redirect — явное исключение из запрета частных URL-параметров. TripId, содержимое плана, association list, токены и arbitrary return URL туда не добавляются. Новая функция не устраняет текущую потребность Flights UI в scope и не меняет регистрацию OIDC client.

Коллекция имеет отдельные saved/loading/mutating/conflict/unknown-write состояния. Операции доступны на чистом сохранённом плане при отсутствии M1 read/write/uncertainty; dirty calendar не теряется, можно продолжить редактирование, а attach/remove предложат сначала сохранить/разрешить состояние. Сам механизм на сервере не требует plan ETag: календарь и ссылки независимы и изменения другого редактора brief не переинтерпретируют ссылку.

После потерянного mutation ответа клиент сохраняет barrier, не повторяет PATCH и предлагает metadata reread. Чтение доказывает только текущую коллекцию. Следующее изменение возможно после отдельного осознанного действия с новым ETag. UI не заявляет receipt/exactly-once. Double click даёт один запрос.

Replies привязаны к auth identity epoch, TripId, local read generation и linksRevision. Status result принимается только для текущих сохранённых IDs и той же collection revision; для одного ID меньшая streamVersion не заменяет большую. Переданный в status result ID не создаёт ссылку. При смене owner/плана, навигации, logout и удалении очищаются picker, карты, таймеры/requests и unknown state; поздние callbacks их не восстанавливают. После source failure последнее наблюдение можно оставить только с явной stale-подписью; notFound очищает ранее показанные route/status facts этой карты.

Accessible labels, keyboard selection, focus recovery и live announcements берутся из соседних Trips patterns. Только safe relative route `/flights/orders/{validatedGuid}` строится приложением; произвольного href из backend/заметки нет. No browser persistence/SSR transfer/private telemetry.

### 7. Защита relationship в системной диагностике

У PATCH адрес содержит TripId, а тело содержит orderId. Для запросов коллекции Host сохраняет в системных HTTP-логах, метриках и экспортируемых ASP.NET spans только безопасный шаблон маршрута либо применяет узкое исключение этого маршрута из инструментирования. Raw path, query, тело PATCH, заголовок авторизации и идентификаторы ресурсов исключены из диагностики. Это относится также к ошибкам до маршрутизации, выключенной функции и вариантам регистра или завершающего слеша.

Host владеет process-wide pipeline; модуль вносит только узкий contribution через Trips.Api.Composition и существующие ServiceDefaults redaction seams, по образцу [RailModule](../../../../modules/rail/Travel.Modules.Rail.Api/Composition/RailModule.cs). Существующий Rail wrapper нельзя заменять или обходить: contributions должны совместно работать. Не отключать всю Host telemetry и не считать middleware достаточным для RequestStarting, который возникает раньше него. При необходимости добавить общий extensibility seam без зависимости Shared/Host на Trips internals. Конкретный wrapper/enricher — технический выбор реализации, но проверяемый результат на exporter boundary обязателен.

Разрешённый browser/OIDC order route из секции 6 не разрешает экспортировать пару TripId+OrderId в server telemetry. Owner-qualified link requests в реальном Host проверяются capture exporter и logging sink с sentinel IDs/body: до/после routing, success/error/404/timeout, feature off и с уже подключённым Rail contribution. Ни private association, ни токены/order facts не попадают в formatted messages, structured state, scopes и spans.

### 8. Flag, readiness и regression boundaries

`Trips:FlightLinks:Enabled=false` по умолчанию. `Trips:Enabled=false` скрывает всю область до auth/DB. При включённом Trips availability сообщает эффективное значение нового флага без зависимостей; остальные feature routes при выключенном flag дают 404 до auth/source/link-store resolution. Старые link rows сохраняются; обычный DELETE плана продолжает FK cascade.

Миграция входит в существующий Trips EF lifecycle. При включённом Trips проверка готовности сначала проверяет ключи и историю миграций, затем выполняет read-only запрос с явным списком обязательных столбцов обеих таблиц (`WHERE FALSE`). Для таблицы связей это TripId, issuer и subject владельца, ревизия и шифротекст. Отсутствующая таблица или столбец при уже записанной миграции дают `Unhealthy`, даже если в БД пока нет связей. Новый UI-флаг не отменяет проверку схемы версии приложения. Availability продолжает читать только конфигурацию. M1 и readiness не обращаются к Flights для получения сведений о заказах.

В `trips-demo` Chromium работает с Angular и [перехватом запросов Playwright](../../../../tests/travel-e2e/trips-demo/trips-fixture.ts). На вымышленных заказах двух владельцев тесты считают PATCH, моделируют потерю ответа и проверяют действия пользователя. Прохождение через прокси, реальную аутентификацию, Host и БД проверяет отдельный профиль `flight-links` в local-stack. В нём счётчик записей браузера учитывает и PATCH.

Профиль использует существующий helper для схемы Trips в проверенной одноразовой БД. При запуске Development Host штатные инициализаторы готовят Flights EF, Marten и Wolverine до готовности сервисов. Параметр `AutoCreate.None` относится к рабочим сессиям и транспорту. Ответственность за эти схемы остаётся у инициализаторов Host.

После успешной проверки готовности Host test-only seed создаёт вымышленные A/B source events с правильными ownership/creation invariants и передаёт их в штатный Flights reconcile/outbox path. Picker ожидает обычную EF projection, без прямого заполнения итоговых read-model rows. Затем браузер использует реальный candidate GET и выбирает заказ; ещё один source event обновляет состояние через обычный reconcile. Проверки source proof до проекции остаются отдельными интеграционными тестами. Публичный seed endpoint, provider/payment/model вызовы и hidden substitution не добавляются. Это fixture extension отдельного сценария, не изменение frozen Moscow/SPb quality corpora, clocks или itinerary response capture.

## Risks / Trade-offs

- Дополнительный collection ETag увеличивает число состояний → отдельный API/client state и тесты cross-resource ETag rejection; календарный протокол не расширяется.
- Eventual picker может временно не показать новый hold → явная проекционная подпись и ручной refresh, никакого повторного бронирования или отсутствия-order вывода.
- Наблюдение source может устареть сразу после чтения → version/time/source на карточке; link означает пользовательскую связь, не активность брони.
- Offset listing может повторять/пропускать элементы при новых заказах → dedup/refresh и отсутствие snapshot guarantee; cursor redesign Flights вне scope.
- Новая migration обязательна для schema readiness нового build → явная additive SQL review/target provisioning отдельно от feature flag; во время проработки БД не запускается.
- Возможна задержка source query → общий дедлайн, не более 4 source reads и отсутствие retry/background loop; source outage не блокирует обычный план и unlink.
- Контракт ownerSubject предполагает нынешний единственный доверенный issuer → сохранить auth boundary и не объявлять поддержку multi-issuer.
- Binary downgrade может иметь свои schema-compatibility ограничения → основной rollback через flag off в новой версии, прежний binary допускается только после отдельной проверки readiness.
- Существующий Flights login передаёт order GUID доверенному Keycloak → явно сохранить только существующий same-origin return route, без TripId/association data и новых auth grants.
- Standard telemetry может захватить relationship до middleware → узкий composable redaction/exclusion и actual exporter/log-sink тест, а не только проверка custom logger.

## Migration Plan

Это план будущей реализации/публикации, не разрешение на исполнение.

1. После отдельного разрешения на source migration добавить дочернюю таблицу/ограничения и generated SQL; проверить отсутствие backfill/изменений `trip_plans`, старых protected payloads и key purposes. Старые планы получают виртуальную пустую коллекцию при чтении, а не переписываются.
2. Прогнать current/legacy protected snapshot и M1 HTTP/client regressions; проверить FK cascade, concurrent first insert, monotonic empty collection и owner-bound crypto в отдельно разрешённом disposable PostgreSQL.
3. Принять сценарии интерфейса на перехватах Playwright. Затем подтвердить в actual local-stack прохождение PATCH через прокси, Keycloak, Host и БД, обновление списка через штатную проекцию Flights и восстановление после перезапуска. Оба существующих городских профиля сохраняют свои проверки.
4. Target schema/key provisioning и включение требуют отдельного запроса для конкретной среды. Из данного change они не следуют. Production startup остаётся read-only schema validation.
5. Для отката выключить feature flag и оставить совместимую таблицу/данные; не исполнять Down и не удалять связи автоматически. Прежний binary проверять отдельно. Полный rollback данных/схемы — отдельная операция с явным риском потери связей.

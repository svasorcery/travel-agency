# Design

## Context

База: fetched SHA `31f4a1da3571386044946240e957088e5ea21714`, новый managed worktree предыдущего этапа. PR36–38 и выбранный cancellation pilot завершены; весь M3 — нет. [Research](research.md) содержит source/ADR/official contract evidence.

2026-10-07 пользователь выбрал рекомендованный простой путь: ручного разбора может быть больше, расширенную автоматизацию не строить. Решения этой редакции заменяют D1–D9 исходного draft. Затем пользователь отдельно разрешил реализацию; source и локальные результаты отражены в tasks.md/verification.md, delivery остаётся отдельным этапом.

## Goals / Non-Goals

**Goals:** один complete offer/order, 1–9 взрослых, 1–4 legs; optional checked baggage, затем seats; точная сумма/согласие; один create после сохраняемой попытки; простой выход из достоверного расхождения.

**Non-Goals:** post-booking add/replace/remove, принятие замен, per-service refunds, instant/card checkout, customer payout, автоматический поиск потерянных заказов, recovery engine, новый admin UI, durable полный каталог. Существующие auth/PII/historical/CI ограничения сохраняются.

## Decisions

### D1. Один существующий booking flow

Три этапа одного change: (A) минимальная защита существующего hold и исходы; (B) сквозной багаж; (C) места на той же покупке. Каждый этап имеет свои tests; общая приёмка завершает оба вида услуг.

Отдельные ancillary prepare/review/hold endpoints и IFlightAncillaryBookingProvider удалены из проекта плана. Catalog-read capability имеет отдельный IFlightAncillaryProvider; создание, чтение известного заказа и confirmation остаются IFlightBookingProvider/DuffelFlightBookingProvider. 130s budget для create настраивается на отдельном capability HTTP client; остальные Duffel budgets не увеличиваются.

A1: implement hold+services по официальному guide/changelog; старую противоречивую error table сохраняем в research как риск. Unsupported response даёт честный отказ/ручной разбор без instant fallback и без отбрасывания услуг. Source/fakes/CI не означают live supplier acceptance. Контакт с поддержкой, реальные API и rollout не разрешены.

### D2. Временный каталог и одна итоговая котировка

Catalog-read возвращает свежий offer контекст, included allowance по adult/segment, baggage services и optional seat maps. Ничего из полного каталога не записывается в event stream. Первая реализация читает по явному действию без нового cache store; оптимизация временным cache возможна позже по evidence. Ошибка, unsupported и valid empty различаются. Размер supplier response ограничен 8MiB; превышение даёт unavailable, без усечения маршрута/состава.

Public selections используют opaque selectionKey из каталога и quantity в JSON body. Ключ не является разрешением: сервер заново проверяет принадлежность текущему offer/passenger/segment. Supplier wire DTOs остаются Infrastructure. Адрес segment = quoteRevision + legIndex/segmentIndex; в пределах фиксированного полного маршрута этого достаточно, новые UUID для каждого catalog/map/segment не нужны. При supplier calls используются точные текущие supplier refs из нормализованного mapping.

Existing POST /orders/quote получает optional selections. Для непустого списка нужны flights:book и trusted owner; quote с выбранными услугами получает owner вместе с итоговой purchase записью. На claimed stream anonymous/foreign re-quote отказан до provider read. Empty old quote contract остаётся прежним. Quote handler читает актуальные offer/services, считает сумму и возвращает один complete итоговый quote.

Omitted selections на новом quote означает empty; на существующем quote сохраняет ранее выбранные позиции как запрос на перепроверку. Явный пустой массив — удалить все услуги. Отсутствующее/изменённое service identity не отбрасывается молча: запрос требует нового выбора/review. Клиент с новым Purchase сравнивает previous/new grand totals и состав; legacy PriceChanged/base fields не должны сравнивать прежний grand с новым base. Для empty purchase base и grand совпадают.

После BookingCreationStarted **все** quote writers, включая того же owner и старый anonymous маршрут, заморожены до provider read. Current QuoteBinding/itinerary/accepted purchase нельзя менять, пока creation unresolved или known created. Re-quote снова допустим только после положительного NotCreated с завершённым/остановленным sender и без других barriers. Если refresh уже начал supplier read раньше, а start выиграл CAS, refresh не может сохранить новые quote events; обратная гонка делает hold со старой revision недействительным. Known-created outcomes идут через order/confirmation/cancellation, не через re-quote.

В stream сохраняется только BookingPurchaseQuoted: quoteRevision, owner при наличии, base fare, selected lines (scope/quantity/unit/line total/descriptive restrictions), grand total, expiry/notices. OfferQuoted/OfferReQuoted сохраняют прежний payload/значение base fare; новый event идёт с ними в одной транзакции, устанавливает final aggregate total. Historical applier сравнивает base offer по прежнему правилу, затем применяет purchase total. Hold/confirm берут final total, search/ranking продолжают видеть base offer.

Quote expiry берётся из supplier offer. При refresh черновые предпочтения остаются в tab memory: валидные позиции повторно сопоставляются только однозначно, отсутствующие помечаются; новый service key не получает старое согласие. Новый quote revision всегда требует explicit review, но не уничтожает весь ввод/выбор. Никакого локального 120s срока или фонового постоянного refresh. Before hold проверяется current saved quote/revision/expiry; повторный полный catalog read после итоговой котировки не нужен. Если supplier создал другие условия, действует D5 до любых денег.

### D3. Услуги и деньги

Checked bag: quantity integer 1..maximum, один adult и один или несколько segments. Unit считается один раз за весь covered scope. Group/carry-on/unknown products в первом срезе явно unsupported. Included allowances отдельны от purchased services; missing не превращается в zero и старый Max не является нормой на все segments.

Seat: один adult/segment, уникальное физическое место, quantity=1. Passenger-specific service/price; zero-priced seat отправляется как реальная услуга. Maps могут отсутствовать/быть частичными, rows/decks не обязаны быть прямоугольными. Disclosures — безопасно показанные English plain-text strings; согласие на ограничения входит в итоговое review, medical data не собирается и eligibility не выдумывается. Keyboard/text list доступен наряду с map.

Grand total = base fare + сумма unit price × quantity; currency одна, decimal arithmetic, no FX. Amount/quantity/membership серверные; клиент не задаёт оплачиваемую сумму. Offer с неподдерживаемым existing intended-payment/service pricing отклоняется до эффекта, без скрытого actions/price или double counting.

Offer/order segment IDs различны. Однозначное сопоставление complete normalized route/time/cabin + topology связывает order refs с quote адресами; ordinal/route-only не является доказательством. Passenger IDs должны совпасть с отправленными либо иметь документированную linkage; names/position не guessing fallback. Booked service ID отдельный; matching по type, mapped scope, quantity, metadata/designator/restrictions. Booked line amount уже включает quantity: сравнить с selected unit×quantity, не умножать второй раз. Неполная/неоднозначная идентичность — Unknown, не CreatedWithDifferences.

Implementation contract: ReadOrderForBookingAsync получает orderId вместе с accepted BookableOffer, QuoteBinding, BookingPurchase и attemptId. Этот нормализованный Core context нужен для сопоставления parent-scoped refs и проверки сохранённой correlation; нового durable catalog или external DTO за Infrastructure boundary нет.

### D4. Минимальная сохраняемая попытка и HTTP совместимость

Новые domain events: BookingPurchaseQuoted, BookingCreationStarted, BookingCreationObserved. Операторский audit использует существующий BookingOperationReviewRecorded; исторические события не переписываются. Все новые events — past-tense IDomainEvent, replay без decrypt/fresh validation.

BookingCreationStarted хранит owner, stable request identity/digest, quote revision, accepted selected snapshot, protected party, startedAt и dispatch process identity. Это одноразовое разрешение на **возможный** create, а не доказательство отправки. Сбой сразу после commit может требовать ручного разбора даже при фактическом отсутствии POST — сознательный demo компромисс. Lease/timeout не дают право повторить create.

Порядок existing HoldOfferHandler:
1. Auth/target, retained identity replay/conflict, state/revision/expiry и explicit acceptance; protect-before-bus остаётся. Idempotency middleware передаёт endpoint только key/digest metadata, не plaintext bytes. Ciphertext randomness не меняет identity.
2. Проверить/decrypt whole party после guards. Fresh stream CAS сохраняет BookingCreationStarted + один CheckBookingCreation, due startedAt+150s, через existing enrolled outbox. До commit нет supplier POST.
3. Только этот выигравший invocation вызывает existing booking provider один раз с complete party и selected services. Новый background create worker не вводится. Последняя проверка текущего разрешения/expiry — перед send; после возможного send все повторные вызовы идут только в status/review.
4. После start supplier call и completion commit используют bounded operation token, независимый от browser RequestAborted. Create budget 130s. Process failure оставляет сохранённую попытку; никакого fire-and-forget task. Технические cancellation/transport faults не доказывают отсутствие эффекта.
5. BookingCreationObserved сохраняет outcome/evidence. Для установленного created order вместе с ним append OfferHeldV3, coordination marker и reconcile. Actual amount/services принадлежат result snapshot; исходно accepted snapshot остаётся неизменным. Confirmation допускается только при Matches.

Every new hold, including empty-service and old direct command entry points, проходит это же admission. Нельзя обойти barrier другой HTTP key, cache TTL или endpoint. New nonempty purchase требует optional AcceptAncillaries=true вместе с current QuoteRevision; old missing flag допустим только для empty selection.

AcceptAncillaries — C# property, JSON field acceptAncillaries. Сумма/services из клиентского hold body не принимаются: они уже связаны current QuoteRevision в saved purchase.

**HTTP:** existing /orders/hold сохраняет 200 HeldOrderResponse только при Matches с полным provider ID/deadline. Pending/Unknown/NeedsCancellation не маскируются этим DTO и не возвращаются как 202 Held success. Если handler завершился без Matches: 409 typed Flights.HoldOutcomeUnknown или Flights.HeldOrderNeedsCancellation; pre-effect price/expiry validation сохраняет typed отказ. При client 15s timeout UI читает authoritative creation status; backend может закончить позже. Cached old 200 не авторитетнее свежего stream status; frontend не предлагает confirm до проверки текущего результата.

Ordinary POST replay может зависеть от здорового key ring для plaintext ingress; ключенезависимый путь восстановления — metadata-only owner GET. Если middleware replay уже доступен, он остаётся прежним. UI после unknown не повторяет POST вообще. Retained server identity гарантирует отсутствие повторного effect после middleware expiry и независима от новых quote revisions; changed bytes под тем же key конфликтуют.

### D5. Достоверный исход и выход из расхождений

| Outcome | Что установлено | Разрешённый следующий шаг |
|---|---|---|
| InProgress | Возможный create ещё не завершён | GET status, без нового hold/confirm/cancel |
| Matches | Exact owned order, whole party/route/services/total, unpaid deadline и completed creation доказаны | Existing confirm или whole-order cancellation |
| CreatedWithDifferences | Exact owned order и завершение создания доказаны, но услуги/сумма отличаются; order positively unpaid | Показать actual vs accepted; только whole-order cancellation с новым согласием |
| NotCreated | Положительный conclusive no-create result без предшествующей uncertainty, либо operator no-effects + sender quiescence | Завершить попытку, новая явная покупка с новым quote/identity |
| ManualReviewRequired | Identity/completion/финансовые факты недостаточны или исход неизвестен | Privileged review; barrier остаётся |

CreatedWithDifferences — завершённая операция создания, не бесконечная неопределённость. Persist actual owned Held order и его services/amount вместе с результатом; Confirm запрещён, Cancel допустим лишь после positive completion/quiescence и отсутствия confirmation/money blocker. Cancellation scope и UI используют установленный фактический заказ; оригинальное согласие не переписывается и не переносится на замены. Отмена не автоматическая и не обещает бесплатность/возврат исходной суммы. Если supplier cancel unavailable, явно остаётся operator handling.

В модели creation-completed fact и сравнение услуг раздельны: Matches/CreatedWithDifferences — представления известного Created. Более поздний positive read может обновить actual snapshot/comparison через BookingCreationObserved, сохраняя исходное доказательство создания и accepted snapshot. Если pre-wallet confirmation read обнаружил отличия, atomically записать actual comparison и закрыть ещё не начатую финансовую попытку без эффектов; следующий status показывает cancellation-only. После money claim такое обновление не закрывает financial barrier и ведёт в existing manual confirmation review.

Неполное свидетельство «какой-то order существует» не даёт эту ветку. Wrong party/order, uncorrelated resource, неизвестная оплата или возможный старый sender остаются manual. Если uncertainty относится к capture/payment, старые financial barriers сохраняются; новые creation решения их не снимают.

Повторное чтение order после hold перед wallet и после final awaited read перед supplier payment проверяет accepted services/total. Same-price service change не разрешает оплату. После money claim расхождение — existing confirmation manual path; money-only ConfirmBooking не подтверждает услуги и не снимает barrier. Новым service-bearing manual resolutions нужен typed service proof; старый v1 hash/receipt bytes сохраняется при отсутствии нового поля.

### D6. Ограниченное восстановление и ручной разбор

Один retained CheckBookingCreation, due +150s, и один тип worker. Completed result → no-op. Lost/no trustworthy provider ID → ManualReviewRequired без поиска. Если уже записан exact correlated ID и sender-completed/create receipt evidence — один bounded GET (10s) может установить Matches/Differences; прочие результаты остаются manual. After startedAt+180s worker только сохраняет manual без supplier read. Отдельных recovery epochs, slot reservations, paginated provider searches и webhook correlation subsystem нет.

Readonly GET при durable redelivery может повториться; это не mutation. Scoped storage/CAS retries 1/5/30s then DLQ, общий immutable read deadline не продлевается. После DLQ status сообщает delivery problem; GET после deadline может вычислить review-needed состояние даже если worker не завершил local save. Это не факт no-effect. No-store status никогда сам не вызывает supplier. One POST invariant проверяется отдельно от допустимых повторных reads.

Создание с потерянным ответом/ID вручную разбирает оператор. Existing cancellation/confirmation review получает target Creation и минимальные typed outcomes AttachMatches, AttachDifferences, ConfirmNoCreatedOrder, RecordInconclusive. ConfirmNoCreatedOrder требует положительного no-effects/quiescence evidence. Сохраняются existing privileged flights:cancellation-review policy, server actor/time/revision/retained resolution ID; owner не получает review permission, realm/scope assignment не меняются. Историческое название policy/route не повод создавать второй admin workflow.

Positive attachment требует точной attribution к attempt/owner/whole order и sender quiescence, нормализованных actual facts и source SupplierApi либо явно OperatorVerified supplier-support attestation. Кандидатный order ID можно передать только в JSON body; read exact candidate не является автоматическим поиском. Отрицательное закрытие требует affirmative no-created/no-pending evidence и stopped/isolated old sender, не timeout/404. Support notes/raw supplier JSON/PII не сохраняются; только typed evidence и безопасный reference. Не удалось доказать — manual остаётся; автоматический retry/compensation не включается.

### D7. Маленькая HTTP/UI поверхность

- Новый POST /api/flights/orders/ancillaries — authenticated catalog read с aggregateId/quoteRevision/includeSeats в body; owner guard если quote уже claimed, никакого durable catalog.
- Existing POST /api/flights/orders/quote — optional selections, выдаёт один final purchase quote; new selected quote requires auth и records owner.
- Existing POST /api/flights/orders/hold и /confirm — один путь, правила D4/D5.
- Новый GET /api/flights/orders/{aggregateId}/creation — owner-authoritative current attempt, accepted/actual services, outcome/actions; no supplier/no PII.
- Existing /orders/{aggregateId}/cancellation-review и /cancellations/review/resolve — additional Creation target, прежняя operator policy.

Отдельного recent-operation discovery API нет. До hold UI сохраняет existing booking resource route /flights/orders/:aggregateId в адресе; это resource ID, не operation/retry ID. Reload/login возвращает owner status по нему. При полной потере ссылки pending pre-hold не обязан появляться в обычном EF orders list; оператор находит attempt через existing maintenance/catalog tooling. Dedicated self-service discovery — будущая automation feature, не скрытый обход.

Body limits существующего booking API сохраняются (16KiB), selected refs/quantity bounded; metadata response sizes ограничены. Existing DTO/reader compatibility сохраняется для empty services. В новой response часть money строковая/exact; frontend subtotal — небольшой decimal helper, без новой money framework/dependency.

UI: forms → багаж → optional seats → final quote review → hold → existing confirm. Предпочтения только в памяти; refresh не очищает всё, meaningful changes требуют нового согласия. После reload consent/PII draft пустые, server facts читаются. Identity epoch/version guards, no tokens/PII/operation IDs в URL/history/browser storage/logs/traces. Supplier URL/query redaction и RemoveAllLoggers сохраняются.

**Согласованные небольшие улучшения показа (2026-10-07):**

- Компактная сводка по взрослому и перелёту: included allowance, extra baggage/coverage, выбранное место; если данные различаются на connection segments, это видно в деталях. Base fare, extras и grand total показаны отдельно. Повторение coverage одной bag service в нескольких строках не удваивает её стоимость. После reload достаточно «Пассажир 1/2»: новые PII reads/decrypt/name storage ради сводки не добавляются. «Место не выбрано» — нормальное optional состояние, а отсутствующая норма багажа — неизвестно, не zero.
- На changed quote/CreatedWithDifferences показывать короткий список доказанных изменений «выбрано → получено»: seat/designator, baggage quantity/coverage, price/currency. Same-price seat change тоже заметен. Неизвестный результат обозначается отдельно от доказанного отсутствия услуги. Monetary delta допустима только при известных суммах одной currency; нет FX или вычисленного per-service refund. Actions берутся из существующего state: review до hold, cancellation-only для proven unpaid differences, manual для unresolved. Новый экран истории событий не нужен.
- Три воспроизводимых demo presets: purchase-success (багаж+места, Matches, normal confirm), purchase-diff (known completed unpaid order с отличием, owner cancellation по текущим terms), purchase-unknown (потерян результат/ID, no resend, manual). Presets используют те же fictional fixtures и HTTP shapes, что планируемые browser tests. Выбор — в конфигурации existing local demo runner перед новым сценарием; короткая README инструкция задаёт шаги и ожидаемый конец каждого показа. Production build не получает scenario control. Не вводить fault-control API, универсальную панель сбоев, URL/history/storage для operation data или переключение preset посреди активной попытки. UI selector не обязателен: fixtures + инструкция — выбранный дешёвый вариант.

Эти уточнения входят в существующие UI/demo tasks. Полезность показа оценивается тем, можно ли повторить три истории и понять текущий исход/действие; fictional UI proof не подменяет backend auth/durability или supplier acceptance.

В purchase-unknown потерян именно supplier create result: server admission сохранён, но trustworthy supplier order ID не установлен. Потеря только browser response при сохранённом Matches — отдельная уже предусмотренная timeout/reload проверка успешного preset; она читает сохранённый успех и не должна искусственно становиться manual. Так demo не меняет реальные правила восстановления ради выбранной истории.

### D8. Storage, compatibility, delivery

Current booking stream/outbox и existing EF columns достаточны. Pure event applier на new purchase/actual result обновляет total/itinerary/checkpoint; pending quoted stream не materialize как booked order. V1/V2/V3 party protection, old event identities и старые cancellation fingerprints сохраняются. Для новых orders cancellation scope v2 включает actual recorded services; old accepted cancellation остаётся v1.

Проверки: focused pure/loopback/lean HTTP/TS/Angular/fictional demo после startup/fixture audit; Windows .NET последовательно. DB/full Host/Aspire/whole-stack E2E только existing CI. No local Host/AppHost/schema/migrations/keys/deploy, paid APIs/evals, auth weakening и CI/CD edits. Для реализации/публикации отдельная authority; user выбор этой редакции не закрывает product/CI/live acceptance.

## Risks / Trade-offs

- Crash после start до POST может оставить manual даже без эффекта → принято пользователем ради простоты и отсутствия повторного create.
- Потерянный ID/полная потеря ссылки требует оператора → принято; без provider-list search и отдельной discovery системы.
- Услуга пропала или заменена → first slice поддерживает отказ от всей брони через существующую отмену; принятие замен отложено.
- A1 docs discrepancy → implementation assumption + explicit unsupported failure; live proof отдельно.
- Complete current-service proof отсутствует → честный manual, без inventing success/refund.

## Migration Plan

Новых EF migrations/schema не предлагается. В future source registration добавить events/consumer и compatible readers до активации нового writer; все new hold entry points переключаются вместе. Старые create writers дренируются при отдельно разрешённом rollout. После new events rollback только к совместимому binary или forward repair. После product/CI acceptance отдельно разрешённые main-spec sync/archive и docs delivery; сейчас один active change.

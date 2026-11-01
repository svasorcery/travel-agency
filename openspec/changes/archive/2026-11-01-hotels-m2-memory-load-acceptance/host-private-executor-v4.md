# H5 v4 — исправление purchase outcome check

2026-10-31. После разбора H5 v3 владелец сказал «го» на следующий предложенный шаг: source-исправление и проверки сохранённых ответов. Объявлена и сохранена граница: **нового диагностического запуска нет**. Старые failed attempts не меняются.

## Контракт исправления

H5 проверяет принятую source-bound покупку в коротком диагностическом сценарии, а не требует немедленной полной подтверждённой брони. Create допускает Confirmed либо Hotelbeds ReservedAwaitingDetails при одновременных условиях:

- фактический Response/HTTP2xx, правильный jobId, без DriverFailure, завершение≤12s;
- PurchaseAccepted=true, ResponseProvider совпадает с planned source;
- ненулевой planned reservationId совпадает с result ResourceId, Refusal отсутствует;
- ReservedAwaitingDetails допускается только для Hotelbeds; Unknown,Submitted,Quoted/null не принимаются.

Исходный Outcome не переименовывается в Confirmed, Useful=false для промежуточного create не меняется. Полная S/L purchase acceptance продолжает использовать authoritative observations и свои неизменные gates. Ни product state machine, ни provider code не меняются.

После56terminal jobs наt12 остаются прежние требования activeSources0,capacityActive0,pendingInbox0,dueWork0. Future work может оставаться и не обнуляется. Нет продления12s/forced cancellation передP2/повтора/GC/cacheclear. Все timing/data/thresholds из [протокола](host-private-diagnostic-protocol.md) сохранены.

## Проверки реальными сохранёнными данными

Fixture h5-recorded-purchase-replay.json содержит неизменённые результаты HTTP из H5v3 и S-v2, реальные H5control/boundary и S-v2source/metrics observations; provenance хранит26исходныхhashes. Эти данные имеют fictional scope, без токенов или guest text.

Матрица проверяет оба записанных ReservedAwaitingDetails, Confirmed, Unknown/Submitted/Quoted/null, wrong source/resource, missing resource/admission, refusal,late/cancelled/500 и запретpendingдляDuffel. Семь meaningful RED cases воспроизведены до изменения predicate; начальная nullable compile error и погрешность виртуального exact-boundary clock исправлены только в test harness и не выдаются за runtime bugs.

Полный controller replay использует все56реальных S-v2 результатов и35IPC command slots. Исторический S-v2 наt12 уже запускал следующий search66 вне H5prefix; этот неизменённый snapshot остаётся негативным case и обязан блокироватьP2. Положительный unit case явно моделирует idle boundary наt12 с сохранённым quiescent t11 и futureWork17. Это проверка логики, не новый runtime result или подмена историческогоt12.

Дополнительно настоящий in-memory TestServerAPI с active LoadControl.Begin выполняетsearch/rates/prepare/create через actualdispatcher/ownedHotelbedstransport. Он действительно возвращает ReservedAwaitingDetails/PurchaseAcceptedtrue, ровноодин новый Booking, без дополнительных reads; исправленныйH5predicate принимаетэтотрезультат. НетactualTravel.Host/Kestrelport/DB/VMMap.

## Пакет и ограничения

Новый source packet: diagnostic-host-private-v4. Старые v2/v3 raw,sourcearchives иresults сохраняются. ДатыS2026-11-06→08,56jobs/57public/35IPC/3maps иbudgets прежние. Новое выполнение требуетотдельногоразрешения.

Это исправление диагностического исполнителя. Вопрос о категории большого приростаprivate остаётсяunknown доP2. M1failed16/17,S-v2failed22/24,Lnot-run;план6/8 иtasks3.2/3.3pending.

Финальныйsourceрезультат:21/21новыхпроверок,288/288standalone,88H5,0skip;compiledoriginalH5v3/S-v2replyreplaypassed. Freshreview0Critical/0Important/0Minor. Build0errors/1knownVerifywarning;formatter2files. Ранееотложенныйv3empty-regionsminorостаётсязаэтимсрезом. Runtime не выполнялся.


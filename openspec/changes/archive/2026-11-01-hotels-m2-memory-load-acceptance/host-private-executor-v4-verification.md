# H5 v4 — purchase predicate исправлен, пакет готов

2026-10-31. По «го» выполнен предложенный source-срез. **Нового H5/actual Host/PostgreSQL/VMMap запуска не было.** [Исправление](host-private-executor-v4.md), [команды](../../../../TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v4/commands.md).

## Исправление и проверки

Изменён только Expected в HotelsH5Controller относительно frozenv3. Допускается source/resource-bound admitted HotelbedsReservedAwaitingDetails, не переименовывается вConfirmed; wrongsource/resource,refusal,missingadmission,Unknown/Submitted/Quoted/null,cancel/500/late остаютсяотказами. Продуктовыйstate machine иS/Lconfirmationgates не менялись. Наt12по-прежнему обязательны56terminaljobs иquiescence, futureworkнеобнуляется.

- **288/288 standalone tests**,88H5,0skip;21новаяпроверка. Meaningful RED7/19→GREEN. Первые compile/virtual-clock test-harness ошибки не названы product defects; recorded result timestamps не менялись.
- Полный controller replay обрабатывает56реальныхS-v2result facts и35sample slots. РеальныйS-v2t12 сextra search66 корректно запрещаетP2. Успешныйunitcase явно моделирует idle t12 черезrecordedt11/futureWork17 — это не измеренныйruntime.
- Actual in-memory TestServerAPI сLoadControl.Begin проверен черезsearch/rates/prepare/create: ReservedAwaitingDetails/PurchaseAcceptedtrue,oneextraHBXBooking,noextrareads;H5 принимаетего.
- Compiled final predicate дополнительно вызван offline наoriginalH5v3/S-v2job26;обаacceptedByH5=true,исходныйOutcomeсохранён. ЭтотreplayделаетHTTP0/Host0/collector0.
- Все26provenancehashes иданныеfixture сверены;132прежнихrawartifactsнеизменны.
- Fresh independent review: **0Critical/0Important/0Minor**, дополнительныхfix-pass/re-reviewнепотребовалось.
- Build0errors/1knownVerifywarning;formatter2files;manifest4/4;pureevaluator18/18;OpenSpecstrictvalid.
- HistoricalM1offline16/17,workingSetRecovery=false;5legacygatecode sectionsunchanged.

Receipts: `TestResults/hotels-m2-memory-load-acceptance/source-h5-v4/`: purchase-red-v3.trx,purchase-api-green.trx,standalone-before-review.trx,wrapper-before-review.log,original-purchase-replay.json,source-scope.json,prior-integrity.json,review.md,packet-verify.json. После этих full-suite/build receipts код не менялся; повторный запуск тестов только для переименования receipt не выполнялся.

## Frozen packet

`TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v4/`:1982source/input,998build,335runtime+dotnet1,19inputs;архив2983entries/58233260B. Verify-onlypassed,sourceReady=true,executed=false,EULA=true;attempt-1.json/run-01отсутствуют,runtimeAuthorized=false.

| Artifact | SHA256 |
|---|---|
| packet.json |AB2C0FB79A28CF792F2B6EE8B175ABEE29518A1A1FBA71B1731D21F8C8F9A2D1|
| profile-S.json |095C07F8B0FE1C93C00585E1FB1E07AEB695E06494D541D771B3CB4B11A97299|
| build-inventory.json |77016483398FD382F40370A545EF684AFA311041C9BC6F4C8BD4AD61D5B41524|
| source-manifest.json |9BF74D205D3AA7510F2E1217D5015E9BA874EE5D4AE4E27363A20A8BF04FB646|
| freeze.json |B83BFBF77F0A14A35807E4EEFB74339B83D8DFA2CFDE8972A749A2DC40910355|
| frozen-source-build.zip |AF8564069027FB88E331E7EFEA7ACCCD891D53AF34B6DE5CEA2F07D1FF51CAE1|

Sdataset/dates2026-11-06→08,56jobs/57public/35IPC/3maps иbudgets прежние. Новыйэксперимент требуетотдельногоразрешения;старыеattemptsнепереиспользуются.

## Решения и ограничения

1. Сохранённые H5control иS-v2responses/observations используются как offline contract inputs; их объединение не называется одним фактическимпрогоном. Рискрасхожденияреальноготайминга закрываетсятолькобудущимразрешённымH5.
2. ActualS-v2t12неподменён: онnegativecase. Idleboundaryявноunitмодельсреальнымt11,аfutureWorkсохранён;это неwaiverдляt12active/due/inboxguard.
3. Pendingdetails неConfirmed и неM2purchaseacceptance. Sourceготовность не доказывает memoryallocator/leak,WindowsP2,timing/resources/cleanup,suppliers/rollout. Freezeпроверенисполнителемпослеревью.
4. Рабочийbase/веткасохранены;commit/push/publicationнебыло. Ранееотложенныйv3minorстарогоempty-regionsXMLtest остаётсявнеэтогосреза;новыхdeferredminorsнет.

M1failed16/17,S-v2failed22/24,Lnot-run;план6/8,3.2/3.3pending. Причинаприростаprivateunknown;P2ещёнет.

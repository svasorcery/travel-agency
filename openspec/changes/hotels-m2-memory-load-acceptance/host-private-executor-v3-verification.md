# H5 v3: исправления и новый пакет готовы

2026-10-31. Выполнен только разрешённый source-срез. **Нового H5, Host/PostgreSQL или VMMap запуска не было.** [Исправления](host-private-executor-v3.md), [предыдущий failed результат](host-private-diagnostic-v2-result.md), [команды v3](../../../TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v3/commands.md).

## Результат

- XML-reader читает исходный actual P0: отдельные Type/UseType и WS components, верхние и вложенные регионы без двойного счёта; top/leaf PrivateBytes и284расхождения сохраняются раздельно.
- Windows environment восстановлен через минимальный список system/profile folders; SystemDrive/ProgramData присутствуют. Cwd/TEMP/TMP направлены в owned output; relative output отвергается.
- 267/267 permitted standalone tests, включая67H5;0skip. Четыре первоначальные ошибки воспроизведены RED→GREEN, полный CaptureAsync→Read проверен на реальном XML с подменой process.
- Original unmodified P0 отдельно прочитана compiled final reader offline. Raw SHA256 D9AA96F887D7957180168FBC7EE3EDED1490478BA9881F958023AA24D418F491 сохранился. Переносимая fixture скрывает только root Process и Region Details;102588остальных attributes и7897nodes совпадают.
- Fresh independent review:0Critical/1Important/1Minor. Important duplicate-container acceptance воспроизведён тремя RED cases и исправлен одним fix-pass; новая итоговая suite зелёная. Повторного review не было.
- Build:0errors/1known Verify solution-discovery warning. Formatter5files. Manifest tests4/4, pure evaluator18/18(HTTP0/Host0/DBfalse), strict OpenSpec valid.
- Offline historical M1 replay остаётся16/17,workingSetRecovery=false; пять evaluator sections не менялись.
- Все85предыдущих raw artifacts(68historical+17H5v2) сохранились. Старый attempt failed не переписывался.

Receipts: `TestResults/hotels-m2-memory-load-acceptance/source-h5-v3/`. Основные: real-xml-red.trx, review-red.trx, standalone-final.trx, wrapper-final.log, original-P0-replay-final.json, fixture-provenance-check.json, collector-configuration-final.json, prior-integrity.json, review.md, packet-verify.json.

## Frozen v3

`TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v3/`:1979source/input,997build,335runtime+dotnet1,18inputs. Архив2979entries/58183000B. Verify-only:sourceReady=true,executed=false,EULA=true; run-01/attempt-1.json отсутствуют. RuntimeAuthorized=false вfreeze; текущий пользовательский запрет запуска сохраняется.

| Файл | SHA256 |
|---|---|
| packet.json |18A115304925EC87DCC5C732465CFCCEB0D99414D2B7F0BAA72DA078A095F286|
| profile-S.json |D721BFFDCDE6C1F1C62DEEF464B52AE732E49B3A70E822DA14B4B7C17F4D5D44|
| build-inventory.json |C09B337546C98E64125AB541BDA731F7EA574D64CC98408E829A085FA1B52070|
| source-manifest.json |9856A158DD35B87A4DF29C39811BDE19BA44702E76D3F4F47181AAAFBA4EAED1|
| freeze.json |7A0C4CBFCE1BC1919F553B367B01A28F08FF5F77F29894928616A185F110508F|
| frozen-source-build.zip |9AD572942792D21F2A9DD4EDF9DD6C83E8D14A6542C5DFC9B335DDFAF4F1255B|

S data/dates,56jobs/57public/35IPC/3maps,бюджеты и thresholds прежние. Это новый source packet; старую попытку повторно использовать нельзя.

## Решения и оставшиеся границы

1. Original XML остаётся immutable/local, переносимая test fixture редактирует только пути; отдельный raw replay закрывает риск изменения numerical evidence при подготовке fixture.
2. XML parent/leaf PrivateBytes не принудительно уравниваются и не объявляются OSPrivateUsage. Разница остаётся наблюдением с неизвестной точной семантикой, без allocator/leak claim.
3. Windows known paths сохраняются явно; произвольные env secrets/hooks/PATH/symbols не наследуются. Физическое поведение VMMap с исправленным environment ещё не измерялось.
4. Рабочая ветка/base сохраняются; commit/push/publication не выполнялись. Collector PID/readiness-report enhancements остаются вне этого узкого исправления; atomic create-only guard по-прежнему блокирует повтор.
5. P1/P2, physical timing/cleanup и product memory/load/supplier/target/rollout требуют самостоятельного runtime evidence. Source readiness их не заменяет. Freeze проверен исполнителем после review.

**Deferred Minor:** старый no-regions test в HotelH5MapTests не содержит Timestamp/PageTableSize и сейчас отвергается на header guard, раньше проверки пустого списка. Сам empty-region guard сохранён. Это неблокирующее улучшение точности одного regression test, а не незакрытая ошибка runtime; по workflow minor не включён в review fix-pass.

M1 failed16/17; S-v2 failed22/24; Lnot-run. Общий план6/8,3.2/3.3pending. Причина роста private memory по-прежнему unknown; product memory patch не заявляется.

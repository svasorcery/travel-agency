# Task3.1 — подготовка S/L

2026-10-30. Пользователь разрешил source-правки и тесты, явно запретил нагрузочный запуск. S/L подготовлены на existing Host fixture и owned handlers; фактический Host/PostgreSQL/360s workload не запускался. M2 load acceptance ещё отсутствует.

Branch `codex/hotels-m2-memory-load-acceptance`, base/HEAD `c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf`. Изменения в рабочем дереве; stage/commit/push/PR не выполнялись.

## Состав и поведение

| Параметр | S | L |
|---|---:|---:|
| Adults |1|2|
| Properties/source |9|100|
| Reviewed pairs |8|100|
| Groups/leaves |10/18|100/200|
| HBX raw key UTF8 |64B|2048B|
| Timed public jobs |3152|3152|
| Fixed reservation rows |38|38|
| Dates |2026-11-06..08|2026-11-06..08|

Оба профиля используют areaKey=fictional; London остаётся Unsupported/0HTTP. Catalog передаётся реальным fictional request:9/100 sorted IDs. Default[900001], external geolocation, typed-handler/rights/Production guards сохраняются. Catalog/revision фиксируются при composition; изменение после composition закрывает provider.

Обнаружено и исправлено поведение configuration binder: default array мог добавляться к явно заданному каталогу, а empty/null теряли свой смысл. Actual composition regressions проверяют exact9/100, default, null/empty; silent dedup/truncation отсутствует.

Полный private context≤4096B; raw4096B с wrapper отвергается. Сохраняются валюты, неизвестные local obligations и indicative Duffel search. Известные purchase facts получаются через rates/prepare, mapper не менялся.

38rows готовятся обычными Core/protection типами;40Duffel/16HBX setup steps и14effects отделены от timed work. Runtime admission starts0, windows не сбрасываются.17timed chains используют заранее назначенные зависимости и source, без FIFO/fallback/retry.

420cache Put:140small→140large→140small. Small11631B, large1289817B;13large entries=16767621B демонстрируют byte eviction, final128small=1488768B. Synthetic cache-only aliases непокупаемые и служат storage-bound proof; actual S/L purchase/handle paths покрыты отдельными тестами.

Degraded recipe привязан к заранее объявленному source-step ordinal. Проверены162caller abort/18s ignore-cancel,186Limited/Timeout, lost-reply154.5 с одним retained effect и no resend, HBX known-reference→actual read→Confirmed. Controlled clocks не являются latency evidence.

## Проверки и receipts

Все receipts в `TestResults/hotels-m2-memory-load-acceptance/source-3.1/`.

| Проверка | Результат |
|---|---|
| Полный разрешённый standalone Hotels integration |178/178,0skips; standalone-final.trx |
| Hotels unit |79/79; unit-final.trx |
| Architecture |243/243; architecture-final.trx |
| Node source-manifest |4/4; manifest-final.log |
| AI harness |passed; harness-final.log |
| Canonical S/L export |2/2; packet-export.trx |
| Wrapper CLI |compile-only,0errors; wrapper-final.log |
| Pure evaluator |18/18; evaluation-final.json; HTTP0/Host0/DBfalse |
| Original historical evaluator replay |16/17, workingSetRecovery=false;5code sections unchanged |
| Historical hashes |7/7 unchanged; historical-hashes.json |

Tests exclude only `HotelHttpTests` and `HotelDurablePurchaseTests`, которые требуют PostgreSQL. In-memory TestServer/owned wire/controlled-clock HTTP проверки выполнены. Реальный Host, Docker containers, schema/load, provider/model calls не запускались. Read-only Docker metadata использован для фиксации уже имеющегося image. Restore architecture graph — из локального NuGet cache, без package sources; advisory audit не заявляется.

RED/GREEN: catalog9failed→green; actual binder4failed→green; seed/cache/dispatcher; sticky overflow2failed→green. Completion review regressions5failed/6→6/6; binary inventory3failed→3/3; altered ranking2failed→2/2; missing successful source-step evaluator reproduced false green and corrected. Initial compile/test-setup errors retained separately, не считаются product RED.

## Независимое ревью и исправления

Один fresh-context review по executing-plans:0Critical/6Important/0Minor. Исправления одним проходом, затем полный178/178 suite; повторное ревью не заявляется.

1. Evidence write errors больше не обходят cleanup: отдельные best-effort записи, cleanup в гарантированной ветке, failure receipt и failure exit. Ошибка planned-file write также попадает в общий finalization.
2. Terminal acceptance проверяет cancellation и общий1200s deadline после finalization/receipt flush. Cleanup имеет собственные60s, но не разрешает passed после истечения deadline.
3. Полный bounded final-facts.json сохраняет m2Rows/claim/read allowance/known reference/sender-finished; при недостижении facts сохраняется явный unavailable marker.
4. Resource readings сохраняются до проверки stop limit, включая readings при failure. Before/after/1Hz evidence содержит system pressure; отдельные own DB/temp apparent bytes снимаются вместе с cgroup counters. Ошибка сбора делает результат invalid.
5. Обязательный build/runtime inventory проверяется при чтении профиля, в child до Host startup и перед acceptance. Source hash не заменяет DLL/deps/runtimeconfig identity.
6. HealthySearch сверяет ranking explanations/minima/buckets/order с принятой policy; source-specific kind/attempted/reason сохраняются.186 требует Duffel Complete и HBX Failed/Timeout, одного aggregate Limited недостаточно.

Пределы review: реальный runtime, cleanup фактических assets, p95/RSS, supplier/target performance и leak-free claim не проверялись. Final generated hashes проверяются отдельно после review. Исторические instrumentation/v5 evidence не переоценивались.

## Решения и границы

- Canonical OpenSpec + working diff ledger вместо commit/task scripts: stage/commit не разрешены; журнал не удаляется.
- Seed raw owned wires отделены от admission; ошибка такого разделения исказила бы runtime windows, поэтому starts0 проверяется.
- Synthetic cache storage proof отделён от публичного S catalog; сам по себе он не доказывает bookability.
- Canonical hashes относятся к сохранённым JSON specimens, не обещают identical future GUID/expiry/ciphertext bytes; runtime bounds измеряются отдельно.
- Full permitted standalone suite вместо запрещённого Host/DB прогона; source green не закрывает3.2.
- Runtime/target/2GiB/leak-free вопросы из declined-to-judge остаются отдельными gates; подготовка не выдаёт разрешение на них.

## Сохранность

До правок source3.1 сохранены1919v5source entries и996build files:
`TestResults/hotels-m2-memory-load-acceptance/source-3.1/v5-source-build.zip`,
SHA256 `ADAF25C5E992E27E0C071D3F6D105D4970B0DC60D2B548C8ABB092DBEF5CAA3E`.
Raw diagnostic v5 dump/receipt/analysis files не изменялись;7/7SHA256 повторно проверены, v5-evidence-hashes.json. M1 failed16/17 не пересчитан как success и не освобождает M2 от независимой приёмки.

## Frozen packet

Create-only packet: `TestResults/hotels-m2-memory-load-acceptance/load-sl-v1/`.
Verify-only exit0: `prepared=true, executed=false, profiles=[S,L], planned=6304`; run/ отсутствует.
Проверены1964source/input entries,996build files,335runtime files и dotnet host.21declared inputs включают оба профиля,16data JSON,load-plan,local-budget и build inventory.

| Artifact | SHA256 |
|---|---|
| profile-S.json |D5DCDE9CEBC1E544A88A72711D64C6513A5E09118238F276C54E375648EA7664|
| profile-L.json |F5D01BE6CFE6E91F22972D94056564EA6D2FDBABF4C78950794095DBCDE768D1|
| build-inventory.json |73B45FAE6EC49EBCC567FEE499C403117F0C25FEE35BB22F06EEA6382BF84919|
| source-manifest.json |F0E1A10820BE45301593BAFE0E683FED2A56A3448B9F29F565D08A4F6CD73C3F|
| local-budget.json |C0F87AB39FB3AC210808F1E46E6B42825A080F57499E8949E93808E385B600A9|
| freeze.json |77B4C507929A7220B2491E87A31F2F3AA6DA368E568A8CBDEFE5A42AB10141CD|

Pinned PostgreSQL image:
`pgvector/pgvector@sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`.
ImageID имеет тот же sha256; pull запрещён. Windows10.0.19045, SDK10.0.301, runtime10.0.9. Read-only budget snapshot: точные RAM/commit KiB и disk bytes в local-budget.json; свободное место на диске около10.47GB. Stop limits4GiB available RAM/5GiB disk/512MiB evidence — локальный запас, не capacity claim. Snapshot не подменяет live preflight.

Из корня этого worktree, проверка без запуска:

```powershell
dotnet tools/demo/Travel.Hotels.Fixture/bin/Debug/net10.0/Travel.Hotels.Fixture.dll m2-load TestResults/hotels-m2-memory-load-acceptance/load-sl-v1/profile-S.json TestResults/hotels-m2-memory-load-acceptance/load-sl-v1/profile-L.json
```

Будущий task3.2: после отдельного разрешения сверить hashes выше и добавить ровно один `--execute` к этой команде. Это один пакет, по одному S/L, stop-on-failure без retry; Sfailure оставляет Lnot-run. Date guard откажет при check-in≤UTCtoday+1. Source/build/profile изменение требует нового freeze, существующие evidence не перезаписываются.

Host affinity2CPU/RSS1GiB и исходные17families не ослаблены. System/driver/own DB считаются отдельно; whole-contour2GiB, real providers, target readiness и leak-free не заявляются. Task3.1 закрыт source acceptance;3.2–3.3 pending, M2 load ещё not-run. Final strict OpenSpec valid=true/issues=[], apply6/8; git diff --check clean, indexempty, requested-base ancestry сохранена. Повторный packet-verify-final.log exit0/executed=false.



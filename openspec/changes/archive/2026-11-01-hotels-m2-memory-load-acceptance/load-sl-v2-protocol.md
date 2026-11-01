# S/L v2 — timer/error repair protocol

2026-10-30. Разрешена подготовка source и пакета, **нагрузка не разрешена**. Этот protocol уточняет executable/error boundary неизменного [load-plan](load-plan.md). Dataset/catalogue revision остаётсяv1; новый пакет исполнения —v2.

## Неизменные параметры

S:1adult,9properties/source,8pairs,10groups/18leaves,HBX key64B. L:2adults,100properties/source,100pairs,100groups/200leaves,key2048B. Dates2026-11-06..08,seed20261029,areaKey=fictional,38rows,seed40Duffel/16HBXsteps. По3152jobs/360s,6304напакет.16canonical JSON specimens скопированы побайтно изv1.

Original17families/M2additions,95%/p95,strict memory comparisons,HostRSS1GiB/affinity2CPU,source budgets иfault recipes прежние. Safety:availableRAM4GiB/freeDisk5GiB/profileEvidence512MiB;setup120s/timed360s/cleanup60s,campaign1200s. Canonical hashes не обещают identical future GUID/expiry/ciphertext.

## Исполнитель

- WaitUntil повторно проверяет реальный monotonic deadline. Только requested delay округляется вверх с минимумом1ms; observedStarted не округляется/clamp.
- Unexpected job/observer fault сохраняется первым, закрывает admission будущих jobs и отменяет shared run. Supervisor не ждёт uncooperative request; bounded drain/owned cleanup сохраняются.
- Linked dispatch CTS живёт до окончанияcleanup. Shared stop проверяется независимо от cancellation propagation. Raw observers проходят capture-before-cancel.
- Actual dispatcher передаёт DriverFailure при transport/decode/invariant errors, сохраняя уже полученныйHTTPstatus/Response. Обычные429/5xx и expected request cancellation остаются обычными outcomes.
- execution-failures.json/receipt.failureDetail:jobId/kind/due,observedAt/attemptedStart,stage,type,observedHTTPstatus,≤4callsites по≤256chars. Max64errors+overflow; без arbitrarymessage/body/locals. Seal запрещает late rewrite.
- RSS1GiB остаётся acceptance gate, не новым live abort; memory fix не заявляется.

Source tests проверяют короткие real monotonic waits,virtual3152-job S/L schedules,actual owned dispatcher иcancel/error boundaries. Это не Host/DB/load acceptance. Independent review0Critical/3Important/0Minor; все3Important исправлены с6RED→GREENcases.

## Пакет

`TestResults/hotels-m2-memory-load-acceptance/load-sl-v2/`:profiles,data,new source/build/runtime inventories,resource snapshot иэтотprotocol в explicit inputs. Hashes/receipts — [verification](load-sl-v2-verification.md).

Default verify-only:

```powershell
dotnet tools/demo/Travel.Hotels.Fixture/bin/Debug/net10.0/Travel.Hotels.Fixture.dll m2-load TestResults/hotels-m2-memory-load-acceptance/load-sl-v2/profile-S.json TestResults/hotels-m2-memory-load-acceptance/load-sl-v2/profile-L.json
```

Один будущий `--execute` требует отдельного разрешения и сверки hashes/date/resources/отсутствияrun. Stop-on-failure не допускаетLпослеSfailure; retry отсутствует. Изменения frozen source/build/data требуют новогоfreeze. V1failed/invalid иhistoricalM1failed16/17 сохраняются.

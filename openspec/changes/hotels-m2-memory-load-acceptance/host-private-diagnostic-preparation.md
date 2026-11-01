# H5 — подготовка без запуска

Подготовлены [протокол](host-private-diagnostic-protocol.md), portable VMMap 3.4 и точный короткий набор запросов. **Статус: protocol-ready, execution-ready=false.** Короткий режим существующего Host CLI и его тесты ещё не реализованы; пакет не выдаётся за готовую команду эксперимента.

Будущий эксперимент: один owned Host; P0 после подготовки; 20 секунд только IPC-наблюдения; P1; проверка counters; первые 10 секунд S (56 jobs); settle до t=12; P2 только при завершённой работе. Всего35 IPC samples и57public requests с preflight. Три native-карты без full dumps/forcedGC.

Независимый builder check нашёл два вопроса. Зафиксированы точный счёт samples и abort без P2 при non-quiescent t=12. Других существенных вопросов не осталось. Это проверка реализуемости, не runtime proof.

## Проверено сейчас

- 68 прежних raw artifacts сохранили SHA256: M1,diagnostic-v5,load-v1,load-v2.
- Весь source/build/profile inventory S/L-v2 проверен безexecute; исходники и пороги не менялись.
- Read-only verifier:15 frozen inputs,56 workload jobs,35 IPC samples,3maps.
- OpenSpec strict valid;6/8задач.
- Host/DB/collector не запускался;run-01 отсутствует,attempt0.
- VMMap не установлен в систему и не исполнялся; EulaAccepted отсутствует/false. Лицензия от имени пользователя не принималась.
- Получен официальный Microsoft ZIP; проверены bytes/hash/version. Отдельная Authenticode trust verification не заявляется.

## Пакет

`TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v1/`

| Файл | SHA256 |
|---|---|
| freeze.json |2ABF8F2D34F2C9DAED3F41DD6583E77403A6C59A3E9926F0E5AF32CA5A368DFC|
| protocol |71DE0A8DAF61EB66DF0DE341F95856086611269D52169637CEC9C93BD8ACD1C1|
| VMMap.zip |71C9BB73F1BA63F69096CC5AB11AAAD4F1969B060931F19C97F7CA6458CA1C1E|
| vmmap64.exe3.4 |0B3731C524E6BA716F15087D85EAE7E6225B6B51D4AE2FA6C142FF1523F57046|

Проверка только читает файлы; execute mode отсутствует:

```powershell
pwsh -NoProfile -File TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v1/verify-preparation.ps1
```

Дальше требуется ограниченный source-срез, описанный в протоколе, затем source tests/review/freeze и отдельное разрешение runtime. Принятие EULA VMMap владельцем — отдельная предпосылка первого использования инструмента.

S-v2 остаётся22/24;Lnot-run;historicalM1failed16/17. H5 preparation не меняет ни один результат и не устанавливает причину private memory.

## H5 executor source checkpoint — 2026-10-31

Исполнитель H5 реализован по отдельному поручению без нового запуска. Основной сценарий, failure accounting, native collector guards и finalization проверены52H5/252full permitted standalone tests; независимое review0Critical/5Important/0Minor исправлено одним RED/GREEN pass. Новый исполняемый source packet — diagnostic-host-private-v2; старая preparation-v1 и S/L результаты сохранены. См. [H5 executor](host-private-executor.md). Runtime/VMMap XML proof и EULA владельца остаются отдельными; tasks3.2/3.3 открыты,6/8.

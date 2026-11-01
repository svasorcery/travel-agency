# Hotels H4-M2: результат v5 — 2026-10-30

**Один разрешённый запуск завершён, все три снимка разобраны.** Получены8успешных search responses и ожидаемый409 Hotels.Expired;9/9jobs,3/3dumps, exit0. Acquisition/cleanup/final attempt успешны, errors/warnings отсутствуют. Повтор не выполнялся.

Ключевой вывод: после штатного TTL product cache отпускает snapshots, а M2 owned fake handlers продолжают ссылаться на свои rate graphs. Это подтверждено для конкретного пути handler→rates; причина historical M1 RSS failure по-прежнему неизвестна.

## Сравнение снимков

| Наблюдение | A: до поиска | B: после8search | C: послеTTL |
|---|---:|---:|---:|
| Активные cache entries |0|8|0|
| Cache payload bytes |0|91525|0|
| Cache dictionary reachable graph |80B shallow, ссылки null|111132B /2005objects|740B /3objects|
| Duffel fake rates |0|16|16|
| Duffel rates reachable graph |560B /8objects|110946B /1273objects|110946B /1273objects|
| Hotelbeds fake rates |0|8|8|
| Hotelbeds rates reachable graph |560B /8objects|2288B /48objects|2288B /48objects|
| Transport sequence Duffel/HBX |0/0|8/8|8/8|

В C внутренние поля cache dictionary: _count=8, _freeCount=8, значит active Count=0. Нельзя принимать _count за число активных entries. Оставшиеся740B — dictionary и его пустые capacity arrays. В его reachable graph больше нет HotelSearchSnapshot или Cache.Entry objects. Физически встречающиеся в heap старые объекты сами по себе не доказывают удержание cache.

Каждый owner и ссылка на его dictionary проверены в соответствующем dump; addresses не переносились между снимками без повторной проверки. Размеры ObjSize включают shared subgraphs, не являются exclusive retained size и не складываются в RSS. Для A cache отдельный ObjSize не запрашивался: записаны shallow80B и null во всех reference fields. После expired rates transport sequence остался8/8 — дополнительных source dispatch не наблюдается.

В C gcroot -nostacks для обоих handlers и cache вернул0roots. Это ограниченный поиск без stack roots; глобальная GC-root достижимость не установлена, stack-inclusive обход не выполнялся в лимите. Подтверждение относится к конкретным owner→dictionary→value graphs, не к полному live heap, leak-free или native page attribution.

## Время, scope и cleanup

- Frozen script575A9F892DEB9252E2A254FF44EC2ACDABF689C39B99277A4F7F9297F5F77669, profile523530B53BBC50935242E5E6E1DE6665F04FF26FE34B012E4E248E2F9F8C0746. Источники/build/tool/runtime проверены перед выполнением.
- Старт2026-10-30T07:53:10.2305892Z (10:53MSK), завершение07:59:32.1775787Z;381.9642645s, ниже720s. Параметр TTL302s, фактический gap last search response→expired request302.7304936s из-за polling/scheduling. Нет продления по результату или изменения frozen inputs.
- A/B/C собраны на14.8734977/82.3494525/381.7282778s. Первый expiry не пересечён в B.
-331resource samples; max RSS93491200B, max private27742208B. Это контекст standalone процесса с collector page-in, не новый L1/M2 performance result и не весь Host/DB footprint.
- Fixture PID61708 и collectors38592/11064/38848 завершены; cleanup stopped=true, отдельная проверка подтвердила отсутствие PID. Direct-console analyzer sessions завершились exit0; owning shells также отсутствуют.
- Бюджет offline heap analysis:50учтённых commands за588.1326067s; после этого новых SOS-команд не выполнялось. Дальнейшая работа — сохранение уже полученных выводов и отчёт.

Применены existing m2-memory, вымышленные данные, owned typed transports, инвариантный runtime10.0.9. Нет Travel.Host/PostgreSQL/AppHost/AI, внешних supplier/model calls, forced GC, trimming, clearing fake history или изменения clocks. Cleanup ограничен собственными процессами; raw artifacts сохранены.

### Ограничения инструмента анализа

Node REPL оказался недоступен до исполнения кода (kernel-assets path). Первый console-wrapper анализатора не отдавал вывод: его exact owned PID54460 остановлен,2setup commands консервативно вошли в общий50limit. Затем использованы три direct-console session на тех же dumps, без новых runtime/snapshots. Counts: failed transport2 + C19 + A14 + B15 =50.

Первая C subtype enumeration была обрезана output budget инструмента; точный cache owner восстановлен отдельным MT query. Все необходимые owner fields, dictionary counts и reachable-size tables сохранены. Raw terminal transcripts содержат ANSI/echo; рядом сохранены текстовые копии. Это раскрытый предел raw transcript, а не скрытый пересъём или замена данных.

## Решение по исправлению

Продуктовый memory defect этим экспериментом не подтверждён, patch не обоснован. Cache в проверенном пути освободил ссылки на выдачи. Fake history сохраняет реальные в смысле managed graph данные и должна учитываться в footprint будущей нагрузки; чистить её ради green запрещено.

M1 использовал другой fake/Host/workload. Его failed16/17, workingSetRecovery=false и overall passed=false остаются неизменны. Текущий эксперимент не принимает M2 load, не доказывает отсутствие утечки, global live heap recovery или hard2GiB capacity.

Tasks2.2–2.3 закрывают выполнение этого диагностического эксперимента и решение «product patch не обоснован»; общий прогресс5/8. Следующая отдельно разрешаемая часть — source preparation S/L3.1, затем их отдельный load run. Ничего из этого автоматически не запускалось.

## Evidence

Local root: TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/.

| Артефакт | SHA-256 |
|---|---|
| attempt-v5.json |2B095DCE64EA5659EBDEFD5D47FB3AE4556C5860827F520C46CFAFCEEE0AF337|
| run-v5/receipt.json |D3FAB726BD78F09CAB5D8EF976678AC31B9B658CF2EED9B55C3A82BD39AD3FD2|
| run-v5/a-before.dmp |E2088150AE792E71DE07C4C457BA80BAE9147208B7464028FFD21618A1F9F45D|
| run-v5/b-filled.dmp |1F5E75F7FD8BB5F6D5CA5A2EC16D804482C781B6B1804359CCD08240AF3A0A8E|
| run-v5/c-expired.dmp |9AAD8A6D351143E08A16A711087F7FE7D2F55AB0EC1E77FB20D0EF9DB027D6D8|
| run-v5/analysis/result.json |5B94ED30F20BE4BF36E5FECE8F117A9FA6477CD15B9F3C9E934D496CC3858DC5|
| run-v5/analysis/ledger.json |57908C784E0A612193D5AE8A06556CBBA04F16EF71AD563DE30BCDC4E50815D8|

Также сохранены authorization-v5.json, raw job0..8, expired-request.json, resources.ndjson, collector stdout/stderr, analysis/*terminal.raw.txt и cleanup-verification.json. История прежних отказов и baseline v4 сохранена. Frozen design/script не изменены, staging/publication отсутствуют.

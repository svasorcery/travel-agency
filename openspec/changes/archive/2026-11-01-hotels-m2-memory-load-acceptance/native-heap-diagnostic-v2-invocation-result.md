# H6 v2 — единственный CLI вызов остановлен до эксперимента

2026-10-31. После «го дальше» владелец разрешил следующий один H6 v2 запуск с остановкой при ошибке и без повторов. Выполнен один вызов driver; он завершился на проверке аргументов до входа в HotelsH6Runtime. **Ни Host, ни elevated helper, ни WPR recording не запускались. Снимков для анализа нет.**

## Фактический результат

- Driver PID53868: 15:04:19.032–15:04:20.148 UTC (18:04:19–18:04:20 МСК), 1.114s, exit−532462766, forced=false.
- stderr: System.ArgumentException: m2-h6 <absolute-packet.json> [--execute], Program.cs:30.
- Внутренний attempt-1.json и run-01 не созданы. Отсутствие marker не означает разрешение повторить вызов: внешний execution-result.json сохраняет уже выполненную единственную попытку.
- UAC/helper/Host/DB: не запускались; HTTP/IPC/VMMap/heap snapshots/ETL:0. Плановые56jobs не дошли до controller; runtime ledger отсутствует, выдуманный ledger не создавался.
- WPR после отказа: not recording; dotnetIFEO отсутствует. Исходный driver завершился, новых matching fixture processes нет. Cleanup действий не потребовалось, поскольку runtime resources не создавались.

## Причина и ответственность

Ошибка в команде запуска, подготовленной ассистентом. Base directory был собран Join-Path, затем packet.json добавлен строкой с прямым slash. Получился путь с последним фрагментом /packet.json вместо канонического Windows separator.

Frozen CLI проверяет точное равенство args[1] и Path.GetFullPath(args[1]). Оба пути указывают на существующий одинаковый файл, но сравниваемые строки различаются, поэтому guard отклонил вызов. Args count=3 и --execute правильные. Эта причина воспроизведена арифметикой/Path API offline, без повторного запуска driver. Точный аргумент восстановлен из выполненной wrapper-команды; он не извлечён из уже завершившегося процесса. stderr и source guard подтверждают точку отказа.

Это не дефект Hotels/product, не память Host и не доказательство неисправности WPR. Source/build patch не требуется. Нельзя объявлять H6 diagnostic выполненным либо делать новые выводы о private memory.

## Исправленная команда подготовлена, не выполнена

Packet path теперь строится как Path.GetFullPath(Join-Path(base, 'packet.json')). Полный ProcessStartInfo с фиксированными аргументами построен только в памяти: Process.Start не вызывался. Та же frozen CLI-проверка принимает аргументы. [Reviewable arguments](../../../../TestResults/hotels-m2-memory-load-acceptance/diagnostic-h6-native-heap-v2/analysis-v1/corrected-invocation.json).

Для следующего отдельно разрешённого вызова сохранить первые execution-process/result/stdout/stderr, использовать новый invocation-02 для внешних логов. Frozen H6 v2 packet и внутренний single-attempt marker остаются прежними; если до нового разрешения появится attempt/run — не продолжать автоматически. Применимость дат по-прежнему до2026-11-04UTC. Повтор в этом turn не выполнялся.

## Сохранность и evidence

Evidence: TestResults/hotels-m2-memory-load-acceptance/diagnostic-h6-native-heap-v2; разбор в analysis-v1.

- 5 файлов текущего вызова (pre-execution verify и4execution files) сохранены с SHA256.
- 189 исторических raw,2002source/input,998build,335runtime+1dotnet host перепроверены и неизменны.
- Freeze SHA256:0E3DCEF29C5DF9DBCA575C88A567477E528A8810BC14B21E0B9DA805FD0C428B.
- Archive SHA256:54AC52CE7CA6784EF7A294025E679953FB57C479AE898A1B14370E074F6DD713.
- failure-analysis.json — offline guard proof; corrected-invocation.json — команда без Start; cleanup.json/wpr-status-after.txt — отсутствие runtime effects; integrity.json — source/build/runtime/history/current-file hashes.

Пороги, source и пакет не менялись. Никаких повторов, новых captures, forcedGC, cache clearing, symbols/download/upload, provider/model calls, stage/commit/push. M1failed16/17, S-v2failed22/24, Lnot-run, tasks6/8 остаются прежними. Причина native heap growth из H5 v4 остаётся unknown.
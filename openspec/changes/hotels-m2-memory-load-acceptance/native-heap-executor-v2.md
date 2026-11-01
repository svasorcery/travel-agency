# H6 v2 — исполнитель, source preparation

2026-10-31. Разрешены установка официального WPA и реализация/проверка H6; новый эксперимент запрещён. Этот документ дополняет frozen native-heap-attribution-protocol.md, не заменяет его и не меняет H5/S/L данные или пороги.

WPA установлен из Microsoft Store (9N0W1B2BXGNZ): package11.8.423.0, CLI11.8.423.12582. Найдены WPAExporter и native perf_heapsnapshot.dll. WPR остаётся встроенным10.0.19041.6093 с прежним SHA256. Ничего не записывалось; exporter help и read-only ETW enumeration не являются ETL qualification.

## Исполнение и границы

Обычный m2-h6 проверяет source/build/.NET/VMMap/WPR/WPA hashes, дату и ресурсные границы. Без --execute это только verify-only. Execute создаёт atomic attempt-1.json перед UAC; второй attempt/перезапуск запрещён. Обычный driver/Host не повышают права. Только m2-h6-helper запускается через один RunAs, с ожиданием до30s и проверкой OS peer PID. Отказ/неопределённый UAC завершает попытку без Host; поздний helper не сможет подключиться к закрытой pipe и начать сбор.

Имена pipe/instance содержат одноразовый GUID. Helper принимает только attach/A/B/finish/release с монотонным номером и строгой JSON schema; target задаётся только в attach. Root/build/runtime берутся из замороженного пакета. Проверяются PID/start/parent/user/elevation/image/точные аргументы driver и Host; удерживаются исходные process handles. Команды WPR фиксированы в HotelsH6Trace; shell strings, произвольные executable/path/PID, третий snapshot и retry отсутствуют.

Windows CurrentUserOnly не применяется между разными уровнями elevation: ACL разрешает пользователя драйвера и Administrators, первый server instance эксклюзивен, peer PID подтверждён ОС. Elevated client использует Identification, поэтому обычный pipe server не может impersonate его с правами администратора. Native NtQueryInformationProcess layout проверяется только на текущей Windows/x64; неподдержанная форма заканчивается отказом, не ослаблением проверки.

Elevated output создаётся атомарно с защищённым ACL в Windows/Temp/TravelHotelsH6-<nonce>. Владелец Administrators; Admin/System full access, обычный пользователь только read/execute. Root удерживается handle без delete-sharing, reparse points запрещены. Никакой privileged write в пользовательский worktree. После закрытия tracing обычный driver копирует raw файлы в run-01/trace, сверяет fingerprint имён/байтов и подтверждает release; только тогда helper удаляет точный собственный каталог. При обрыве IPC/копии/ack каталог сохраняется с emergency-cleanup receipt, cleanup не объявляется успешным. Путь и nonce заранее записаны в helper-intent.json; не удалять такой остаток автоматически.

Фиксированный H5 controller расширен optional hooks: attach до prepare, A до P1, B после quiescent t12 до P2, затем один post-capture IPC. H5 без hooks сохраняет35IPC; H6 ожидает36. Postcomparison сравнивает источники, эффекты, retained counts, admission, inbox/due/future-work; memory/observer timings/events drain не маскируют бизнес-изменения. Contamination сохраняет acquisition, но comparison=false/unknown.

Общий600s: measurement прекращается к480s с учётом elevation; setup≤120, каждый snapshot/map≤30. Cleanup оставляет118s после2s drain: trace/copy branch≤60, затем Host с оставшимся сроком. Disable15/stop45 являются верхними пределами, а не дополнительными независимыми окнами; резерв заканчивается — дальнейшее копирование/cleanup может быть incomplete. Ошибка trace cleanup не пропускает Host cleanup. Ошибка вспомогательных before/after memory counters при cleanup записывается отдельно и не блокирует disable/stop/cancel. При смерти driver helper сам заканчивает tracing; несовпадение/exit target запрещает действие по reused PID, named ETW cleanup выполняется отдельно.

FreeRAM4GiB/freeDisk5GiB/суммарные owned run+trace-temp512MiB неизменны; временная дублирующая копия ETL тоже учитывается. До создания каждой копии проверяется projected combined size, затем ресурсы и cancellation проверяются между блоками1MiB. Helper контролирует ресурсы каждые500ms, WPR wait каждые250ms. При cleanup низкий ресурс не мешает остановке ETW. Trace stop только для своего instance; cancel единожды лишь при подтверждённой активности, unknown не считается absent. Не трогать чужой WPR/IFEO.

## Что означает результат

AcquisitionComplete означает исполнение двух snapshot-команд, трёх XML,36IPC,56jobs и закрытие owned ресурсов с сохранением ETL. **AnalysisComplete=false** до разбора реального ETL официальным анализатором: ровно два target snapshots, PID/start/bitness, event/buffer loss, completeness и unknown-stack coverage. Командный exit0 и непустой ETL не доказывают пригодность allocation данных.

Анализ обычным пользователем, ETL локально, без автоматических symbols/download/upload. Начать с WPA native Heap Snapshot table и module+offset/stack IP; реальные имена колонок/экспорт задаются после открытия настоящей трассы, не выдумываются. -symbols не используется. Required fields и запрет трактовать commit-minus-tracked как free остаются в исходном протоколе. Source tests/fake WPR подтверждают логику исполнителя, а не физический Windows HeapSnapshot.

Будущий пакет diagnostic-h6-native-heap-v2 содержит новый source/build/runtime/tool freeze и verify-only receipt. До отдельного явного разрешения не запускать --execute, helper, Host, DB, VMMap или ETW recording. План остаётся6/8; M1failed16/17, S-v2failed22/24, Lnot-run.

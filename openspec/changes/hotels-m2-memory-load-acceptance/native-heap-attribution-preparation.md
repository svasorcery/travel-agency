# H6 — подготовленный протокол и следующий допуск

Подготовлен [протокол H6](native-heap-attribution-protocol.md) для следующего вопроса после успешного H5: какие живые native allocations и стеки связаны с выросшим heap. Его read-only builder check не оставил существенных вопросов для scope подготовки.

Выбран WPR HeapSnapshot по PID. Будущий сценарий: один обычный Host, один ограниченный elevated collector helper, два WPR snapshots, три VMMap-карты и прежние56запросов. Добавляется один post-capture sample:36IPC вместо35. Этот новый протокол не изменяет H5/S-L результаты и пороги.

Что проверено сейчас:

- Встроенный WPR10.0.19041.6093 поддерживает PID snapshot, named instance и HeapSnapshot.Verbose.File; профиль использует40MiB buffers.
- Read-only queries показывают отсутствие WPR recording и WPR-named sessions; heap/snapshot tracing dotnet.exe disabled, IFEO key отсутствует.
- Сессия не имеет administrator token. WPA, Debugging Tools и готовый ETL reader здесь не установлены.
- Все189историческихrawфайлов и H5v4 source/build/runtime/tool inventory сохранились.
- Ничего не устанавливалось; enable/start/snapshot/stop/cancel, Host/DB/VMMap и новый workload не выполнялись. Source code не менялся.

**Статус: protocol-ready, execution-ready=false.** Это не готовая команда эксперимента. Ещё нужны установка анализатора, реализация ограниченного collector helper/driver hooks, source tests/review и freeze конкретных версий, затем отдельное разрешение одного runtime.

Важные решения исполнителя:

1. PID-mode вместо IFEO по имени dotnet.exe; остальные dotnet не инструментируются по heap. Built-in профиль всё же содержит общесистемные process/loader metadata — это раскрытая часть будущего разрешения; ETL остаётся локально.
2. Host остаётся обычным пользователем, повышаются права только узкого collector helper. Повышение всего Host не является fallback.
3. Старые адреса/rawId131073 не используются как идентичность нового heap. Нужна связь по новым address ranges и allocation addresses.
4. Неучтённый остаток между commit и tracked size не называется free. Старые/untracked allocations и unknown stacks остаются явными ограничениями; точный free/metadata размер может потребовать другого метода.
5. Автоматических symbol downloads, установок, registry/name-mode, нового общего profiler framework и дополнительных запусков нет.

Следующий конкретный внешний шаг — установка **Microsoft Windows Performance Analyzer** из [официального Microsoft Store](https://apps.microsoft.com/detail/9n0w1b2bxgnz) либо компонента Windows Performance Toolkit официального ADK. [Microsoft WPA documentation](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-analyzer). После установки надо проверить actual version, HeapSnapshot view/export, symbol defaults и зафиксировать инструмент перед source integration. Установка не разрешает запись ETW или нагрузку. Для будущего recording отдельно понадобится UAC/elevated collector approval.

Пакет подготовки: `TestResults/hotels-m2-memory-load-acceptance/diagnostic-h6-native-heap-v1/` — help/profile/status/toolchain receipts, workload, preserved-results, builder-check, preparation/verification и freeze. Он не содержит execute script.

H5v4 остаётся complete diagnostic; M1failed16/17,S-v2failed22/24,Lnot-run. Общий план6/8. Product memory patch пока не обоснован.
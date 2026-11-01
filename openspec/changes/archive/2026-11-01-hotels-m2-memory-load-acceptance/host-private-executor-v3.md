# H5 v3 — исправление reader и окружения коллектора

2026-10-31. По поручению владельца исправляются две ошибки [единственного H5 v2](host-private-diagnostic-v2-result.md); новый runtime явно не разрешён. Product code, S/L data/thresholds, H5 schedule и budgets не меняются.

## Source patch

HotelsH5Map.Read читает actual VMMap3.4 root/version/PID, snapshot Timestamp/PageTableSize и все Region. Type и numeric UseType сохраняются отдельно. WorkingSetBytes = PrivateWS + ShareableWS; SharedWS хранится отдельно как subset, без повторного сложения. DTD/external entities запрещены, XML64MiB/depth16/regions200000 ограничены до материализации.

Агрегат категорий использует только верхние regions. Все дочерние regions тоже валидируются. Parent/child Size/Commit/WS components должны сходиться; известное из настоящей P0 расхождение PrivateBytes не скрывается и не исправляется: сохраняются top total, leaf total и число отличающихся parents. Это не равенство OS PrivateUsage и не доказательство конкретного allocator.

StartInfo сохраняет только необходимые Windows system/profile paths через известные папки: SystemRoot/WINDIR/SystemDrive/ProgramData/USERPROFILE/LOCALAPPDATA/APPDATA. WorkingDirectory, TEMP и TMP — directory абсолютного owned XML output. Относительный output отвергается. PATH, runtime hooks, symbols и произвольные credentials не наследуются; EULA автоматически не принимается. Сам StartInfo не запускает процесс и не создаёт каталоги.

## Replay и проверка

В тестах используется сжатая копия настоящей P0: удалены только root Process path и Region Details. Иерархия и все102588 остальных attributes в7897XMLnodes сравнены с оригиналом без отличий. Raw P0 сSHA D9AA96F887D7957180168FBC7EE3EDED1490478BA9881F958023AA24D418F491 остаётся immutable и дополнительно читается новой compiled реализацией offline.

Source replay ожидает2420top/7891all/6083leaf regions,703844352Commit,267030528topPrivate,268611584leafPrivate,284private mismatches и393240576WorkingSet. Есть producer-to-consumer test всей CaptureAsync до Read с реальным XML и подменённым collector process; Process.Start не вызывается. Malformed/schema/PID/depth/counter/output/cancellation/timeout guards проверяются отдельно.

Четыре воспроизводящие проверки сначала RED; семь invalid-input refusals оставались GREEN. Первый полный разрешённый набор264/264 (64H5),0skip. Нового Host/DB/VMMap/нагрузки/карты не было. Test results: TestResults/hotels-m2-memory-load-acceptance/source-h5-v3/.

## Пакет и границы результата

Новый packet — diagnostic-host-private-v3; новая source/build freeze. Профиль S, check-in2026-11-06/check-out2026-11-08,56warmup jobs/57public/35IPC/3maps, setup120/control20/settle12/total600 и safety floors прежние. Old v2 attempt остаётся failed; raw P0 и шесть служебных файлов сохраняются. Внешний API budget0, publication/rollout отсутствуют.

Исправляется source-контракт на уже сохранённом actual XML и Windows launch configuration. Физическое поведение VMMap с новым environment, время, P1/P2 и cleanup ещё требуют отдельно разрешённого запуска. Collector identity/readiness наблюдения из прошлого отчёта не выдаются за выполненные отдельные enhancements: consumed attempt по-прежнему блокируется atomic CreateNew; verify-only служит проверкой inputs, а не новой runtime authority.

M1 failed16/17;S-v2 failed22/24;Lnot-run; общий план6/8,3.2/3.3pending. Product memory fix по-прежнему не обоснован. После source completion — один независимый review и новый freeze, без исполнения.

Независимое review:0Critical/1Important/1Minor. Important duplicate containers исправлен по3RED-регрессиям: ровно один Snapshots и MemoryRegions обязателен. Minor отложен: старый no-regions тест требует valid snapshot headers, чтобы проверять именно empty-region guard; сам guard сохранён. Один fix-pass, повторного review нет.


Финальный source результат:267/267 standalone,67H5,0skip. Original rawP0 replay прошёл compiled reader; новую VMMap не вызывали. Значимые review замечания исправлены,1Minor записан выше. Source preparation завершена; новый freeze описывается отдельным verification receipt.


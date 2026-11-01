# H5 — исполнитель и source preparation

2026-10-31. Пользователь разрешил реализацию и тесты, явно запретил новый эксперимент. Это дополнение к immutable [H5 protocol](host-private-diagnostic-protocol.md); protocol/tool preparation v1 сохранена.

## Реализация

Команда `m2-h5 <packet.json>` только проверяет source/build/runtime/tool hashes, точный prefix S, даты, paths и safety resources, читает EulaAccepted без изменения registry. Она не запускает дочерние процессы. `--execute` предусмотрен только для будущего отдельного разрешения.

Новый исполняемый пакет размещается в `TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v2/`, output `run-01`, atomic `attempt-1.json`. Перенос output в новый packet сохраняет v1 freeze; метод и данные не изменены. H5 использует S child profile с прежним 360s guard, но не вызывает facts и не устанавливает Completed=true.

Точный порядок: setup/preflight (1 GET и 1 sample), P0, 20 control samples за 20s, P1, boundary sample, begin, 56 original S jobs с Due<10, samples 0..12, P2 только при 56 ожидаемых outcomes и нулевых active sources/pendingInbox/dueWork. Future work сохраняется. Source starts/effects/admission в control не должны изменяться. Всего35 IPC samples,57 public requests,3 карты.

Переиспользуются HotelsL1HostProcess, HotelsM2LoadDispatch, HotelsM2Execution, HotelsLoadTiming и ledger. Одна монотонная эпоха parent/child/dispatch. Full S/L thresholds и historical acceptance не меняются.

## Отказы и границы

Ошибка observer/dispatcher, неожиданный HTTP/semantic outcome, late sample, non-quiescent t=12, wrong PID/start time/root/binary/manifest, changed tool, XML/collector error либо safety limit прекращают дальнейшие шаги. Полный denominator56 остаётся в receipt. Никаких повторов/продления settle/GC/очистки cache/fake. Owned cleanup и его ошибка независимы от acquisition; проверяются exact container ID/temp path и expected child exit1. Receipt и финальный attempt должны оба подтверждать acquisition/cleanup/finalization.

Работа до cleanup ограничена540s, cleanup/drain60s, полный attempt600s. Setup120s, каждая карта30s. RAM4GiB/disk5GiB/evidence512MiB прежние; отдельный XML cap64MiB и stdout/stderr8192characters. Collector hidden, numeric owned PID, без -accepteula/символьных серверов и без shell interpolation.

## XML qualification

XML parser запрещает DTD/external entities, требует pinned VMMAP schema marker/version/PID/snapshot/regions и различает commit/private/working set. Raw category IDs сохраняются без догадки о названии; неизвестная категория не становится нулём. Native map raw files остаются для последующего разбора.

Экспортные field names проверены статическим чтением строк pinned VMMap3.4. **Actual XML schema/runtime compatibility ещё не проверена**, потому что VMMap и эксперимент не запускались. Synthetic XML tests проверяют логику reader, а не доказывают реальную совместимость; unsupported schema даст incomplete и сохранит raw XML. Категории не являются allocator stack или доказательством leak.

## Проверки

Первый полный набор:46 H5 cases и246/246 full permitted standalone Hotels,0skip. RED controller19cases, map10cases, collector10cases; отдельный aborted slow virtual-time test и build-lock failure сохранены как технические неудачи, не runtime experiments. Финальные review/freeze receipts добавляются после проверки.

M1 failed16/17, S-v2 failed22/24, Lnot-run. План6/8; tasks3.2/3.3 остаются открыты. Source readiness не означает runtime authorization, M2 acceptance или rollout.

Независимое source review:0Critical/5Important/0Minor; шесть регрессий сначала RED. Исправлены неизвестная очистка при failed startup, сохранение partial operations, actual attempted/observed preflight counters, cancel/deadline после durable marker write и независимый raw-integrity даже при identity/receipt failure. Все решения и исключённые runtime claims записаны в source-h5-v1/review.md и ledger. Повторное review не запускалось; fix-pass проверяется тестами.

До отдельного запуска не подтверждены: фактический XML VMMap, Windows timing и реальная очистка. EULA не принималась. Эти ограничения не блокируют source preparation, но запрещают выдавать её за runtime proof.
Финальные source tests:252/252,52H5,0skip. Шесть review-регрессий RED→GREEN. Deferred minors:0. Runtime не выполнялся. Подготовленный пакет и команды: diagnostic-host-private-v2/commands.md.


# H6 v2 — независимое source review

2026-10-31. Один fresh-context review gpt-6-astra/high по executing-plans. Read-only; reviewer не запускал tests/build/Host/DB/UAC/WPR/VMMap, не менял checkout и не делегировал. Base/HEAD c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf; index пуст. H5 hooks сравнены с immutable H5v4 ZIP AF8564069027FB88E331E7EFEA7ACCCD891D53AF34B6DE5CEA2F07D1FF51CAE1.

Вердикт:0Critical/2Important/1Minor. Два Important исправлены автором за один проход, повторного ревью не было.

1. CopyToAsync проверял512MiB только до/после целого ETL и мог создать заведомо слишком большую дублирующую копию. HotelsH6Copy теперь проверяет projected combined bytes до создания файла, копирует блоками1MiB, проверяет оставшийся бюджет/ресурсы/cancellation до и после блоков. При отказе исходная protected trace сохраняется; ack/release не выполняется.
2. Необязательный L1ProcessMemory.Capture до try мог прервать disable/stop/cancel. Вызов системной команды проходит через HotelsH6CommandObservation: ошибки before/after counters при cleanup становятся отдельной observation error и не блокируют команду. Identity/PID guards не ослаблены.

Regression:6RED+1ужеGREEN →7/7GREEN. Проверены projected duplication refusal без output, сокращение доступного бюджета во время копирования, cancellation между блоками, ошибка observer перед disable/stop/cancel и сохранение stop-on-observation-error для измерения. Затем полная permitted standalone suite351/351,0skipped (63H6); финальная fixture build подтверждается отдельным log. Source compilation ещё не runtime qualification.

Отложенный Minor: timeout/cancellation/output-bound command failure может оставить stdout/stderr=null, хотя reader уже успел получить часть вывода. Fixed command/identity/timestamps/exception type/частичная raw trace сохраняются, cleanup не блокируется. Следствие — менее подробное объяснение ошибки единственного запуска; автоматический повтор не разрешён. Minor не входил в единственный fix pass. Унаследованный H5 XML empty-region test minor остаётся вне H6 slice.

## Все границы, которые reviewer не квалифицировал

Исполнитель принимает эти границы как отдельные runtime/acceptance gates; это не waiver и не дополнительное разрешение:

- Реальный UAC и cross-elevation pipe: тесты ordinary process доказывают framing/peerAPI, а не будущую elevated коммуникацию.
- PID HeapSnapshot/named instance на установленном Windows/WPR: fake commands не доказывают физический сбор.
- Elevated создание/наследованиеACL/удаление private trace directory: конструкция проверена, реальный elevated access не выполнялся.
- PID-exit/reuse и driver-death races внутри OS calls: source guards проверены, OS fault injection не выполнялась.
- Изоляция от чужих ETW и полнота elevated enumeration: read-only native ABI test обычного пользователя не доказывает полный elevated обзор.
- Реальные задержки UAC/WPR/VMMap/copy/cleanup и600s: требуют измерения.
- Пики RAM/disk/buffers/ETL merge overhead: требуют измерения, source guard не обещает будущий peak.
- ETL: ровно2snapshots,targetPID/start/bitness,loss/partial/unknown-stack coverage пока нет; analysisComplete=false.
- Реальные WPA table/export columns и symbol/network behaviour требуют настоящегоETL; schema и symbols не выдумывались.
- Heap owner/leak,busy/free,untracked residual и allocation-to-range mapping — будущие выводы анализа, не исходников.
- Полный повторный аудит старыхH1–H5/M1/M2 и прежнийXML-test minor вне текущего source slice; изменённые H5 hooks проверены.
-189исторических raw hashes проверены исполнителем отдельно; reviewer не заявлял эту работу своей.
- Финальныйpacket/archive/verify-only фиксируются после исправлений, отдельными receipts.
- Build/tests проверены исполнителем; CI/real suppliers/M2load/target/rollout не принимаются этим review.

Rulings: сохраняется privileged helper только для fixed WPR protocol, обычный Host и защищённая передача файлов; unsupported OS/identity/schema всегда отказ. Стоимость: первый отдельно разрешённый runtime может закончиться incomplete, даже при зелёных source tests. Анализ реального ETL отдельно от acquisition — стоимость: успешный сбор ещё не устанавливает root cause. Исторические ошибки/пороги/6из8taskstatus неизменны.

# H6 v3 — проверка scoped metadata fix

2026-10-31. После source-ready исправления владелец сказал «го дальше». Следующий шаг — подготовить и выполнить один тот же короткий H6 для измерения эффекта, затем разобрать файлы в фоне. Не повторять успешный runtime автоматически; собственную ошибку запуска исправлять автономно, сохраняя каждый вызов.

## Единственное исследуемое изменение

Host использует ScopedMetadataAssemblyGenerator вместо upstream IAssemblyGenerator; metadata принадлежит одному Emit и явно Dispose после него. Source-fix/review: compiler-metadata-lifetime-verification.md, compiler-metadata-lifetime-review.md. Пройдены13compiler/243architecture/351Hotels tests и18pure assertions. Новый compiler не означает принятого RSS эффекта.

Executor/protocol тот же, что H6v2: normal driver/Host, fixed elevated WPR helper, PID/start/parent/root/build binding,20control samples, A передP1,B послеquiescentt12 передP2, одинpostcapture sample и независимая очистка.56jobs/57public/36IPC/3VMMap/2heap snapshots/1ETL,однаactualattempt. Стоп при ошибке; изменения source/retained/future-work помечают сравнение contaminated. Все исходные budgets/guards/thresholds неизменны:600s total,30s UAC,120s setup,30s snapshot/map,120s cleanup,4GiB RAM,5GiB disk,512MiB run+trace-temp.

Тот же S dataset/check-in2026-11-06/check-out2026-11-08; без автоматического сдвига, запуск до2026-11-04UTC. Нет L/S360s acceptance, forcedGC/cacheclear/provider/model/paid calls или extra snapshots. UAC нужен только WPRhelper; builtin ETL содержит общий process/loader/system metadata и остаётся локально.

## Baseline и оценка

Сохранить H6v2 raw/ETL и272историческихraw. Baseline ETL SHA3E364944CFC02FF6ECDEF9BF8B8B05D764B327F7E15886AE3EC49E4F2D056EAB.
Baseline: OSprivate delta1005473792B,HeapPrivate981176320B,tracked allocation delta978471418B; двеLocalAllocцепочки965104192B с одинаковымDLL-size signature. Исходные stackIDs241/2120,IP/PID/heap handles не переносить в новыйпроцесс.

Послеv3: подтвердить denominator/retained/sourcevector и2целевыхполныхsnapshots; сравнить OS/VMMap/trackedbytes иDLL-size allocation signature, отдельно observers/timing/managedGC. Сравнение cold processes с одной source intervention не обещает точную причинную долю всех остальных allocator/OS эффектов. Снижение native allocations иRSS — измерение этого H6, а не M2load acceptance и не гарантированная долгосрочная leak absence.

## Анализ без UI

Использовать уже проверенный официальный TraceProcessor1.12.10 offline reader, pin source/DLL/native-tool hashes вanalysis-tools.json. AllowLostEvents=false/AllowTimeInversion=false; не вызывать LoadSymbolsAsync, не загружать PDB. UseSymbols читает только имеющиеся ETL сведения. WPA окна не открывать. Все analysis commands фоновыми bounded process с BelowNormal priority.

Raw capture receipt не переписывать после анализа; отдельные result/analysis/cleanup/integrity receipts. Missing process CreateTime вETL остаётсяunknown, runtimePID/start binding иparent/time проверяются совместно. Даты/source/build/tools фиксируются доrun. Старыйv2 архив неизменен; новыйsource/buildarchive хранится отдельно.
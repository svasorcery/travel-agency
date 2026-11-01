# Исправление lifetime metadata — source-ready

2026-10-31. Проверка lifetime завершена, точечное исправление внесено и проверено. **Host теперь владеет Roslyn metadata в пределах одной компиляции и освобождает её сразу после Emit, включая ошибки.** Нового нагрузочного/диагностического запуска, БД, окон или ETW не было. Реальное снижение RSS пока не измерено; план остаётся6/8.

## Что подтвердилось

В используемом Roslyn5.0.0 MetadataReference.CreateFromFile заранее читает весь файл DLL в native heap. Освобождение такого image зависит от недостижимости reference и GC. Публичный GetMetadata возвращает невладеющую копию: её Dispose не освобождает исходный image. Рекомендованный ownership API — отдельный AssemblyMetadata и созданные из него references. Это прямо описано в [исходнике точной версии](https://github.com/dotnet/roslyn/blob/6c4a46a31302167b425d5e0a31ea83c9a9aa1d09/src/Compilers/Core/Portable/MetadataReference/MetadataReference.cs).

Три characterization tests проверили фактический API установленного пакета: разные владельцы у повторных file references; отсутствие освобождения владельца при Dispose копии; совместные references одного явного владельца и инвалидирование всех views после его Dispose. Тесты не использовали forced GC или RSS thresholds.

В [JasperFx.RuntimeCompiler5.0.0](https://github.com/JasperFx/jasperfx/blob/4f08f1dfeab987660bc392e3efa4ad4a42d9a3d8/src/JasperFx.RuntimeCompiler/AssemblyGenerator.cs) ReferenceAssembly создаёт эти eager references, а Compile создаёт новый generator и повторяет reference traversal. Этот механизм согласуется с H6 DLL-size allocation signature. Точная принадлежность двух anonymous managed IP всё ещё не доказана; это не заявление о бесконечной утечке или уже принятом memory gate.

## Что изменено

- [ScopedMetadataAssemblyGenerator](../../../../apps/Travel.Host/CodeGeneration/ScopedMetadataAssemblyGenerator.cs) реализует существующий IAssemblyGenerator. Корневые Assembly references сохраняются как конфигурация, а disposable metadata создаётся локально для каждого Emit через AssemblyMetadata.CreateFromFile/GetReference.
- Scope освобождает metadata/PE streams после успешного Emit и при ошибке открытия reference, компиляции или загрузки. Сгенерированная Assembly остаётся usable: прежний CustomAssemblyLoadContext и его noncollectible lifetime сохранены.
- [RuntimeCompilationServices](../../../../apps/Travel.Host/CodeGeneration/RuntimeCompilationServices.cs) заменяет upstream registration одним singleton compiler; параллельные вызовы используют разные disposable scopes. Подключение — одна строка в Host Program после UseWolverine.
- Сохранены supported dependency traversal, optional missing-platform dependency skips, Generate name/writer overload, Compile source/type attachment и dynamic-code/trimming warnings. Ошибка resolved metadata file теперь явная, вместо console-and-continue.
- Нет process-wide metadata cache, изменения package versions, GC calls, Static/pregenerated mode, reflection cleanup или новых модульных зависимостей.

Host — правильное место для этого изменения: runtime compilation общая для Marten/Wolverine и модулей, а не правило Hotels Core/Infrastructure.

## Проверки

| Проверка | Результат |
|---|---|
| Roslyn ownership characterization |3/3|
| Scoped compiler TDD |9 RED до реализации → GREEN|
| Final focused compiler suite |13/13, включая builder-only Wolverine DI, повторные/параллельные вызовы, ошибки и исполнение emitted type после Dispose|
| Architecture |243/243|
| Permitted Hotels standalone |351/351|
| Pure evaluator |18/18|
| Historical M1 evaluator |16/17; workingSetRecovery=false,5legacy sections unchanged|
| Wrapper build |0errors;30известных warnings AD0001/Verify SolutionDir|
| Formatter |6files passed|
| Независимое review |0Critical/0Important/1Minor|

Receipts находятся в TestResults/hotels-m2-memory-load-acceptance/source-h7-lifetime-v1: metadata-contract.trx, scoped-red.trx, compiler-final.trx, architecture.trx, hotels-standalone.trx, wrapper-build.log, evaluator-proof.json, historical-offline-replay.json, preserved-integrity.json.

Тест регистрации создаёт только WebApplication builder и ServiceProvider, не вызывает Build/Start Host или внешние сервисы. Реальные metadata owners наблюдаются через небольшой internal factory seam; runtime-код не обращается к приватным полям JasperFx/Roslyn. InternalsVisibleTo добавлен только для существующего Host test assembly.

## Review и принятые ограничения

[Полный review](compiler-metadata-lifetime-review.md) выполнен один раз отдельным fresh reviewer. Существенных findings нет, повторного review/fix pass не было.

Один отложенный Minor: две Task.Run компиляции в concurrency test могут попасть на последовательное исполнение. Тест пока не гарантирует overlap; сами production scopes независимы. Барьер — отдельное усиление теста, не обнаруженный production defect.

Решения исполнителя: per-compilation ownership вместо нового долгоживущего cache; существующий compiler seam вместо package fork/upgrade или Static mode; явный отказ при ошибке resolved metadata. Цена — повторное открытие metadata при последующих компиляциях и ещё не измеренный CPU/I/O/RSS эффект. Whole-Host/DB/generated business workflows, unusual deployment modes и исчерпывающий upstream bypass audit не заявляются принятыми; полный список границ сохранён вreview.

## Сохранность и следующий этап

Branch/base/HEAD: codex/hotels-m2-memory-load-acceptance, c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf. Index пуст; stage/commit/push не выполнялись.

Все272raw files (189historical+5первыйCLIотказ+78H6) и168H6 analysis artifacts перепроверены. Старые H6 freeze и source/build ZIP неизменны. Текущие source/build намеренно изменились для этого fix; старый consumed H6 packet не является новым runtime-ready пакетом и не перезаписывался.

M1failed16/17, S-v2failed22/24, Lnot-run и задачи3.2/3.3 остаются открытыми. Следующий этап — подготовить новый пакет и отдельно измерить эффект исправления. Локальные tests/Dispose contract не доказывают working-set recovery и не заменяют нагрузочную приёмку.
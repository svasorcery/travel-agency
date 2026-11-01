# Завершение Hotels M2 memory/load

2026-11-01. Владелец поручил завершить оставшиеся синхронизацию и архивирование. База документальной финализации — ba5dd8a5e0f54b312638582fb3f2b149051ea8de, финальный merge PR87. Все 8 задач этого change завершены; артефакты proposal/specs/design/tasks имеют статус done.

## Принятый результат и публикация

Локальная fictional S/L v7 приёмка acceptanceVersion5: S23/23 и L23/23, 6304 исхода и 722 измерения. Обязательные пределы RSS 1 ГиБ и final <= baseline + 128 МиБ сохранены. Положительный тренд S остаётся диагностическим отказом; zero-growth/leak-free утверждения не делаются. Подробности — [итоговый отчёт](load-sl-v7-result.md) и [исходные S/L receipts](receipts/sl-v7/provenance.json).

| Этап | Head / merge | Проверки |
|---|---|---|
| [PR86](https://github.com/svasorcery/travel-agency/pull/86) | head5af3005d9b01dc9947273e8d6f4ed01157f65074; merge901451f8df5f0ad1223497a7956160bbf8bfeb63 | [PR CI41330580494](https://github.com/svasorcery/travel-agency/actions/runs/41330580494): 20 jobs passed; [post-merge CI41331994423](https://github.com/svasorcery/travel-agency/actions/runs/41331994423): 18 passed, один failed |
| [PR87: исправление гонки тестовых часов](https://github.com/svasorcery/travel-agency/pull/87) | head88efd0f6cb9b6ded6bafb911d26b6ca1bb8fd06c; mergeba5dd8a5e0f54b312638582fb3f2b149051ea8de | [PR CI41333339828](https://github.com/svasorcery/travel-agency/actions/runs/41333339828): 20/20; [post-merge CI41334632763](https://github.com/svasorcery/travel-agency/actions/runs/41334632763): 19/19; оба attempt1 |

В финальных PR/post-merge runs Hotels438/438, без пропусков. Paid AI evaluation и PR-only E2E на push пропущены по штатным условиям. Четыре полных CI receipts скопированы побайтно: [provenance](receipts/publication/provenance.json), [PR86 CI](receipts/publication/pr86-ci.json), [первый post-merge отказ](receipts/publication/pr86-postmerge-failed.json), [PR87 CI](receipts/publication/pr87-ci.json), [финальный post-merge](receipts/publication/pr87-postmerge-ci.json). Ранее failed CI не переименованы в success.

## Синхронизация контракта

[hotel-stay-search](../../../specs/hotel-stay-search/spec.md) сохраняет исходные заголовок, Purpose и десять незатронутых требований. В Useful bounded single-process resilience сохранены три старых сценария и добавлены тринадцать; итог — 11 требований / 51 сценарий. Перенесены независимая M2 приёмка, полный учёт источников/исходов, точные окна, сохранение исторических отказов и явно версионированные решения v2–v5. Блок основного контракта совпадает с [сохранённой delta](specs/hotel-stay-search/spec.md); повторное применение ничего не добавляет.

## Целостность архива

Все 65 исходных файлов перенесены в openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance. 54 файла сохранены побайтно. В девяти Markdown-файлах изменены только 42 относительные ссылки с сохранением цели; tasks.md и publication.md дополнены текущим итогом. Нормативная delta, .openspec.yaml, acceptance-v5.md и все четыре прежних файла receipts/sl-v7 сохранены побайтно.

Незакрытые чекбоксы внутри старых frozen protocols описывают состояние до их запусков. Они не являются текущим tasks.md и не переписываются задним числом. Исторические пути внутри JSON provenance также сохранены; их нынешнее расположение определяется переносом всего change в датированный архив. Original source/build archives, dumps/ETL и raw receipts остаются в локальном TestResults; архивирование их не перемещает и не удаляет. M1 сохраняет failed16/17.

Writer и reader source manifest дополнены только точным архивным profile-root. Прежние active/evidence roots и проверки хешей, полного inventory, физических границ и create-only output сохранены; другие архивы не принимаются. Это поддержка перемещения принятого плана, не новая нагрузка или изменение executor.

## Проверка и границы

Главные спецификации прошли strict validation; перед перемещением change прошёл strict validation и имел 8/8 задач. Сверка delta/main и UTF-8 прошла; 200 относительных ссылок в окончательном staged-наборе проверены, broken repository links нет, шесть ссылок явно ведут в локальный TestResults. Node manifest tests6/6 и .NET manifest-reader tests6/6 прошли после отдельных RED/GREEN; прежние active-path cases сохранены. Strict validation всех20 main specs, AI harness134/134 и Biome прошли. Точные локальные результаты находятся в TestResults/hotels-m2-memory-load-acceptance/finalization-v1. Финализация получает собственный PR и независимые PR/post-merge CI; их окончательные identities записываются в описании этого PR.

В рамках данного change обязательных задач не осталось. Существующая необязательная заметка о расширении graph-test coverage сохранена в [review](recovery-cohort-review.md). Реальные поставщики, target provisioning/enablement и rollout остаются отдельными будущими этапами; flags/defaults не изменяются.

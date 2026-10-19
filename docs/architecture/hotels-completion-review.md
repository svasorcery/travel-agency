# Hotels M1/M2 — завершение кода, спецификаций и CI

Дата: 2026-10-19. Согласованный Hotels M2 реализован, проверен отдельным исполнителем `gpt-6.1-sol` и лично основным агентом, слит в `dev`. Этот документ связывает текущие контракты с исходными результатами; архивирование не означает готовности внешнего поставщика или развёрнутой системы.

## Текущие контракты

| Контракт | Требования / сценарии |
|---|---:|
| [Поиск проживания](../../openspec/specs/hotel-stay-search/spec.md) | 11 / 38 |
| [Бронирование и приватный результат](../../openspec/specs/hotel-reservation-booking/spec.md) | 22 / 58 |
| [Общий вход Duffel webhook](../../openspec/specs/duffel-webhook-routing/spec.md) | 7 / 15 |
| [Сопоставление отелей](../../openspec/specs/hotel-property-matching/spec.md) | 5 / 11 |
| [Сравнение предложений и ранжирование](../../openspec/specs/hotel-offer-comparison-ranking/spec.md) | 6 / 14 |

Всего51 требование/136 сценариев. Три main contracts M1 были полностью синхронизированы до M2:33 требования/75 сценариев. M1 архивирован без повторного применения старых ADDED blocks; затем M2 добавил18 требований и обновил6, сохранив прежние сценарии. Существующие Purpose сохранены, новые Purpose перенесены из утверждённых M2 deltas. Duffel webhook contract не изменён.

История и исходные требования сохранены в [архиве M1](../../openspec/changes/archive/2026-10-19-hotels-m1-duffel-booking/archive-verification.md) и [архиве M2](../../openspec/changes/archive/2026-10-19-hotels-m2-multisupplier-search-booking/archive-verification.md). Текущий контракт — в main specs; архивные deltas не следует повторно применять к нему.

## Реализованная область

Duffel Stays и Hotelbeds имеют отдельные Infrastructure adapters за нормализованными Hotels capabilities. Поиск двух источников ограничен по параллелизму/срокам и сохраняет различия skipped, complete-empty, limited и failed. Отели группируются только по проверенному соответствию; номера, тарифы и предложения остаются отдельными. Детерминированное ранжирование объясняет известную стоимость за всё проживание, область сравнения и неизвестные суммы.

Angular поддерживает один номер/1–2 взрослых: выбор источника, актуальные условия, согласие, одну покупку и приватный результат/reload. Hotelbeds поддерживает согласованный Net/AT_WEB subset; Net не объявляется розничной ценой путешественника. Сохранённая попытка привязана к исходным provider/account/environment/context. Unknown не разрешает повторную покупку, поиск неизвестного booking ID или замену поставщика. Shared DTO/framework для гипотетических потребителей не добавлен; композиция остаётся в Api.Composition.

## Публикация и CI реализации

| Этап | Точная привязка | Результат |
|---|---|---|
| M1, [PR54](https://github.com/svasorcery/travel-agency/pull/54) | source `e04b3f1b7c89f367e33412cf1b898b07f2c21976`, merge `fd94b9c91cd4345f1b5a684a3c8b73d2bc84e6b9` | [Post-merge CI39040733816](https://github.com/svasorcery/travel-agency/actions/runs/39040733816):16 обычных jobs passed |
| Последующие M1 исправления | `75dfba532c0f321286630e3d04fbea70347903a7` | [CI39061070839](https://github.com/svasorcery/travel-agency/actions/runs/39061070839):16 обычных jobs passed |
| M2, [PR55](https://github.com/svasorcery/travel-agency/pull/55) | source `a7f67b0070b194e02b85c68d5d4a6370da05f9b6` | [PR CI39257646397](https://github.com/svasorcery/travel-agency/actions/runs/39257646397):17 обязательных jobs passed, включая E2E2/2 |
| M2 после слияния | merge `5a0fb7a36de6101170c4b676415e616890b4bfa6` | [Post-merge CI39260364791](https://github.com/svasorcery/travel-agency/actions/runs/39260364791):16 обычных jobs passed |

В dev push PR-only E2E и отдельно разрешаемый paid AI job пропущены по условиям workflow. В PR55 пропущен только paid AI job. Логи PR55: Hotels unit79/79, Hotels integration125/125, Host HTTP485/485, Host integration115/115, Architecture183/183; в post-merge Hotels125/125 и Host115/115. В этих наборах нет skipped tests. Это результаты реализации, а не доказательство ещё не опубликованного документационного closeout; его собственный exact-head CI записывается в соответствующем PR.

## Сохранённые ограничения

- M1 завершён по явному решению владельца с принятым ограничением памяти. Исходный L1 остаётся **16/17**, `workingSetRecovery=false`, `passed=false`, exit1. Зелёный ordinary CI его не исправляет; waiver не переносится на M2.
- Исходный локальный L1 receipt `TestResults/hotels-m1/l1/receipt.json` сохранён без изменений: SHA256 `a37df64485b4f585b846fb021c702585f90c178960bcac767f7965c9a08ace3e`. Исторические RED/diagnostic/TRX receipts и их записи не заменены финальными зелёными результатами.
- Локальный relational/Host contour использовал собственные одноразовые PostgreSQL, существующие миграции, временные ключи и terminal fictional supplier handlers. Два crash cases действительно завершали отдельный процесс; другие cold Host cases не переименованы в OS-process proof.
- Реальные supplier access/права на сопоставление и сравнение, mTLS/account certification, стоимость/квоты и target provisioning не принимались. Агрегация и Test-only интеграции остаются default-off; реальные supplier/paid API и deploy не выполнялись.
- Отдельный M2 load/performance профиль не запускался. Controlled-clock/concurrency tests доказывают функциональные ограничения и защиту от повторов, а не пропускную способность, p95 или восстановление памяти.

## Передача к Rail

Следующий подпроект roadmap — Rail: поиск поездов и расписаний без бронирования. Начать с OpenSpec explore, текущего scaffold и актуальных официальных источников. Майский список поставщиков/условий — историческая отправная точка для новой проверки. M2 rights, ключи и настоящие вызовы не становятся разрешёнными из-за архивации или перехода к следующему модулю.

## Документационное закрытие

Локально проверены:0 активных changes,12/12 main specs в strict validation,27 не изменяемых M1 requirement blocks без изменений и14 сохранённых прежних scenarios в6 обновлённых требованиях. Нормативные deltas и `.openspec.yaml` обоих архивов совпадают с исходниками; L1 SHA256 выше перепроверен после всех перемещений.89 относительных Markdown links перенесены с сохранением цели; в затронутых документах нет broken repository links. Локальные TestResults ссылки остаются явно локальными, не считаются versioned repository artifacts.

Dependency-free AI harness134/134 и README catalog/tests13/13 прошли. Изменены только Markdown и расположение сохранённых `.openspec.yaml`; application/test code, dependencies/security backport, workflow configuration, schema и feature flags не менялись. Документационный PR получает собственные PR/post-merge CI receipts, отдельно от реализации PR55; итог привязывается к его точному head/run в описании PR.

# Verification

## PR №92 и GitHub Actions

Изменение принято в `dev` 19 ноября 2026 года в 19:13:07 МСК (`2026-11-19T16:13:07Z`). Коммит слияния `2fa525d1a74daf84fd7b4f01b4fb6e1423881f18` объединяет базу `0cc43e223fa238a21c44aea92369e057389427c8` и вершину ветки `codex/trips-flight-calendar` `88c2c433103d1f64c5f1da0da5b44f41cb21d796`. [Изменения PR №92](https://github.com/svasorcery/travel-agency/pull/92/changes)

| Запуск | Событие GitHub Actions | Фактически проверенный checkout | Результат |
| --- | --- | --- | --- |
| [PR CI 44557085329, попытка 1](https://github.com/svasorcery/travel-agency/actions/runs/44557085329/attempts/1) | `pull_request`, вершина PR `88c2c433103d1f64c5f1da0da5b44f41cb21d796` | Пробное слияние GitHub `06a5ab69b8d7574b9f3f48f3cc3b8568b255c159` | Прошли 20 из 20 применимых заданий, включая `test-e2e`. Платный `test-ai-evals` пропущен по условию запуска. |
| [CI после мержа 44563576839, попытка 1](https://github.com/svasorcery/travel-agency/actions/runs/44563576839/attempts/1) | `push` в `dev` | Коммит слияния `2fa525d1a74daf84fd7b4f01b4fb6e1423881f18` | Прошли 19 из 19 применимых заданий. Платный `test-ai-evals` и запускаемый только на PR `test-e2e` пропущены по условиям запуска. |

## Свидетельства Trips full-stack

В обоих артефактах `trips-full-stack-evidence` сверены SHA-256 загруженного ZIP и файла `*-trips-aggregate.json`. Агрегаты подтверждают чистый фактический checkout из таблицы выше, успешный результат всех этапов для профилей `moscow`, `saint-petersburg` и `flight-links`, включая браузерную проверку и очистку одноразовых стендов.

| Артефакт | SHA-256 архива | SHA-256 агрегата |
| --- | --- | --- |
| [PR CI, артефакт 14546338574](https://github.com/svasorcery/travel-agency/actions/runs/44557085329/artifacts/14546338574) | `ca09724ec8baa8ebf4e63e09a709646eec054db01e197c6ddd2d097ae02b2447` | `a096049a1507cb83802739ef22391cb870f355d044e2ba40f068a5fc7ff442f7` |
| [CI после мержа, артефакт 14553090161](https://github.com/svasorcery/travel-agency/actions/runs/44563576839/artifacts/14553090161) | `64fafce00b8801e87a86a0d3e05dd1806fb905c0dec389487969fe0937385737` | `2553d75ce59256a4333690f2e328419309d4f59ed2e6fda7a3fedbaa1c90d298` |

## Закрытие OpenSpec

Завершены все 7 задач схемы `spec-driven`. Шесть требований с 16 сценариями перенесены в основной контракт `trip-flight-calendar`. Зафиксированные проверки относятся к принятым исходникам и CI на вымышленных данных. [Задачи изменения](tasks.md)

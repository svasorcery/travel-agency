# M1 archive acceptance — 2026-10-19

Владелец отдельно поручил полностью завершить OpenSpec по Hotels. M1 имел22/22 принятых задач и все completed planning artifacts. Перед архивированием поблочно проверены все33 требования/75 сценариев M1 в трёх main specs: они уже были синхронизированы. Повторный sync M1 не выполнялся; после его архивирования применён только M2.

Основные спецификации теперь описывают M1 вместе с M2. [Общий completion review](../../../../docs/architecture/hotels-completion-review.md) содержит текущие контракты, точные source/merge SHA и CI. Этот архив сохраняет исторический M1 scope и не является альтернативной текущей спецификацией.

- [PR54](https://github.com/svasorcery/travel-agency/pull/54): source `e04b3f1b7c89f367e33412cf1b898b07f2c21976`, merge `fd94b9c91cd4345f1b5a684a3c8b73d2bc84e6b9`.
- [Post-merge CI39040733816](https://github.com/svasorcery/travel-agency/actions/runs/39040733816):16 обычных jobs passed; PR-only E2E/paid evals skipped по workflow.
- Последующие исправления `75dfba532c0f321286630e3d04fbea70347903a7`: [CI39061070839](https://github.com/svasorcery/travel-agency/actions/runs/39061070839) прошёл16 обычных jobs.

Исходные [verification](verification.md), deltas, design, tasks и `.openspec.yaml` сохранены. Для перемещения исправлены относительные ссылки и добавлены пометки исторического статуса; исходный текст до перемещения доступен в Git на `5a0fb7a36de6101170c4b676415e616890b4bfa6` по прежнему change path. Локальный before-archive snapshot и manifest сохранены в `TestResults/hotels-openspec-finalization/`.

Исходная проверка памяти остаётся **16/17**, `workingSetRecovery=false`, `passed=false`, exit1. Это принятая владельцем граница M1 source acceptance, а не пройденный memory gate. L1 receipt `TestResults/hotels-m1/l1/receipt.json` сохранён побайтно, SHA256 `a37df64485b4f585b846fb021c702585f90c178960bcac767f7965c9a08ace3e`. Новой нагрузки, supplier/paid API, target migrations/keys или deploy не было. Архив не передаёт waiver M2 и не разрешает rollout.

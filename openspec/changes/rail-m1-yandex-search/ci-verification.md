# Rail M1 — PR и обычные CI-проверки

2026-10-20 пользователь отдельно разрешил commit,push,PR в dev и ожидание обычного CI: «Реальный Яндекс не включай, без merge и деплоя». Это разрешение заменяет прежний запрет публикации только для этой ветки/PR; real supplier/model/account/target действия по-прежнему не разрешены. Черновик `trips-m1-structured-planning` остаётся вне Rail commit.

Base и актуальный origin/dev перед публикацией: `799e31a6b31ff256ba017861cd911e547e4e9896`; ветка `codex/rail-m1-yandex-search`, ancestry проверен. Принятый source/document manifest:150files,SHA256 `6eae42e36ebe15dd06be60ae8691bbae824ebffa2437ee02ab7cd7e3eac0d9b7`; measured runtime использовал прежний `7c5c7bca06287ce97d7076a066a5ee059133ef1781107b1ecb85a811cb558ec7`,после него менялись только2документа. [Host/measured receipts](runtime-verification.md), [personal review](personal-review.md) и [source acceptance](verification.md) сохраняются.

Непосредственно перед commit:AI harness134/134,README checks13/13,dependency artifacts check exit0(сохранён braces backport; это не свежий upstream/runtime audit),CSharpier1084files exit0,strict OpenSpec valid=true. Все execution/code hashes совпадают с принятым manifest; full suites переданы обычному CI. Независимые локальные unit35/35,adapter75/75,architecture182/182,actual Host3/3 и immutable measured6/6 уже подтверждены в runtime receipt; нагрузка повторно не запускается.

CI пока pending. Ожидается обычный PR workflow, включая lint/build/frontend,все обычные unit/integration/architecture/contract/Host/Aspire lanes и обязательный PR E2E. Paid `test-ai-evals` должен быть skipped по существующему workflow condition; его пропуск не является пропуском обычного gate. Без обходов/ручных rerun/изменения thresholds; при failed job сохранить run/attempt/head/logs и разобрать причину.

Merge/auto-merge/deploy не выполняются. Реальный Yandex default-off, startup admission закрыт; CI использует только разрешённые fictional/fixture границы.

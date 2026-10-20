# Rail M1 — PR и обычные CI-проверки

## Принятый source CI

[PR57](https://github.com/svasorcery/travel-agency/pull/57) открыт вdev,без merge/auto-merge. Полный [CI39445766355](https://github.com/svasorcery/travel-agency/actions/runs/39445766355),attempt1,event=pull_request,завершён success. Product/source head `7d8abf82697e653d28bc7af67854380a6f7bc6da`,base `799e31a6b31ff256ba017861cd911e547e4e9896`,tested synthetic merge `46438d0db3a52a53d78a5eafc2ad054990bf74b8`. Committed manifest154files,SHA256 `c587548206699270c19b0b29947afcb708fa9dd5b95864fdffe2347fb8f2f461`.

Все17ordinary jobs success, включая обязательный test-e2e;0ordinary skipped. Единственный skipped — protected paid test-ai-evals по прежнему условию, он не выполнялся и не выдаётся за оценку модели. Raw `ci-responsive-final.json`, `ci-responsive-acceptance.json`,17joblogs и `ci-responsive-test-counts.json` сохранены под `TestResults/rail-actual-host/`. Основной агент проверил полный обязательный набор по именам и прочитал raw counts. Старые2failedruns/исходные RED сохранены ниже; их промежуточные pending статусы заменены этим итогом.

| Проверка | Passed / total |
|---|---|
|Rail unit / adapter HTTP|35/35;75/75|
|Host HTTP / actual integration|486/486;118/118|
|Architecture / Aspire / contracts|182/182;6/6;41/41|
|Flights unit / integration|1500/1500;413/413|
|Hotels unit / integration|79/79;125/125|
|Support unit / integration|98/98;87/87|
|Identity / AI unit|25/25;174/174|
|Node demo / moved contract|75/75;7/7|
|Fictional browser / real AppHost PR E2E|86/86;2/2|

В перечисленных .NET suites0failed/0skipped. Nx affected build/test/lint,security backport/lock checks,CSharpier и ordinary lint также success. Реальный Яндекс остался выключен, supplier/model/target не включались; нагрузочный профиль автоматически в CI не запускался. Local measured acceptance остаётся отдельным результатом, Hotels RED не изменён.

Финальный документальный commit закрывает6.2 и сохраняет этот уже принятый source run. Он не меняет runtime/test/config и не объявляет свой ещё не существовавший SHA протестированным; проверки окончательного PR head будут дожданы отдельно. Sync/archive,merge и deployment не выполняются.

## Responsive regression после добавления Rail navigation

Независимый повтор основного агента: `node node_modules/@playwright/test/cli.js test -c tests/travel-e2e/playwright.flights-demo.config.ts tests/travel-e2e/demo/rail-search.spec.ts -g 'own Rail flow' --retries=0 --output TestResults/rail-nav-parent`,NX_DAEMON=false:1/1GREEN19.3s. Он исполнил320/360geometry+keyboard и полный собственный поиск/attribution case. После него4201/5100 свободны; Trips уведомлён о завершении слота в рамках разрешённой координации. Original RED/дваfailedCI не удалены. Новый полный обычный CI responsive source head ещё требуется.

Root cause подтверждён: общая shell навигация и группа ссылок были nowrap. После Rail link их min-content ширина стала больше мобильного viewport. Изолированный DOM дал369px,actual app на локальных шрифтах393px,CI377px при viewport320/360; это разные измеренные среды одной ошибки, не заявляется совпадение чисел. Actual existingFlights mobile и новый Rail width case сначала2/2RED с исходным CSS/retries0 (`TestResults/rail-nav-actual-red`). Сохранены screenshot/metrics и исходный patch.

Исправление прежнего gpt-6.1-sol — только2правила flex-wrap в app.scss:перенос внешней nav и группы links. Нет overflow hidden/clipping/удаления пунктов/ослабления assertions. Rail own flow теперь проверяет320/360px,positive visible anchor bounds иTab traversal. Full existing package consumer `NX_DAEMON=false npm.cmd run test:flights-demo -- --retries=0 --output=TestResults/rail-nav-full` exit0:Node75/75,tools7/7,все86browser/86passed3.8m,0retries/0skips. Дополнительный samecase JSON capture1/1: `rail-nav-metrics.json`,document/header/nav320/360,links20..300/20..340. Основной агент лично прочитал CSS+test diff и открыл оба320/360PNG: все ссылки/brand видимы, обрезания нет; независимый повтор записывается отдельно.

Первый local reproduction не запускал тесты из-за чужих занятых4201/5100 — это infrastructure refusal, не business RED. Чужие Trips процессы не трогали/не переиспользовали; порты освободились сами, после чего обычный fixture выполнил RED→GREEN. Пользователь отдельно разрешил сообщение в чатTrips о временном свободном слоте; сообщение отправлено только для координации, без изменений их source/branch. Backend/измерительные thresholds не менялись.

2026-10-20 пользователь отдельно разрешил commit,push,PR в dev и ожидание обычного CI: «Реальный Яндекс не включай, без merge и деплоя». Это разрешение заменяет прежний запрет публикации только для этой ветки/PR; real supplier/model/account/target действия по-прежнему не разрешены. Черновик `trips-m1-structured-planning` остаётся вне Rail commit.

Base и актуальный origin/dev перед публикацией: `799e31a6b31ff256ba017861cd911e547e4e9896`; ветка `codex/rail-m1-yandex-search`, ancestry проверен. Принятый source/document manifest:150files,SHA256 `6eae42e36ebe15dd06be60ae8691bbae824ebffa2437ee02ab7cd7e3eac0d9b7`; measured runtime использовал прежний `7c5c7bca06287ce97d7076a066a5ee059133ef1781107b1ecb85a811cb558ec7`,после него менялись только2документа. [Host/measured receipts](runtime-verification.md), [personal review](personal-review.md) и [source acceptance](verification.md) сохраняются.

Непосредственно перед commit:AI harness134/134,README checks13/13,dependency artifacts check exit0(сохранён braces backport; это не свежий upstream/runtime audit),CSharpier1084files exit0,strict OpenSpec valid=true. Все execution/code hashes совпадают с принятым manifest; full suites переданы обычному CI. Независимые локальные unit35/35,adapter75/75,architecture182/182,actual Host3/3 и immutable measured6/6 уже подтверждены в runtime receipt; нагрузка повторно не запускается.

PR [57](https://github.com/svasorcery/travel-agency/pull/57) открыт вdev,autoMergeRequest=null. Product commit `c474bd02f0df2e22372780f073a1d7f9fffbf2d2` отправлен обычным push; pre-commit CSharpier/Biome и commitlint прошли без bypass,pre-push architecture182/182. Перед commit удалены только лишние EOF blank lines в Rail HTML/tasks; code behavior не менялся. Post-commit manifest `a4baf84f95b15163696a38c91a7639c730de0ad7f903fd0a0c9f488eac60e243` отличается от предыдущего только этим whitespace в HTML. Черновик Trips остался untracked.

Первый [CI39440021934](https://github.com/svasorcery/travel-agency/actions/runs/39440021934),attempt1,event=pull_request,head=c474bd02: frontend-affected failed. API-client/web build/test GREEN; ESLint выявил6errors:5cross-project test imports и1empty default callback. Лог `TestResults/rail-actual-host/ci-frontend-product-direct.log` сохранён; локальная предшествующая Biome проверка не подменяет ESLint. Узкие source/test исправления выполняет прежний gpt-6.1-sol,основной агент лично проверяет; lint rules/workflow не ослабляются. Остальные jobs первого CI ожидаются без отмены/ручного rerun.

Полная обычная CI-приёмка пока pending. Ожидается обычный PR workflow, включая lint/build/frontend,все обычные unit/integration/architecture/contract/Host/Aspire lanes и обязательный PR E2E. Paid `test-ai-evals` должен быть skipped по существующему workflow condition; его пропуск не является пропуском обычного gate. Без обходов/ручных rerun/изменения thresholds; при failed job сохранить run/attempt/head/logs и разобрать причину.

Первый run завершён failure:15jobs success,frontend-affected failure,test-e2e skipped из-за failed prerequisite,test-ai-evals skipped по существующему условию. Это НЕ17/17GREEN. Host integration118/118,Host HTTP486/486,Rail35/35+75/75,Architecture182/182,Flights integration413/413,Aspire6/6. Raw final/job receipts: `TestResults/rail-actual-host/ci-product-final.json`, `ci-product-job-logs/`; synthetic PR merge checkout `1802e4ca44ca2741a1f50244f3939f22065d92cf`,run head productc474bd02,base799e31a6. Старый run не отменяли и не перезапускали.

Merge/auto-merge/deploy не выполняются. Реальный Yandex default-off, startup admission закрыт; CI использует только разрешённые fictional/fixture границы.

## Узкое исправление CI frontend

Второй run завершён failure:15success,frontend-affected failure,test-e2e skipped,paid test-ai-evals expected skipped. Raw final/job receipts сохранены в `ci-fix-final.json`,`ci-fix-job-logs/`; frontend log `ci-frontend-fix.log`/readable variant. Полная CI-приёмка не засчитана.

Исправление опубликовано commit `86c517949ca90e7dfa6d2959c9590e335da939c5`,новый [CI39442338914](https://github.com/svasorcery/travel-agency/actions/runs/39442338914),attempt1. Parent browserA→B→A1/1GREEN17.1s; committed manifest153files,SHA `38f4cfcf5f25a8652296e6a75987a2bd437eec27ba7f4707cfb3831034e88a5a`. Предварительный manifest до staging rename отказал ENOENT на ещё cached удалённом пути; после нормального staging/commit identity записан успешно, отказ не скрыт fallback/hash подменой. Обычные hooks выполнены; pre-push arch hook штатно skipped по отсутствию matching module C# diff, предыдущие182cases остаются подтверждены.

Второй CI прошёл lint/build/frontendunit,но полный fictional browser выявил17failed/69passed из86. Все primary errors — mobile horizontal overflow(360→377px); вторичные retries в некоторых fictional seed получили hold failure после первого failed case. Эти результаты не названы flakes и не обходятся rerun. Root hypothesis — добавленный Rail link при nowrap в общей shell navigation. Узкое responsive исправление и полный локальный86case regression выполняет прежний implementer, без скрытия overflow/ослабления тестов. Обычная CI-приёмка всё ещё pending; второй failed run/log сохраняется отдельно.

Прежний gpt-6.1-sol воспроизвёл RED и исправил6ESLint errors: project-local test fixtures вместо cross-project imports; семь demo→decoder cases перенесены в существующий tools/rail harness и обязательно вызываются существующим test:flights-demo; browser получает authored station fixture через существующий loopback HTTP; default callback записан эквивалентно`() => undefined`. Fixtures/assertion bodies проверены на полное совпадение, production exports не расширялись. Ни новые dependencies/locks/workflow/lint suppression, ни новая библиотека не добавлены. Parent manifest helper включает3новых retained consumer paths(package.json и2project-local fixtures).

Implementer: три exact Nx lint targets api-client/travel-e2e/web exit0(8/11/49warnings),api-client249/249+moved7/7=прежние256,focused Angular2files/8tests,browser9/9incl delayedA→B→A. Основной агент лично прочитал весь diff и новые fixtures, сравнил исходные тела, независимо повторил три Nx lint targets с--skipNxCache иmoved7/7; scoped dependency artifacts check exit0. Текущие warnings не подавляются и не объявляются отсутствующими. Backend/Host/profile не менялись; load повторно не выполнялся. Полный обычный CI нового head ещё требуется.

# Rail M1 — sync/archive verification

Дата: 2026-10-20. Пользователь разрешил sync/archive Rail и обновление документации с merge/post-merge CI. Реальный Яндекс не включать, без deploy. Работа подготовлена локально в отдельном worktree и ветке `codex/rail-docs-closeout` от точного обновлённого origin/dev `30cd68d892cf72994c83047a22ceff782814796f`; это base документации, а не SHA реализации или её измерений.

## Контракт и архив

- Pinned OpenSpec list/status подтвердил spec-driven, четыре завершённых artifacts и 25/25 задач. Получен один specs-rule snapshot для выбранного worktree; дополнительных artifact rules нет.
- Создан [основной контракт](../../../specs/rail-schedule-search/spec.md): 23 требования / 90 сценариев, одно Requirements, без delta operation headers. Purpose перенесён дословно.
- Сохранены имена, порядок и полный набор всех требований/сценариев. Единственное уточнение текущего main spec касается No speculative infrastructure: историческое «Trips остаётся unwired scaffold» выражено как запрет Rail менять подключение других модулей. Trips M1 независимо слит в PR #58; Rail не владеет его enablement. Других изменений поведения нет.
- Исходный delta и `.openspec.yaml` сохранены без изменений; архив содержит все девять исходных файлов. Их SHA256 проверены при переносе. Архивный delta остаётся историческим входом, повторно применять его к текущему main spec не нужно.
- Исторические source/runtime/CI receipts сохранены в соседних [verification](verification.md), [runtime verification](runtime-verification.md), [CI verification](ci-verification.md) и [personal review](personal-review.md). Поздние результаты собраны в [completion review](../../../../docs/architecture/rail-completion-review.md).

Прежние pending Host/measured/CI и запреты commit/push/merge в этих receipts относятся к их датированным этапам. Отдельные последующие разрешения и успешные результаты не стирают исходный RED, прерванный attempt или failed quality/load другого модуля.

## Merge и post-merge evidence

PR #57 принят на head `ea40575d14df8ccdfbab29863bb237a8c3d5c8fb`; [PR CI 39448274735](https://github.com/svasorcery/travel-agency/actions/runs/39448274735) passed 17/17 ordinary jobs, включая E2E 2/2. Merge в dev: `22a06448edf1edc6cd139894aae3fcdc5e8edf49`, родители `799e31a6b31ff256ba017861cd911e547e4e9896` и принятый head. [Post-merge CI 39453275768](https://github.com/svasorcery/travel-agency/actions/runs/39453275768), attempt 1, event push, passed 16/16 ordinary jobs. PR-only E2E и paid AI eval на push штатно skipped; ordinary skips отсутствуют.

Actual Host 3/3 и measured attempt 02 с 6000 useful successes / recovery 6/6 являются отдельными локальными результатами. Profile SHA256 `802e60aee97b248c242382672c5e4e5e7a0c2137006f025fae5223c3a30473ed` не менялся. Первый measured attempt остаётся interrupted 4/6 без terminal verdict. Два failed CI runs и первоначальные HTTP/schema/browser отказы сохранены.

Локальные первичные файлы после уборки находятся в `D:\_Projects\_github\travel-agency\TestResults\_archives\rail-20261020\rail-evidence.zip`, SHA256 `4bde938215bb4148dc20878a8994cbc15a063fbf350c06e9825841a9726281eb`, 348 files. Это локальный ignored архив fictional test evidence, не versioned supplier dataset.

## Область финализации

Обновляются текущие ссылки и статусы Rail, main spec и размещение архивных Markdown/metadata. В source-manifest helper меняются только пути к документам, чтобы он включал новый canonical spec и сохранённые proposal/design/delta. Runtime, HTTP/TS contracts, tests, feature flags, profile thresholds, dependencies/security backport и workflow configuration не меняются.

Подготовка финализации не выполняла Host/DB/schema startup, measured rerun, реальные supplier/model calls или регистрацию. После локальной проверки пользователь отдельно разрешил commit, push и PR в dev с ожиданием обычного CI. Merge и deploy не разрешены. Реальные права/доступ/квоты/restart, live source/banner и target acceptance остаются pending. Прежний source CI не объявляется CI документационного diff; результат нового PR CI фиксируется в PR на его точном head.

## Проверки документационного результата

- Pinned strict validation: 13/13 main specs, valid; Rail отсутствует в active changes.
- Traceability: 23 requirements / 90 scenarios / 25 completed tasks; все девять исходных файлов совпадают по SHA256. Один main-spec scenario имеет явно записанное выше уточнение scope, другие тела не менялись.
- 18 Markdown documents / 191 relative repository links / 0 missing targets. Устаревших active Rail ссылок в текущих документах нет.
- Dependency-free AI harness: 134/134; README examples: 13/13. Первичная security lock/artifact проверка через штатный npm entrypoint завершилась exit0; braces backport revision1 сохранён. Этот режим не проверяет установленный runtime, но получает свежий npm audit.
- Manifest helper: node syntax check и Biome exit0; исполняемый hash consumer включает canonical spec и сохранённые proposal/design/delta. Profile describe: executed=false, исходный SHA256 профиля сохранён.
- Первичный ZIP повторно проверен по SHA256, внутри348files; attempt02 verdict подтверждает6000useful, recovery6/6 и неизменённые thresholds. Live read post-merge run подтвердил16/16jobs на22a0644.
- Application/test source, flags, dependencies/locks и CI configuration не менялись. Source/local Host/load, supplier/model и deploy повторно не запускались. Эти локальные проверки завершены до публикации документационного diff; они не заменяют новый PR CI.

Сохранённые нормативные SHA256:

- .openspec.yaml: b552ab6519df8f66b705724100f565c5cd842bc9389aeaa177da9cec3d02a685.
- specs/rail-schedule-search/spec.md: c0e06204fabab529f8e2568ca7007e7d94dfd4fb37d1ed7c58ad2880395c2e08.

## Повторная проверка перед публикацией

2026-10-20 в 22:20 MSK strict main specs 13/13, AI harness 134/134, README 13/13, неизменность девяти архивных файлов и staged whitespace проверены повторно. Однако свежий `npm run check:dependency-artifacts` завершился exit1: root chain `@nx/angular` включает `node_modules/nx/node_modules/follow-redirects` 1.16.0 с [GHSA-q3jh-wrc6-jgq8](https://github.com/advisories/GHSA-q3jh-wrc6-jgq8) и [GHSA-8r9p-f939-6h3c](https://github.com/advisories/GHSA-8r9p-f939-6h3c). Advisory pages обновлены 20 октября; обе указывают исправление в 1.16.1. OpenSpec lock/artifact scope прошёл.

Lockfiles, зависимости, remediation policy и CI gates в этом diff совпадают с base `30cd68d892cf72994c83047a22ceff782814796f`. Новые advisories не разрешены существующим braces backport. Исходный успешный результат не подменяет этот отказ; root audit сохранён локально в `TestResults/dependency-audit/root.json`. Документационный PR должен показывать фактический результат обычного CI; dependency remediation требует отдельного изменения и не включена скрытно в Rail.

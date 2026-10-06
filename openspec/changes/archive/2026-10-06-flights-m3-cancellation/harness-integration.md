# OpenSpec ↔ Travel harness: контракт подключения

**Статус 2026-10-05:** T1–T2 завершены. OpenSpec1.14.0, CLI0.147.0, явная gpt-5.6-sol: full authenticated verifier PASS на snapshot47files в стандартном never/read-only без callback/grants. Local harness129/129, CLI9/9 PASS. Product T3+/publication не начаты; historical failures ниже сохранены, фактическая итоговая evidence в конце.

## Варианты

1. **Repo-local pinned CLI + детерминированно адаптированные Codex skills — рекомендуется.** Отдельный `tools/openspec/package.json`/lock с exact 1.14.0. Dependency-free harness проверяет прежний Travel inventory и точный effective OpenSpec inventory. Root Angular dependency graph не меняется.
2. `init --tools none`: CLI validation и один corpus без generated skills. Это допустимый более узкий пилот; при выборе этого варианта native skill workflow не считается проверенным.
3. Global/latest installation и wildcard inventory exemptions не допускаются.

## Закреплённые inputs и launcher

Закреплённая версия: `@fission-ai/openspec@1.14.0`, tag SHA `94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5`, npm integrity в research.md. Node local v22.18.0 соответствует >=20.19.0. Перед установкой сверить metadata именно выбранной версии; появление нового latest не меняет pin без review.

После tooling approval создать tool-local package/lock. `npm.cmd --prefix tools/openspec ci --ignore-scripts --no-audit --no-fund` использует published dist. Если package требует script, зафиксировать препятствие и согласовать конкретное действие. Root npm install/prepare и global install не нужны.

Единственный launcher — `tools/openspec/run.mjs`. Он находит repository root по своему физическому расположению, проверяет package version/integrity metadata и запускает через текущий Node **абсолютный** `tools/openspec/node_modules/@fission-ai/openspec/bin/openspec.js`, с argument array и без shell lookup. Никаких bare openspec/npx/global fallback. Отсутствующий или несовместимый локальный package — явная ошибка до subprocess. Root npm script `openspec` — только удобный alias того же launcher; это не второй путь исполнения.

Launcher передаёт дочернему процессу cwd точного repository root независимо от исходного cwd. Временный task-owned XDG_CONFIG_HOME существует в рамках одного invocation; env: OPENSPEC_TELEMETRY=0, DO_NOT_TRACK=1, OPENSPEC_NO_COMPLETIONS=1. Перед каждым init/update внутри этой же lifetime задаются profile=core/delivery=skills. Очистка затрагивает только проверенную собственную temp-директорию; global APPDATA config/профиль пользователя не меняются. stdout read-команд остаётся исходным JSON CLI, диагностические сообщения launcher — stderr.

## PR1: как generated skills вызывают этот launcher

Выбран **детерминированный adapter текста**, а не зависимость от PATH или глобального shim. Upstream templates на закреплённом SHA вызывают bare `openspec status`, `openspec instructions` и другие команды; такие вызовы не могут остаться в устанавливаемых skills.

Каждый effective skill содержит одинаковый краткий preamble: определить физический корень текущего Git checkout, запускать относительные команды с cwd этого корня и соблюдать session authority. CLI не выбирается из PATH. Детерминированное преобразование заменяет все исполняемые upstream CLI command sites на:

```text
node tools/openspec/run.mjs <original arguments>
```

В commands используется repo-relative launcher при обязательном cwd physical repository root. Это сохраняет переносимость hashes и узкую metadata restriction `Bash(node tools/openspec/run.mjs:*)`, без разрешения arbitrary Node. Сам launcher выбирает absolute verified binary; запуск public launcher по absolute path из nested cwd также проверен. Read workflow из root и из `modules/flights` проходит один launcher и один project root. На Windows и POSIX форма Node-команды одинакова. Аргументы/смысл workflow, skills names, artifact paths и ссылки на `$openspec-*` не меняются.

Adapter имеет exact version и allowlist command sites для **каждого** из шести skills: fenced/inline commands, instructional error/recovery invocations и invocation allowlist metadata, если она присутствует. Не применять произвольную замену слова openspec в prose/paths. Inventory фиксирует ожидаемые snippets/counts; неизвестный command site или оставшийся исполняемый bare openspec/npx/global-install site — failure. Изменение permissions metadata не даёт новых прав; authority по AGENTS/session сохраняется. При upstream drift не принимать новое дерево автоматически.

Init/update через launcher включает staging generation в task-owned scratch, используя exact published binary/core/codex/skills и чистый config. Scratch не получает feature corpus и не создаёт вторую фичу. Сравнить upstream outputs с утверждёнными hashes, применить adapter и проверить effective hashes **до записи** generated target skills. Применять только allowlisted OpenSpec files и marker; Travel skills не переписывать. Первичную project config создать/принять отдельно в рамках approved init с schema=spec-driven, сохранив уже существующий change; существующую config при update не перезаписывать. Generated scratch config не копировать поверх repository context. Root/Flights AGENTS и feature corpus hashes до/после должны совпасть. На update источником служит свежая генерация pinned package, а не повторное преобразование уже адаптированного текста.

В wrapper UI команды init/update обозначают этот staged workflow; это осознанная repo adaptation поверх CLI. Нельзя называть effective skills неизменёнными upstream-файлами. Реальная установка и offline CLI проверки выполнены; actual OpenSpec model workflow подтверждён; весь authenticated Travel verifier отдельно ожидает завершения.

Пример обычных read-проверок после разрешённого init:

```text
node tools/openspec/run.mjs --version
node tools/openspec/run.mjs status --change flights-m3-cancellation --json
node tools/openspec/run.mjs instructions apply --change flights-m3-cancellation --json
node tools/openspec/run.mjs validate flights-m3-cancellation --strict --json
```

`instructions apply` здесь только читает инструкции и не разрешает implementation. Для уже существующего change `new change` повторно не вызывается. Без `--force`, tool all, cloud workflow, stores и global settings. До adoption проверить selected schema/metadata через status; исправление metadata допустимо только внутри approved setup, без потери согласованных Markdown.

## Exact inventory и provenance

`.agents/skills` содержит ровно 8 прежних Travel directories, 6 effective OpenSpec directories и один physical marker:

- openspec-propose
- openspec-explore
- openspec-apply-change
- openspec-update-change
- openspec-sync-specs
- openspec-archive-change
- `.openspec-target`: content codex с разрешённой нормализацией line endings, не skill.

`.claude/skills` остаётся exact 8 Travel adapters. Существующие instruction pairs, roles/probe/sandbox/capabilities, legacy commands, stale-token/README, non-symlink/type/size checks сохраняются. `tools/ai-harness` сохраняет exact four-files invariant; launcher/adapter/manifest/test fixtures принадлежат `tools/openspec` и отдельно перечислены, без wildcard exemption. Generated metadata проверяется своей точной схемой, не ослаблением Travel Authority/workflow-ID схемы.

Manifest содержит package version/tag/integrity, codex/core/skills, exact paths/names/frontmatter, **upstream normalized hashes**, adapter version+command-site map и **effective normalized hashes**. После первого approved scratch generation сравнить upstream output с pinned source/templates и reviewed diff; только затем зафиксировать expected inputs. Validator не обновляет manifest. Package не требуется устанавливать для dependency-free проверки закоммиченного effective inventory; integration tests отдельно доказывают effective = adapter(проверенный upstream). Допустимая BOM/CRLF нормализация одинакова для обеих сторон.

Generated directories допускают только перечисленные physical files (ожидается SKILL.md); unknown profile/name/file/hash, symlink, command-site count drift — failure. Корпус фичи не копируется в `docs/superpowers`; repo spec skill получает только canonical routing для этого пилота после setup approval. Generated apply/archive не разрешают product edits, публикацию или archive без applicable session authority.

## Проверки подключения

Сохранить existing 90 unit cases и добавить negatives: missing/extra Travel или OpenSpec skill; неправильный marker/type/symlink; wrong upstream/effective hash; adapter drift; unconverted command site; неожиданный Claude adapter; version/profile mismatch; drift Travel authority/roles. Wrapper unit tests проверяют argument forwarding, physical root и scoped env/cleanup, в том числе путь с пробелами.

После approved install выполнить точный launcher command из **effective skill** из root и `modules/flights`: status и instructions JSON относятся к одной фиче, subprocess executable — pinned local package. Поставить harmless fake global openspec впереди тестового PATH: он не должен вызываться; отсутствие global binary также не мешает. Отсутствующий local package должен дать ошибку, а не перейти на fake global. Проверка process path/env — в test-only capture, не постоянный журнал пользовательских команд.

Repeated staged init/update даёт byte-stable effective output после согласованной newline normalization и не повреждает Travel/corpus/config. При неожиданном output остановиться с конкретным diff. CI/CD не меняется; dependency-free tests подключаются через существующий harness entrypoint с сохранением прежних tests. Новые CLI integration tests используют только локальный установленный package и temporary fixture, без supplier/paid calls.

Authenticated `verify:ai-harness:codex` по-прежнему требует разрешённого запуска и работает только со своим temporary thread tree. Его существующие probes остаются. Дополнительный acceptance включает не только skills/list discovery root/Flights cwd, но и **одну read-only команду через effective skill**, с доказательством pinned launcher execution. Offline command tests и authenticated agent workflow — разные уровни evidence; до второго нет заявления native runtime-proven.

## Один канонический процесс и публикация

Feature corpus находится только в `openspec/changes/flights-m3-cancellation`. baseline/research/review/process-log — evidence рядом; ADR — глобальные решения со ссылкой. После product acceptance синхронизация/архивация публикуется отдельным docs-only PR и проходит существующие обязательные checks до уборки; точный протокол в tasks.md §5. До завершения этого PR пилот не закрыт. s03a02 отдельно учитывает adapter/launcher effort и ручную pre-install подготовку, без предрешённого превосходства OpenSpec.



## Execution rulings T1–T2

Public CLI требует command-first syntax; isolated --version/--help допускаются, leading globals перед init/update запрещены. Полная read-only аргументная грамматика запрещает output/code-workspace/force/store flags, не только неизвестные command names. Все current read-команды идут в local root. Staged generation имеет отдельный controlled scratch path, direct upstream init/update не forwarding API.

Working-tree runtime proof: `npm.cmd run verify:ai-harness:codex -- --working-tree`. Default verifier продолжает проверять committed HEAD и явно печатает режим. Working snapshot допускает только T1/T2 files, запрещает symlinks/foreign/secret/traversal paths, не staging/commit. Baseline включает source bytes, Git HEAD/status, entire copied runtime footprint и prepared read-only CLI scope. Actual completed command execution+bound JSON обязателен; текст итогового ответа модели не заменяет proof. Head SHA и snapshot digest сообщаются отдельно.

Readonly CLI scope создаётся verifier в own ignored dependency subtree и передаётся через process-local environment. Source-installed package, version/integrity/runtime digest проверяются до copy; cloned runtime проверяется повторно. Не копируются чужие env/secrets/personal config. Cleanup удаляет только verifier-owned temporary clone/thread tree.

Project config намеренно поддерживает узкую dependency-free YAML грамматику: ровно один `schema: spec-driven`, затем необязательный `context: |`/`|-` с первым непустым текстом на двух пробелах. Comments/blank lines разрешены; context — непрозрачный literal text. Неизвестные/quoted/duplicate root keys, store/rules/flow/anchors/document markers и tabs отклоняются. Setup дополнительно использует pinned YAML parser и сохраняет принятый config. Расширение грамматики требует явного review политики, не ослабления regex ради установки.

Локальная evidence: `check:ai-harness`116/116; actual CLI integration8/8, включая prepared-scope/no-write regression; pinned version1.14.0; status/instructions apply/strict validate current change PASS. `status.isComplete=true` означает наличие planning artifacts, не реализацию продукта; `instructions apply.progress` отражает чекбоксы tasks. Biome affected files PASS без errors/warnings;31 useTemplate infos сохранены как необязательные стилевые рекомендации. Authenticated/native proof не подменяется этими результатами.
## Native acceptance: исторический blocker 2026-10-04

Этот раздел описывает промежуточный код до нового явно разрешённого single-status контракта. Актуальные права, evidence и оставшийся gate описаны в следующем разделе; старые отказы и удалённый prototype сохранены для анализа процесса.

Команда `node tools/openspec/run.mjs status --change flights-m3-cancellation --json` выбрана правильно; native execution record: status=declined, exitCode=-1, output=`rejected: blocked by policy`. Это не подтверждённый запуск pinned package. Данный отказ воспроизводится при обычном use_default/read-only вызове, с явным человеческим разрешением и при экспериментальном доверии только к own clone. Trust-эксперимент не помог и удалён.

На CLI0.147.0 configured gpt-6.1-sol завершала structured turn statusfailed/errorother; безопасный classifier=modelConfiguration. model/list возвращает gpt-5.6-sol(default)/terra/luna/5.5,6.1 отсутствует. Explicit `--model gpt-5.6-sol` применяется одинаково к root и всем literal probes, проверяется по catalog и selected root response; global/default model settings не изменены, automatic fallback отсутствует. [Official model/list contract](https://learn.chatgpt.com/docs/app-server) и [cache/catalog caveat](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference) прочитаны; actual inference success отдельно наблюдался.

Actual core evidence до отказа: typed repo skill discovery в root/Flights, structured migration skill input, project domain-modeler spawn с scoped request/raw+typed+role-bound evidence и bounded behavior. New OpenSpec discovery root/Flights также PASS. Две последующие literal migration-authoring/explore-domain CLI пробы не достигнуты: fail-closed workflow останавливается на actual OpenSpec command proof. Их unit cases сохранены; полный live success не заявляется.

Подготовленный opt-in on-request prototype не выполнялся live и удалён после independent review. Findings: произвольный wrapper basename/profile-loading нельзя использовать как authorization; accepted approval должен быть положительно связан с completed item; missing accept decision не является разрешением. Главное: thread readOnly attestation/null additionalPermissions не доказывают command-level sandbox после approval. [Official approval protocol](https://learn.chatgpt.com/docs/app-server) не использован как выдуманная гарантия. Никакого automatic accept, rule exemption, ignore-rules, workspace-write, dangerous flags или edits пользовательской execpolicy в том промежуточном состоянии кода не было.

Native gate закрывается только фактическим успешным выполнением в разрешённой read-only среде с pinned-command/bound JSON/no-unexpected-writes proof и полным remaining verifier. Следующее решение — обеспечить такую среду/permission contract; blanket разрешение или смена read-only guard не выводятся из consent на передачу контекста. Не запускать продукт T3 как обход этого gate.
Final proof hardening: любой shell-wrapper без независимо подтверждённой executable provenance отклоняется, даже при exit0/правильном JSON. Foreign pwsh/bash fake-output case observed RED→GREEN. Это отдельный source P2, обнаруженный после удаления approval prototype; теперь закрыт fail-closed, не blanket wrapper exception. Windows CLI0.147.0 wrapped record требует дополнительной verified provenance перед acceptance даже после разрешения policy. Пока поддержан прямой Node command record; limitation открыта явно. Source/discovery/standalone CLI validation не заменяют два неисполненных gate — native permission и wrapper provenance.

Current local evidence: full dependency-free harness122/122; actual CLI integration9/9 (включая Windows temp alias/prepared-scope/no-writes); total131. Original90cases preserved. Biome affected source files PASS with optional style infos; strict feature validation PASS. Full authenticated verifier FAIL/blocked by policy, не PASS.

## Актуальный single-status контракт и evidence, 2026-10-05

После понятного объяснения пользователь явно разрешил одно контролируемое выполнение `status --change flights-m3-cancellation --json` с возможным запуском вне read-only sandbox. Для этого существует отдельный opt-in `--allow-one-status-exception`; без него новый approval callback не активен. Это не разрешение на другие команды, изменение execpolicy/config, продукт или публикацию.

Core App Server probes сохраняют `approvalPolicy=never`, readOnly и network=false; исходные literal CLI probes сохраняют read-only args и семантические validators. Status использует отдельный owned thread с on-request, фиксированные canonical shell/Node/launcher/argv/cwd и executable hashes. Разрешается не более одного positively correlated plain accept после проверки immutable baseline; unknown/foreign/повторные запросы отклоняются, persistent/session/network amendments не выдаются. Thread-level readOnly не считается доказательством command-level OS sandbox после approval.

Actual CLI0.147 отображает command argument как JSON string с экранированными Windows-путями и кавычками. Decoder принимает только exact `JSON.stringify(JSON.parse(display))` round-trip либо прежнее plain quoting; это разбор display, без eval/spawn. Wrapper/NoProfile/verified executable/argv/cwd и completion binding сохраняются. Canonical display reproducer наблюдал RED→GREEN; foreign shell, missing NoProfile, extra command, noncanonical JSON escapes и прежние fake-output negatives сохранены. Independent narrow delta review PASS, no P1/P2.

На snapshot47files `ae7aef8d23e78de619334c553e49a0049dab3c55fa52380214ab42ddf9817831`, base31a3803a4d558199eec79b48dc8cf91e078ac681, выполнены core typed discovery/role probes, OpenSpec discovery root/Flights и actual fixed status command. Completed exit0, exact provenance/bound JSON и unchanged workspace/runtime/scope/Git проверены. Approval requests0/grants0; это наблюдаемые счётчики, не заявление о command-level sandbox. Source/default global rules не менялись. Digest относится к снимку до этой documentary ledger update; tooling code после probe не менялся.

Полный verifier после этого остановился на первой original literal `explore-domain` пробе: process timeout180s. Она и следующая `migration-authoring` не считаются пройденными; full T2 PASS не заявляется. Диагностический isolated rerun сохраняет исходный prompt/model/sandbox/timeout и те же47 snapshot bytes, без повторения core/status probes и без обхода validator. Результат фиксируется в process-log.md.

Current local evidence: harness129/129, CLI integration9/9, total138 distinct local tests; exact8 Travel skills/8 instruction pairs/5 agent pairs/5 legacy commands и6 OpenSpec effective skills сохранены. Biome affected source PASS, errors/warnings0, optional useTemplate infos. Strict feature validation и documentary consistency проверяются отдельно. Supplier/product/durable DB/runtime acceptance не выводятся из tooling evidence.

## Strict Windows default и ограниченный runtime budget

После отдельных controls чтение AGENTS.md completed exit0 и с login=false/NoProfile (41086ms), и с login=true (17459ms). Profile-cause hypothesis отвергнута; факт общего timeout не устанавливает иную первопричину.

В обычном Windows verifier fixed status теперь использует тот же verified absolute shell/Node/launcher plan и typed completion proof при approvalPolicy=never/readOnly/networkfalse, без commandApproval callback. Это attested provenance для стандартного Windows wrapper, без permission exception. При unexpected approval request сохраняется прежний protocol rejection. Explicit --allow-one-status-exception отдельно включает ранее согласованный on-request/single callback; наличие этого opt-in не расширяет права обычного режима. POSIX direct-record path сохранён. Bundled Windows PowerShell path должен существовать и пройти canonical/hash checks; неизвестный executable не заменяется PATH fallback.

Original literal explore-domain/migration-authoring prompts/tasks/args/model/cwd и result validators не изменены. Их ограниченный timeout180s увеличен до300s с явным именем этапа. Это бюджет дополнительной проверки активной работы, без отключения checks или утверждения причины/устранения сбоя. Core/status limits не расширены. Independent source/security re-review PASS, no P1/P2; full129 tests GREEN. Следующая full native попытка использует стандартный строгий режим без exception flag; actual result ниже по process-log, до него T2 остаётся открыт.

## T1–T2 acceptance, 2026-10-05

Actual npm.cmd run verify:ai-harness:codex -- --working-tree --model gpt-5.6-sol completed exit0 on CLI0.147.0. Verified snapshot47files digest f2fa651f82876f525f524328e51917c22e0ebab68bfe3ea0dd83e0b63c140c09, HEAD31a3803a4d558199eec79b48dc8cf91e078ac681. Core typed discovery/input, root/Flights OpenSpec discovery, exact project-role spawn/raw+typed+structured binding, actual status with verified shell/Node/launcher/cwd/JSON and immutable snapshot/runtime/scope/Git, both original literal skills, final no-write check and owned cleanup completed. Status never/readOnly/networkfalse, no callback, requests0/grants0. Literal budget300s, prompts/validators unchanged; no exception flag used.

Public protocol still does not return selected custom-agent TOML source path or encrypted V2 message plaintext; proof is not expanded into those unavailable claims. Discovery/model prose alone is not actual command proof. Local129/129+CLI9/9, strict and inventory evidence remain separate from native acceptance.

Explore-domain repeated stale module AGENTS one-way/return despite M2.5/ADR0025. This is a content-quality finding. Descriptive root/Flights instructions are corrected to actual 1–4 complete ordered legs/open-jaw/IANA time with historical readers preserved. Architecture/authority/auth rules unchanged. Native digest above predates this evidence ledger and description-only correction; verifier/tool/role/effective-skill code has not changed afterward. Current static/strict/documentary checks cover final text; every model-map assertion is not claimed independently verified.

Historical tooling checkpoint: T1–T2 connected/runtime-verified within this Windows/CLI/package/model scope. Product implementation was separate and unstarted at that checkpoint. It has since been implemented, accepted in existing CI and merged in PR36 as recorded in verification.md/process-log.md; real supplier/payout/rollout remain excluded. status.isComplete=true alone is still not implementation proof.

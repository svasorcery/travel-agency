# Trips M3: выбор модели и следующий этап оценки

**Решение пользователя2026-10-28:** платную реальную оценку пропустить; текущий бюджет внешних model API — **USD0**. Принят manual-only/defer вариант F. Qualification48 и последующий holdout не выполняются; task6.3 остаётся unchecked/not run, milestone22/23. Это отложенная проверка, а не отмена требований качества или разрешение включить M3. Никакой другой API, локальная модель, скачивание weights или использование подписки через automation не разрешены автоматически. Все прежние provider/budget варианты и drafts ниже — исторические предложения, не текущее разрешение на расходы.

> Current scope update2026-10-28: sections1–9 retain the options/planning record. Source/test preparation and explicit wire projection were subsequently authorized separately; see the follow-ups, source-preparation-verification.md and the current run review packet in section11. PR83 and documentary PR84 are merged; PR84/post-merge acceptance is recorded in section14. Earlier publication prohibitions describe their original phase; the current reconciliation permits no commit/push. Model/provider/prices/expenditure, real qualification and rollout remain unapproved. Earlier requests for source approval are historical, not a new permission gate.

Исследование вариантов выполнено2026-10-27; они сохранены как исторические предложения. Текущий выбор2026-10-28 — **платную оценку отложить, external API budget USD0**, см. §13. Модель/провайдер/тариф и исполнение не приняты. Реальные запросы к моделям/их API, чтение credentials/billing, quality execution и включение M3 не выполнялись. Source/test подготовка принята через PR83 и обычный PR/post-merge CI; актуальные механические проверки и границы — в [verification](source-preparation-verification.md). Существующий change остаётся **22/23**, task 6.3 — pending/not run; rollout и обработка настоящих пользовательских текстов — отдельные pending gates.

Записка продолжает [зафиксированный протокол и 48 fictional случаев](evaluation-plan.md), не изменяя корпус, oracle, prompt, schema, лимиты или критерии приёмки. База исследования — свежий `origin/dev=9ff60e42bb553214a22ed71fb4fa44b9d846c3d2` после merge PR82; Git tree `88ddfbfdbed87ee3c982dadb810e21613951f9bf`. Ветка подготовки: `codex/trips-m3-evaluation-options`. Эта записка не является execution authorization или результатом оценки.

## 1. Рекомендация и варианты

**Историческое предложение, отложенное после отказа от платной оценки: начать с одного direct Anthropic Haiku 4.5 (`claude-haiku-4-5-20251001`), 48 случаев, до USD 0.50, максимум 6 часов.** Причина выбора — совместимость с уже принятой границей исполнения, а не доказанное качество или предпочтение старых моделей вообще. Haiku 4.5 публично отмечен active/legacy; перед разрешением запуска надо снова проверить lifecycle и доступность именно этой версии. Условия доступа конкретного аккаунта, региона и цены счета здесь не проверялись. [Точная версия и lifecycle](https://platform.claude.com/docs/en/models/haiku-4-5/overview), [версионирование](https://platform.claude.com/docs/en/about-claude/models/model-ids-and-versions).

При неудовлетворительном результате — разобрать сохранённые ошибки; отдельно решить, нужен ли второй полный run на Sonnet 4.6. Никакого автоматического переключения модели, повторения случая или увеличения лимитов. Если Haiku 4.5 недоступен, остановиться до dispatch и вернуться к выбору; не подменять его alias или новой моделью.

| Вариант | API / точный кандидат | Применимость к текущему M3 | Роль в решении |
| --- | --- | --- | --- |
| A | Direct Anthropic, `claude-haiku-4-5-20251001` | Уже есть feature-specific `IChatClient`; extended thinking не запрашивается. Нужен offline wire/schema preflight, но новая provider-интеграция не требуется | Рекомендуемый первый кандидат; качество/3.5s deadline ещё неизвестны |
| B | Direct Anthropic, `claude-sonnet-4-6` | Тот же adapter; точные sampling/thinking/schema/usage параметры проверяются до запуска | Отдельно разрешаемый контрольный run; не обещание исправить ошибки A |
| C | Direct Anthropic, `claude-haiku-5-5` / `claude-sonnet-5-5` | **Не готовая замена model string**: sampling/thinking несовместимы с текущими настройками; нужна отдельная source-подготовка и fake wire-проверки | Современные альтернативы: Haiku 5.5 дешевле A, Sonnet 5.5 дешевле B; compatibility slice согласуется отдельно |
| D | Direct OpenAI, `gpt-6-luna` | Нужны отдельный capability adapter, реальные processor/policy metadata и usage/model-identity mapping; текущий runner привязывает policy к Anthropic | Альтернативный провайдер, если важен сам выбор OpenAI; сейчас не runnable |
| E | Paid Gemini Developer API, `gemini-3.5-flash-lite` | Нужны adapter, processor/policy и billing/thinking/model-version mapping; готовой интеграции нет | Альтернативный провайдер; free tier не считаем эквивалентом paid terms |
| F | USD 0: отложить исполнение, оставить ручную форму | Никаких новых runtime-зависимостей | Полноценный вариант, пока расходы/обработка не разрешены |

Текущий [extractor](../../../apps/Travel.AI/TripsBriefExtraction/TripsBriefExtractor.cs) отправляет `Temperature=0`, JSON schema и `MaxOutputTokens=1024`; `ReadResponse` отвергает любой non-text content. Haiku 5.5 требует убрать sampling-параметры и включает adaptive thinking по умолчанию; Sonnet 5.5 возвращает 400 при `temperature` и тоже включает thinking по умолчанию. Поэтому C требует конкретного изменения wire-настроек/совместимости, проверки учета всех billable tokens и сохранения text-only контракта. В этой записке такие изменения не разрешаются и не реализуются. [Haiku 5.5](https://platform.claude.com/docs/en/models/haiku-5-5/overview), [миграция](https://platform.claude.com/docs/en/models/haiku-5-5/migration-guide), [Sonnet 5.5](https://platform.claude.com/docs/en/models/sonnet-5-5/overview).

OpenAI документирует Structured Outputs и `none` reasoning для Luna; это не готовность нашего adapter. Gemini документирует structured output и thinking; stable identifier не принимаем за доказательство неизменяемой revision. Перед выбором D/E надо закрепить доступную точную версию и returned identity и проверить все условия M3 без paid probes. Общий AI core, agent framework, runtime provider routing и второй планировщик не нужны. [OpenAI Luna](https://developers.openai.com/api/docs/models/gpt-6-luna), [Gemini Flash-Lite](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite).

Bedrock/Vertex/Foundry не являются transparent заменой direct API: меняются processor, endpoint, договор и иногда тариф. Локальной модели в текущем runtime нет; её отдельная оценка потребовала бы weights/license, hardware budget и adapter. «Нет API-счета» не означает, что её исполнение или ресурсы уже разрешены.

## 2. Бюджет: расчет, software limits и разрешение

Ниже public list prices standard/global text inference в USD на дату исследования: без cache/batch/flex скидок, tools, налогов, платежных сборов, региональных надбавок и дополнительных запусков. Это **расчет максимального token envelope, не наблюденный счет или обещанная стоимость**. Перед реальным run цены/доступность перепроверяются и включаются в отдельный binding; `PricingVerified` сейчас не устанавливается. Источники: [Anthropic pricing](https://platform.claude.com/docs/en/about-claude/pricing), [OpenAI pricing](https://developers.openai.com/api/docs/pricing), [Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing).

Для каждого разрешенного вызова: `C = (4096 × input_USD_per_MTok + 1024 × output_USD_per_MTok) / 1_000_000`. Вход — весь envelope, включая prompt/schema/служебные поля; бюджет выхода обязан учитывать все billable reasoning/thinking tokens, а не только видимый JSON. Если провайдер/SDK не дает такого контроля и полного usage — кандидат не готов к оценке.

| Модель | USD/MTok input / output | C, USD/вызов | 48 случаев, USD | Дополнительные 24, USD |
| --- | --- | --- | --- | --- |
| Haiku 4.5 | 1 / 5 | 0.009216 | 0.442368 | 0.221184 |
| Sonnet 4.6 | 3 / 15 | 0.027648 | 1.327104 | 0.663552 |
| Haiku 5.5, до 100k input | 0.10 / 0.50 | 0.0009216 | 0.0442368 | 0.0221184 |
| Sonnet 5.5 | 2 / 10 | 0.018432 | 0.884736 | 0.442368 |
| GPT-6 Luna | 0.10 / 0.50 | 0.0009216 | 0.0442368 | 0.0221184 |
| Gemini 3.5 Flash-Lite, paid | 0.30 / 2.50 | 0.0037888 | 0.1818624 | 0.0909312 |

Строки C–E — сравнение economics при том же envelope **после** необходимой интеграционной подготовки, а не разрешение вызвать их текущим runner. Неизвестное usage/spend остается unknown; отсутствие ответа не означает нулевой счет. Software admission и accounting не являются глобальным invoice/fleet hard cap или доказательством отмены уже принятого провайдером запроса. Total cap ограничивает разрешенный token usage этого run; он не разрешает подписку, покупку credits, пополнение аккаунта или иные платежи. Такие условия аккаунта здесь не проверялись.

| Отдельное будущее разрешение | Максимум вызовов | Предлагаемый total cap | Предлагаемые per-request / rolling-hour-process caps | Время |
| --- | --- | --- | --- | --- |
| A: первая qualification, Haiku 4.5 | 48 | USD 0.50 | USD 0.01 / 0.10 | до 6h |
| B: контрольная qualification, Sonnet 4.6 | 48 | USD 1.50 | USD 0.03 / 0.30 | до 6h, отдельно |
| Более поздний независимый holdout для A | 24 | USD 0.25 | USD 0.01 / 0.10 | до 3h, отдельно |
| Более поздний независимый holdout для B | 24 | USD 0.75 | USD 0.03 / 0.30 | до 3h, отдельно |

Все суммы в этой таблице **предложены, не одобрены**. Для A `48 × 0.01 = 0.48 <= 0.50`, а окно из восьми полных резервов — `8 × 0.01 = 0.08 <= 0.10`. Для B — `48 × 0.03 = 1.44 <= 1.50`, окно `0.24 <= 0.30`. Существующие абсолютные потолки USD0.03/request и USD0.30/hour/process не повышаются. Снижение тарифа не отменяет зафиксированное pacing. Общие USD2.00/96 вызовов для A+B — арифметическая сумма двух возможных решений, не автоматическое разрешение второго run.

Региональные или иные надбавки пересчитываются до dispatch. Например, +10% к B дает `0.027648 × 1.10 = 0.0304128`, что не помещается в существующий per-request ceiling; такой профиль не запускается по этим условиям. Выбор другой модели или пересмотр контракта — отдельное решение.

Warmups, smoke-пробы на модели, schema-compilation calls, retries, repair calls и LLM-judge **не включены**: предлагается ровно один вызов на случай и до 48 всего, без дополнительных запросов. Anthropic предупреждает о первой компиляции JSON schema и кешировании grammar; холодный первый запрос остается в latency/quality результате. Нельзя скрыто прогреть schema или исключить неудачный первый случай. Публичная schema не содержит пожеланий/данных владельца. [Structured outputs: ограничения, компиляция и кеш](https://platform.claude.com/docs/en/build-with-claude/structured-outputs).

## 3. Что готово и что еще нужно перед исполнением

В source есть `RunAuthorizedAsync`, production extractor/normalizer seam, EF ledger и fake contract tests. [Runner](../../../tests/Travel.Tests.AiEvals/TripsBriefExtraction/TripsBriefEvaluationRunner.cs) — библиотечная функция, не xUnit test и не готовая M3 CLI-команда. Он не создает provider client и не ищет ключ. Запуск всего `Travel.Tests.AiEvals` не является командой M3: там есть другие платные оценки.

Следующая подготовка **без inference**, требующая отдельного разрешения на source/test работу:

1. Узкая opt-in точка входа для одного M3 run и source-readiness repairs из пунктов5–8: связать существующий runner, точный feature `TripsBriefChatClient`, `TripsBriefOptions` и новый owned disposable ledger. Не запускать публичные Host/UI routes и не включать target M3. Обновление SDK или provider-specific wire mapping, если потребуется, вынести в явный scope, не устанавливать автоматически.
2. Проверить на fake HTTP transport фактическую serialization через pinned `Anthropic12.20.0` / `Microsoft.Extensions.AI10.5.2`: model ID, `temperature=0`, отсутствие thinking/tools/cache-control, `max_tokens=1024`, JSON schema, `MaxRetries=0`, returned model identity и полный usage. Убедиться, что schema subset принятой версии SDK соответствует опубликованным ограничениям API. Fake проверка не доказывает прием запроса реальным провайдером.
3. На всех 48 неизменных входах проверить полный envelope и limits до dispatch, текущие source/prompt/schema/normalizer hashes, исходные oracles, output bound, дедлайны и accounting. Не удалять строки, которые не помещаются, и не упрощать schema/normalizer, чтобы новый провайдер «заработал» без пересмотра source.
4. На границе запуска фактически перепроверять source/config hashes и соответствие тарифов/лимитов между binding, pipeline options и ledger. Текущий runner проверяет форму SourceTreeSha256, packet/prompt/model binding, но не измеряет сам source tree и не создает конфигурацию — эту гарантию должен дать bootstrap; записанного digest недостаточно.
5. Исправить и проверить фактический pacing в существующем runner до платного run. Сейчас он ориентируется на абсолютные planned timestamps; oversleep/process pause может сблизить следующие actual starts. Нужны fake-clock/late-wait/pause regressions и минимум11s по monotonic elapsed между реальными dispatch, без catch-up burst. Общий6h stop также проверять по monotonic elapsed; UTC timestamps нужны для аудита/календаря, но перевод UTC назад не должен продлевать разрешение. Проверить UTC jump отдельно от fixture anchor. Ожидание — до создания request deadline/admission и начала измерения component latency; при выходе за6h/hour budget — stop, а не ускорение. Это отдельная необходимая source-подготовка, а не уже обеспеченное свойство bootstrap.
6. Добавить узкое сохранение controlled per-case evidence перед следующим dispatch. Сейчас durable только authorization claim и metadata EF ledger; Fields/Issues/results живут в памяти, поэтому process crash теряет уже оплаченные quality outcomes. Нужны bounded append-only/atomic+flushed attempt-intent (caseID/correlation/reservation/binding, до вызова) и result (normalized fictional fields/issues, status, usage/cost/duration, после ответа). Ошибка записи останавливает run; crash между dispatch и result оставляет explicit uncertain attempt, исходный denominator и запрет resume/retry. Не хранить raw text/prompt/completion, не использовать production plan store, не удалять authorization claim. Проверить write failure/crash boundaries fake-тестами. Receipt I/O отмечать отдельно от component timings и включать в общий6hwall clock.
7. Исправить границу component latency. Сейчас `milliseconds` фиксируется до `TripBriefCandidateValidator.Validate`, поэтому normalization/validation не входит в опубликованную extraction+normalization duration; после нее deadline повторно не проверяется. Измерять всю цепочку до завершения нормализации, повторно проверить absolute deadline, сохранять raw duration и controlled timeout/failure. Нужны fake-clock regressions с задержанной нормализацией и пересечением5s boundary; нельзя выдавать старое время только provider/pipeline за время всей цепочки. Planned waits и durable receipt I/O измерять отдельно и включать в wall-clock run budget, не исключая EF accounting из самой production pipeline.
8. Явно разделить уровни scoring/reporting. `UnsafeCases` сейчас вычисляется только для non-null validated candidate; отвергнутое предложение модели не увеличивает этот счетчик. Validator также восстанавливает deterministic issues/даты из текста, поэтому результат после него не равен исходному model proposal. Сохранять отдельные bounded категории отказа/отвержения и, где это безопасно наблюдаемо, признаки normalizer-added issues/derived fields и rejected unsafe proposals. Неизмеримый raw-уровень обозначать unknown/not-assessed, не safe/zero. Для такой диагностики не сохранять raw text/prompt/completion и не добавлять LLM-judge/новые запросы. Fake regressions: выдуманный city в H09, отвергнутый validator, и пропущенный model issue, восстановленный normalizer. Они не должны исчезать из объяснения результата или приписываться точности модели. Исходные normalized oracles и thresholds не меняются; diagnostic counters не становятся новым скрытым качественным порогом.
9. Предложение ресурсного scope для будущего owned worker+ledger: до 2 CPU / 2GiB RAM суммарно, до 256MiB writable evaluation data, 6h lifetime процессов worker/ledger. Это не срок хранения evidence: controlled receipts и authorization claims сохраняются после завершения/аварии; их очистка не происходит автоматически. Это не provisioning и не доказанная достаточность; offline preflight должен подтвердить возможность применить caps. Новые установки/загрузка образов, remote worker и облачные инфраструктурные расходы не разрешены автоматически. Существующие demo, DB, keys и human-review материалы не используются.

Проверка готовности нашла конкретные source gaps: late-start pacing, crash-safe per-case evidence, полная component duration и разделение model/pipeline scoring пока не обеспечены существующим runner/reporting. Ни одна из этих source-подготовок не сделана данной запиской. До отдельного execution authorization — ноль real dispatch, runtime flags и model/pricing/processing settings остаются off/unset. Для будущего fictional-only worker внутренние разрешающие options потребуются только внутри явно ограниченного процесса; это не разрешение на пользовательский трафик, private-text rollout или изменение checked-in конфигурации.

## 4. Заморозка и минимальная передача данных

До первого output закрепить точный provider/endpoint/model ID, processor terms, все параметры/цены/лимиты, hash корпуса/протокола/embedded prompt/schema/normalizer/source и SDK/lock identity. Для A использовать датированный snapshot, не `claude-haiku-4-5` alias; для Sonnet 4.6 dateless canonical ID является pinned snapshot по документации Anthropic. Model identity в ответе должна совпасть с priced selection; mismatch не тарифицируется догадкой и не приводит к fallback.

Зафиксированный corpus: D01–D24 и H01–H24; обе части открыты, H — qualification, **не holdout**. Default fixture anchor `2026-10-27T21:30:00Z`; зоны Moscow/Kaliningrad/null и все ожидаемые значения остаются как в корпусе. Runtime clocks для deadline/admission/ledger/monotonic duration реальные; fixture anchor не становится временем исполнения или публичным client override.

В processor request — только заранее опубликованный fictional text, locale, reference anchor/zone/localDate, versioned prompt и schema. Не передавать owner/TripId/JWT, сохраненный план, реальные notes, историю, demo DB или пользовательские пожелания. В приложении не включать payload/SDK/HTTP/bus tracing, prompt/response caches или сырые provider exception logs. В отдельный private run receipt экспортировать только controlled normalized fictional results, case IDs, метрики и разрешенную metadata; сырые completions остаются transient. Metadata ledger не содержит text/candidate.

| Провайдер | Что известно по официальным условиям | Что не считаем согласованным |
| --- | --- | --- |
| Direct Anthropic | Commercial API: стандартная политика описывает удаление input/output в пределах 30 дней с исключениями; API data не используется для обучения без отдельного соглашения. ZDR — отдельная применимая договоренность | Наличие ZDR/доступа у аккаунта, регион/правовые основания, любое настоящее private text |
| OpenAI API | Training не включается без opt-in; abuse-monitoring content может храниться до 30 дней с исключениями; storage/application-state и ZDR controls рассматриваются отдельно | Что `store=false` автоматически дает ZDR, наличие account approval или готового adapter |
| Gemini Developer API | Paid terms не используют prompts/responses для улучшения продуктов; unpaid terms могут допускать такое использование и human review, с региональными исключениями | Что free tier эквивалентен paid, его фактические настройки/доступ и сроки/условия конкретного аккаунта |

Источники: [Anthropic retention](https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data), [API training/deletion](https://privacy.claude.com/en/articles/7996875-can-you-delete-data-that-i-sent-via-api), [OpenAI data controls](https://developers.openai.com/api/docs/guides/your-data), [Gemini terms](https://ai.google.dev/gemini-api/terms). Эти документы не заменяют принятие условий/разрешение пользователя и не являются проверкой его credentials/billing. Внешний processor может иметь свой schema/cache/retention механизм; отсутствие text в наших логах не означает физическое стирание у processor.

## 5. Один будущий qualification run

Порядок уже зафиксирован, его не ускоряем по более низкой цене модели. `R0` ниже — настоящее время начала разрешенного run; это **не** fixture anchor корпуса.

| Окно | Время относительно R0 | Случаи |
| --- | --- | --- |
| 1 | 00:00 | D01–D08 |
| 2 | 01:05 | D09–D16 |
| 3 | 02:10 | D17–D24 |
| 4 | 03:15 | H01–H08 |
| 5 | 04:20 | H09–H16 |
| 6 | 05:25 | H17–H24 |

Требование после исправления pacing: внутри окна — не менее 11s между actual starts по monotonic elapsed time, один active/no queue, исходные process/owner limits не ослабляются. Последний плановый start около R0+05:26:17; hard wall-clock cap — 6h. Planned waits происходят до admission и не входят в inference latency. Только один owned evaluation responder, без другого M3 traffic; restart не обнуляет run budget.

До каждого dispatch перепроверяются authority/binding, наличие полного оставшегося бюджета и wall-clock, stop marker и все исходные input limits. Сначала committed Admitted/unknown metadata row, затем не более одного provider request. Лимиты неизменны: 1000 Unicode scalars/4096 UTF8 bytes текста, 4096 total input tokens, 1024 output tokens, 16KiB response; inference3500ms, AI handler4500ms, whole component budget5000ms. No retry, no JSON repair, no auto fallback, no automatic resume.

После готовности source-prep gates из §3 unknown usage/cost/model, потерянный ответ, accounting/receipt failure, отмена, interruption или исчерпанный бюджет останавливают run; оставшиеся IDs становятся NotRun в исходном знаменателе. При аварии уже dispatched-but-unsettled случаи отмечаются uncertain, а не NotRun/нулевой счет. Отмена/timeout не доказывают отсутствие счета. Admitted reservation сохраняется; lease остается занят до settlement незавершенной SDK/ledger задачи. Нельзя повторять сомнительный вызов или запускать run с тем же authorization заново.

Обычная известная ошибка извлечения/refusal/invalid JSON с полным учтенным usage считается провалом этого случая; она не исправляется еще одним запросом. Из-за реального policy/privacy/action нарушения run останавливается через существующий stop/cancellation boundary, сохраняя все прежние результаты и NotRun. Наблюдение потенциально опасного принятого значения означает failed safety acceptance, а не повод смягчать validator. Не обещаем, что текущая библиотека автоматически прекращает цикл по любому quality mismatch.

Для claim авторизации нужен stable approved receipt directory: предлагаемый будущий путь `D:/_Projects/_github/travel-agency/out/trips-m3-evaluation/receipts`, сейчас не создается. Atomic one-shot claim делается до dispatch и хранится после success/failure/crash. Другой RunId не разрешает reuse; перенос/очистка этой директории требует новой authority. После остановки только новое явное разрешение может назвать exact remaining IDs; оно не стирает первый run, не повторяет uncertain IDs и не уменьшает denominator48.

## 6. Решение по результату, без rollout

Сохраняются исходные критерии [evaluation-plan §3](evaluation-plan.md#3-predeclared-metrics-and-thresholds):

- H safety: ноль invented city/date/count/money, unmarked contradictions в H07/H09/H12–H16/H18–H19/H23, unsafe actions и privacy disclosures. Safety не усредняется с успешными строками.
- H exact match: минимум **22/24** полных normalized slot state/value и required issue-category matches. Неверные дополнительные issues на безопасном тексте — ошибки.
- Usefulness: минимум **11/12** заранее eligible H01–H06,H08,H10–H11,H17,H20,H22. Пустой/missing-everywhere ответ, timeout, refusal, malformed/fallback не выигрывают эту метрику.
- Component pipeline p95 ≤5000ms при inference≤3500ms и handler≤4500ms; все timeouts, raw durations/max/sample size и planned wait/full-run time видны. Timing excludes actual Host/NATS/browser; успешный in-process run не доказывает end-to-end SLA. Холодная schema compilation не вычеркивается.
- Известные usage/spend помещаются в разрешенные caps; unknown остается unknown и cost acceptance не проходит. Отдельно D/H, confusion matrix, per-slot accuracy и unsafe/omitted issue counts; нет LLM judge или человеческого score, выданного за исследование пользователей.

Граница вывода: исходные22/24 и11/12 оценивают нормализованный результат всей extraction+validation pipeline на открытом корпусе. `UnsafeCases=0` говорит только о проверенных кандидатах в текущей реализации, не об отсутствии галлюцинаций в отвергнутых provider proposals. Отдельные raw/rejected diagnostics и normalizer-added facts показываются без приписывания их модели; если они не измерены, это явно not-assessed. Contract error/refusal/fallback остается ошибкой exact/usefulness в исходном denominator, а не качественно успешным безопасным ответом. Никакой новый raw-модельный threshold или oracle этой запиской не вводится.

`Passed` существующего runner также строже краткого H-summary: `UnsafeCases` агрегируется по D+H и всем extracted slots, а duration guard требует, чтобы каждый dispatched sample уложился в5000ms. Эти дополнительные guards показываются отдельно от H metrics, сохраняются и не ослабляются ради прохождения модели. До исправления timing/reporting один boolean `Passed` нельзя выдавать за доказательство всей заявленной оценки.

Полный исходный denominator48 сохраняется даже после early stop. Формально прошедшая qualification говорит только об этом опубликованном fictional корпусе. Не меняем oracle после outputs; дефект oracle требует новой версии корпуса и отдельного решения. Модель/тариф/prompt/normalizer не меняются внутри run. Если понадобится tuning по D или смена модели, нужна новая замороженная конфигурация, бюджет и полный новый report с сохранением прежних неудач; повторный H по-прежнему qualification.

После выбора одного frozen кандидата независимый custodian должен отдельно подготовить и запечатать **новый24-case holdout** с 12 useful cases и hazard/oracle labels после source/prompt/model freeze и до его outputs. Он сейчас не создан, не открыт и не включен в 48 calls/бюджет. Позднейшее разрешение на24calls/3h оценивает один выбранный config без tuning access с теми же safety/22-of24/11-of12 thresholds. Механический synthetic24 fixture из source не заменяет его.

Даже два успешных fictional этапа не включают M3: отдельными остаются model/private-text processing, actual full-path real-model acceptance, target/topology/spend/rollback и явный rollout. Ручная форма и самостоятельная детерминированная генерация продолжают работать по прежним правилам. Human A/B, field calibration, original M2 rollout6.3 и Moscow v2 не закрываются.

## 7. Что должен выбрать пользователь перед следующей работой

Это исторический перечень решений для возможного model run. Source/test подготовка впоследствии завершена; текущий выбор — defer F/USD0 (§13). Выбор платной модели и выполнение списка ниже не являются следующим разрешённым шагом.

1. Путь подготовки: A (минимальный direct Anthropic Haiku4.5) или отдельный compatibility slice для C/D/E. Рекомендация A не является выбранным провайдером. При недоступности legacy версии не переключаться молча.
2. Отдельно разрешить узкую source/test подготовку runner bootstrap и resource/preflight scope из §3 — без inference. Она не готова только потому, что evaluator library существует.
3. Лишь после review bootstrap/binding — отдельно разрешить **fictional-only A: до48calls, total≤USD0.50, request≤USD0.01, rolling-hour-process≤USD0.10, wall-clock≤6h**, точные provider terms/model/rates/receipt path и frozen hashes. Никакого warmup, второго model run, holdout или настоящего private text в этом разрешении.

Исходный запрос на варианты разрешал только подготовку вариантов/бюджета/порядка. На том этапе model/provider/expenditure не были согласованы; task6.3 остается unchecked. Commit/push/PR, модельные запросы, credentials/billing и target activation этим документом не выполняются.

## 8. Baseline fingerprints для последующей сверки

Это исторические raw SHA256 planning worktree на базе9ff60e4, включая его CRLF bytes; не canonical Git/embedded identities и не готовая execution binding. Актуальные LF identities и сравнение frozen inputs приведены в source-preparation-verification.md. Перед будущим run надо пересчитать hashes после любой разрешенной source-подготовки, сравнить embedded resource bytes, зафиксировать полный source tree digest с описанным алгоритмом и все runtime parameters/lock identities. Одна синтаксически корректная строка SourceTreeSha256 в API runner не доказывает identity исполняемой сборки.

| Файл относительно корня repo | Raw SHA256 |
| --- | --- |
| openspec/changes/trips-m3-brief-confirmation/evaluation-plan.md | `a6f5a4fd1c511d75d8019d0cc09ca2fdef64b0d670ff1940085e2638fe51ea3e` |
| tests/Travel.Tests.AiEvals/TripsBriefExtraction/qualification-cases.json | `c6152a8201fc24bc5a12704b59b58b3bcc3c56ec4e571bbffb046aedc7283bf9` |
| prompts/v1/trips/brief-extraction.system.md | `cf5e58130dd93c0d61faaeec67dc47d3053ffe6f2c61386ff8276b652408ac51` |
| apps/Travel.AI/TripsBriefExtraction/TripsBriefExtractor.cs | `8c72dd279d04487c2b0856ca6c8b81416480e25971f78d55cb30410099fd6cd0` |
| modules/trips/Travel.Modules.Trips.Application/BriefExtraction/TripBriefCandidateValidator.cs | `04a4bdf8d8bdf165687a0305b99928a6dd9cba6096e4dca82f4d18ec9bb0f1c1` |
| modules/trips/Travel.Modules.Trips.Application/BriefExtraction/TripBriefDateNormalizer.cs | `5cf8716de8cfb90be743cc459bd09236873a62e243eed618809a9bcb55d5d491` |
| modules/trips/Travel.Modules.Trips.Application/BriefExtraction/TripBriefLexicon.cs | `a0a02d4d0dcfbbd7cd5a425745621b01c02d89058a7bbfd68dd19a75f9be29ed` |
| shared/dotnet/Travel.IntegrationContracts.AI/TripsBriefExtraction/TripsBriefExtractionContracts.cs | `a36aec5c57d60b5054e9ecc0f864ee2870450703cb75fac6291dd6f0fcca36eb` |
| tests/Travel.Tests.AiEvals/TripsBriefExtraction/TripsBriefEvaluationRunner.cs | `e54746a1e79a16d5b399c663f3d42f05943bc4c859de760a468717836eaef6e1` |

Canonical source rows SHA256 внутри machine packet: `ed0ef5cb4a3a407b4b4d988bf4be466ae79ec549b129f7e2f0fa2f75de446052`; это другой digest, не raw file hash. Полный вход содержит ровно D24+H24, 12 useful H и 10 hazardous H; обе части открыты, модельных outputs нет. Корпус, evaluation-plan и prompt в рамках этой записки не редактируются.

## 9. Проверка этой записки

Первый независимый builder-check нашел два P2 source-readiness gap: actual-start pacing после паузы и отсутствие durable per-case quality evidence. В §3/§5 они включены в обязательную будущую source/test подготовку; это исправление плана, не исправление или принятие текущего кода. Scoped recheck этой ранней версии плана, до следующего саморевью: `nothing important left`. Выбор модели/процессора, source/test authority, расходы и execution по-прежнему не согласованы.

Механическая самопроверка записки: все6строк budget table пересчитаны точно через целочисленную арифметику, четыре предложенных budget profiles помещаются в per-request/hour/total envelopes; все9baseline hashes неизменны. В packet по-прежнему48случаев, D24/H24,12useful H и10hazardous H. Task checkboxes22/23 побайтно соответствуют базе, единственная unchecked —6.3; все локальные Markdown file links разрешаются. Изменены только эта записка и ссылка на нее в tasks.md; исходные evaluation-plan/corpus/prompt/source/configuration и historical demo/human materials не изменены. Ни model outputs, ни measured cost/latency/quality не созданы.
### Последующее саморевью

Статический разбор runner выявил дополнительный timing gap: начало измерения на строке269, фиксация duration на294, а normalization —337. Кроме того, `Unsafe` вызывается лишь для принятого candidate (344–345), а validator возвращает deterministic issues/derived fields, поэтому отсутствие accepted unsafe не равно отсутствию raw model errors. В обязательную будущую source/test подготовку добавлены полное измерение до/после normalization и честная атрибуция model/pipeline результатов. Фактический predicate `Passed` по D+H/all-sample duration теперь описан без ослабления.

Также уточнены сравнения стоимости моделей5.5 с соответствующими A/B и граница6h resource lifetime против сохранения durable receipts. Ни одна source-проблема не объявлена исправленной: изменен только план будущей подготовки. Корпус, prompt, oracle, существующие acceptance thresholds, лимиты и task checkboxes сохраняются. Source/test подготовка, расходы и real execution требуют прежних отдельных разрешений.

## 10. Исполнимый source/test план

[Пять задач подготовки evaluator](source-preparation-plan.md) фиксируют файлы, единые интерфейсы, RED/GREEN проверки и порядок P1→P5. Это prerequisites существующей pending6.3, а не разрешение на модельный run и не новые принятые milestone tasks. Контекстного выбора провайдера/расходов не произошло. План подготовлен2026-10-28; к реализации не переходили. После отдельного source/test approval предлагается native выполнение и независимый итоговый review. Главный task counter остается22/23,6.3unchecked.

## Source/test follow-up2026-10-28

Source подготовка разрешена отдельно и ведётся от fresh9ff60e4 в codex/trips-m3-evaluator-preparation; публикация запрещена. Первоначальные wire/budget blockers сохранены в исторических receipts. Пользователь отдельно согласовал явную wire-проекцию при строгой локальной валидации. Реализованы trips-anthropic-wire-v1 и единый Linux envelope worker+PG: CPU2/RAM2GiB/swap0, tmpfs192MiB + persistent archive64MiB =256MiB. Реальные SDK fake-wire и kernel source tests описаны в [verification](source-preparation-verification.md). Pure preflight не объявляет runtime budget доказанным; worker проверяет его до key handshake. Clean source, точные worker closure/image/wire/policy hashes и отдельное разрешение остаются обязательными. Ни модель/провайдер/цена/бюджет, ни48qualification/24holdout/private text/target/rollout не согласованы. На этом раннем source этапе baseline Storybook/deepmerge gate был failed; последующий отдельно разрешённый dependency follow-up ниже фиксирует исправление.

## Dependency follow-up2026-10-28

The user separately authorized minimal dependency remediation. Its source/installed
proof, fresh audit classification, Storybook build and independent review are GREEN;
see [receipt](../../../docs/operations/2026-10-28-deepmerge-security-backport.md).
Earlier failed baseline statements describe the prior stage. Qualification48,
provider/model/verified prices/cost authority, later holdout and target/rollout are
still unapproved; no credentials or model execution occurred. Root package/lock
changed under this new scope and require new future execution hashes.

## 11. Пакет первого запуска: подготовка без исполнения

**Актуальный повтор:** source-only worker/image preparation теперь успешен на чистом d318cb49; новый по-прежнему неисполняемый draft и его preflight находятся в [§12](#12-повтор-source-only-подготовки-worker). Ниже сохранены fingerprints и failed receipt первой попытки; они не заменяются успешным повтором.

Дата2026-10-28. Пользователь разрешил пункт1 предложенного следующего шага: documentary reconciliation, параметры/fingerprints и preflight. Это **не разрешение** на модель, расходы, чтение ключей/billing, target или включение M3. [PR83 и post-merge CI](publication.md#pr82-documentary-closure-and-pr83-evaluator-preparation) приняты. Подготовка ведётся в новой ветке `codex/trips-m3-run-packet`, clean base=fresh origin/dev=`d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f`. В момент подготовки пакета working tree содержал documentary edits: приведённый ниже Clean=true относится **к проверке до этих правок**, а не к будущему исполнению. После документационного коммита его Commit/SourceTree отличаются от сохранённых identities d318cb49, даже если новая рабочая копия чистая.

### Предлагаемый объём одного решения

| Параметр | Предложение / статус |
| --- | --- |
| Профиль | Existing direct Anthropic wire; кандидат A `claude-haiku-4-5-20251001`; модель и processor не выбраны пользователем |
| Данные | Ровно D01–D24 затем H01–H24 из существующего packet;48 openly known fictional cases; без private text, нового holdout и tuning внутри run |
| Вызовы и деньги | До48 SDK starts; total≤USD0.50, request≤USD0.01, rolling-hour process≤USD0.10; все лимиты предложены, не разрешены |
| Тариф | Исторический comparison1/5 USD per MTok даёт4096×1+1024×5 = USD0.009216/request и USD0.442368/48; текущие цены/lifecycle/terms и доступность аккаунта требуют проверки до approval; это не billing proof |
| Inference | Политика: полный input envelope≤4096, output≤1024 tokens/16KiB; source/fake sender-shaped estimate не доказывает actual provider tokenization; temperature0;3500ms inference,4500ms AI handler; без tools/thinking/cache directives/repair/retry/warmup/LLM judge |
| Расписание | Шесть окон по8 случаев на0/65/130/195/260/325мин; реальные SDK starts разделены≥11s;≤6h включая setup, normalization и journal I/O, также не позже одобренного expiry |
| Общий runtime budget | Worker+PG+normalization+evidence: CPU2, RAM2GiB, swap0; tmpfs192MiB + единственный persistent archive64MiB =256MiB writable. Host/build/Docker control plane вне этого cap; supervisor имеет≤5s kill grace |
| Failure | InvalidOutput/refusal/normalizer rejection/component deadline с известным cost — failed outcome отдельного случая; run может продолжиться, результат остаётся в denominator. Binding/authorization change, unknown accounting, budget, journal/storage, worker loss или общий deadline → остановка run с uncertainty/not-run. Без fallback на другую модель, repair, retry или resume |
| Privacy | Только fictional packet; текст+reference anchor+schema/prompt, без owner/TripId/history и отдельных budget/currency значений из формы. Тексты корпуса содержат fictional money wishes для проверки unsupported; это не настоящие пользовательские данные. Raw text/prompt/completion/key/exception не сохраняются в app logs, cache или receipts. Processor retention/schema cache требуют отдельного принятия условий; локальный no-log не доказывает их отсутствие у processor |
| Результат | Полные per-case normalized results/issues, controlled diagnostics, usage known/unknown, cost, timing и stop reason; immutable index остаётся Passed=false/FinalizationPending, пригодность подтверждается только matching returned PostFlushVerdict с index hash |

Числа и порядок не меняют [исходный протокол](evaluation-plan.md): H exact≥22/24, usefulness≥11/12 заранее помеченных eligible H, zero unsafe и пропущенных hazardous issues; все48 case outcomes учитываются, принятые D+H safety/timing/cost checks сохраняются. Pipeline p95≤5000ms с normalization, без запланированного ожидания; это не Host/NATS/browser SLA и не independent generalization. Fake tests не доказывают реальное качество. Holdout24 ещё не authored/sealed и не входит в этот пакет/бюджет.

### Реальный preflight и fingerprints

Tool собран из чистого d318cb49: SDK10.0.301 (repo global10.0.203/latestFeature), Node26.11.1; pinned OpenSpec1.14.0; текущие Anthropic12.20.0/Microsoft.Extensions.AI10.5.2/Testcontainers.PostgreSql4.11.0. Release build успешен:0warnings/0errors; compiled artifacts находятся в ignored task-owned `D:/_Projects/_github/travel-agency/out/m3-run-packet/build`.

Запуск готового DLL с `preflight --repo-root .` вернул exit0, Clean=true, ReadyForExecution=false, ProviderCalls=0, SelectionAndAuthorizationPending. [Нормализованная копия JSON](verification/run-preparation/preflight-clean-base.json) содержит observed identities; exit0 означает успешную инспекцию, а не разрешение на запуск. SourceTree — SHA256 **raw git ls-tree -rz --full-tree HEAD bytes**, а не Git tree SHA1; BuildClosure относится именно к проверенной локальной сборке, не к будущему publish.

| Identity | Наблюдённое значение |
| --- | --- |
| Commit | `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f` |
| SourceTree | `4eac07820cd056f6f8c6a55de7652ba49e4f87db3ab925576357c3a3566c16a8` |
| Packet | `c6152a8201fc24bc5a12704b59b58b3bcc3c56ec4e571bbffb046aedc7283bf9` |
| Prompt | `db7429e3ad0eeb0250dd55f40dbe4f28562bc26df4b9d613d64c11f8dc8b361d` |
| Schema | `93b2b1ac6f3d88ce385445bc5abd5187fbc2d0486221e53593a2be66e7c1342c` |
| Normalizer | `a23d18edccc2329b91392a9852f9cca7164489440f735691591123a127bf9ce1` |
| Lexicon | `a0a02d4d0dcfbbd7cd5a425745621b01c02d89058a7bbfd68dd19a75f9be29ed` |
| Packages | `d33952c5c029a9d51b9093cdfae6b1d655a2a330a35cf7ed93087b22c697b3e6` |
| BuildClosure | `cf09513f25b4bc52dd56c9db0963cbe5094ab523a692f4d84d447f29c05a4f4e` |
| Clean | `true` |
| WireSchema | `a7308ab32226191f44d4057ef373241db50d65e6c23a0b7698312e98d401621a` |
| ResourcePolicy | `cb68d2cfdc45610c2eebf6415bdd12745d593249181ba9e772206161e615df85` |
| WorkerClosure | `(not prepared)` |
| WorkerImage | `(not prepared)` |

Дополнительные raw file SHA256, не embedded/runtime identity:

| Файл | SHA256 |
| --- | --- |
| openspec/changes/trips-m3-brief-confirmation/evaluation-plan.md | `6fedd9ab67a19da3a2017721bab887c327929ba938008e8e827d8f0593832fe8` |
| tests/Travel.Tests.AiEvals/TripsBriefExtraction/qualification-cases.json | `c6152a8201fc24bc5a12704b59b58b3bcc3c56ec4e571bbffb046aedc7283bf9` |
| prompts/v1/trips/brief-extraction.system.md | `db7429e3ad0eeb0250dd55f40dbe4f28562bc26df4b9d613d64c11f8dc8b361d` |

Source-only `prepare-source` опубликовал worker-файлы, затем завершился exit2/SourcePreparationFailed: Linux engine Docker недоступен (pipe dockerDesktopLinuxEngine отсутствует). [Контролируемый результат](verification/run-preparation/source-preparation-failed.json) не содержит raw exception. Docker не запускался/не перезапускался, container/model/key не создавались и demo не трогалось. WorkerClosure/Image **не подтверждены этим этапом**; существующие pinned base images: aspnet `sha256:0fa044f682cb7d93a5a90401a00c626c66f7b00b86922be9441f869eae039f80`, pgvector `sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`. Это source pins, не доказательство наличия или identity составного образа. Ранее принятые PR83 fake/kernel probes сохраняют историческую силу; текущий preflight их не повторяет и не заменяет runtime handshake.

### Черновик manifest, который нельзя исполнить

[qualification-manifest.draft.json](verification/run-preparation/qualification-manifest.draft.json), SHA256 exact UTF8/LF bytes: `af2ad8a35d9c28179ebc1f0032a69c83a5e59906d39f87aafcc8901eb8df98a7`. Файл соответствует strict manifest v1 shape,≤64KiB, но намеренно не авторизован: Authorized=false, RunId empty, AuthorizationId/model/pricing/policy empty, rates0, evidence root null, expiry1970-01-01. Нулевые rates — **pending placeholders**, не бесплатный тариф. ProcessorId/Endpoint описывают только поддержанный candidate A adapter, не пользовательский выбор. Предложенные cost caps не дают права расхода.

Preflight **с этим exact draft** также завершился exit0/Clean=true/ProviderCalls=0/ReadyForExecution=false; [результат](verification/run-preparation/preflight-draft.json) содержит SeparateAuthorizationRequired, ModelSelectionPendingOrChanged, PriceLimitsOrPolicyChanged, ReservationNotCovered, BoundWorkerPreparationRequired, AuthorizationExpired и StableEvidenceRootRequired. Ни execute, ни negative execute для проверки защиты не запускались. Этот SHA позволяет проверить черновик, но не должен использоваться как одобрение исполнения. После documentary edits [preflight документальной рабочей копии первой попытки](verification/run-preparation/preflight-working-documents.json) также вернул exit0/ReadyForExecution=false/ProviderCalls=0, теперь Clean=false и дополнительный ArtifactsDirtyOrChanged. Это ожидаемая блокировка изменённого дерева; исторический clean receipt выше не выдаётся за текущее состояние.

### Порядок до первого model dispatch

Сохранён как условный порядок на случай нового отдельного решения. После выбора defer F/USD0 этот список не выполняется.

1. Выбрать точный processor/model и принять актуальные публичные terms/retention/region/цену; проверить совместимость current wire без paid probes. Никаких credentials/billing в подготовке; если Haiku4.5 недоступен — остановиться для нового выбора, без alias/fallback.
2. Source-only prepare-source уже повторён на чистом принятом d318cb49, см. §12. Для будущего model execution подтвердить выбранный committed source и пересчитать все identities, если он или binaries/worker/image изменились; проверить budget/путь evidence. Публикация текущих документов изменит Commit/SourceTree и требует нового binding. Копировать старые hashes или считать эту dirty ветку готовой нельзя.
3. Согласовать physical evidence root без links, новый RunId/AuthorizationId, точный scope48 IDs, caps и expiry. После подготовки отдельного финального manifest прогнать preflight и дать пользователю его exact byte SHA и полные параметры. Authorized=true само по себе не является человеческим разрешением; положительный static admission не заменяет kernel readiness.
4. Получить **отдельное явное разрешение на этот exact manifest** и fictional-only model processing/spend. Только затем допустим execute с confirm-manifest-sha256; key lookup ограничен существующим TRIPS_EVALUATION_ANTHROPIC_KEY после repeated admission и WORKER_READY. Ни один Host/UI feature flag не меняется.
5. Выполнить один run без warmup/extra calls; сохранить failed/uncertain/not-run, сверить index/PostFlushVerdict и разобрать полные результаты. Не менять corpus/oracle/prompt/порог после outputs. Любой второй run, новая модель и позже holdout24 требуют нового решения; rollout остаётся отдельным gate.

Пакет для review подготовлен; **разрешения на исполнение и полного runtime binding ещё нет**. Результаты quality/cost/latency отсутствуют, task6.3 остаётся unchecked и общий milestone22/23. Commit/push/PR/merge этим этапом не выполняются.

## 12. Повтор source-only подготовки worker

Пользователь2026-10-28 попросил повторить подготовку. Объём прежний: source-only publish/image и preflight, без модели, ключей/billing, включения M3, commit/push. Проверочная копия `C:/Users/Vladimir_sva/.codex/worktrees/trips-m3-worker-binding/travel-agency`, новая ветка `codex/trips-m3-worker-binding`, содержит чистый accepted source `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f`. Документальные изменения остаются отдельно в codex/trips-m3-run-packet; source ветка после подготовки чистая. Никакой fake clean, stash или commit для обхода admission не использован.

### Docker recovery и successful preparation

Docker Desktop был выключен. После фонового запуска backend застрял на init API docker-desktop: API500, init ping timeout. В рамках ранее явного разрешения на Docker recovery выполнен один scoped restart: graceful stop с20s timeout не завершился, штатный Docker Desktop force-stop прошёл, затем завершён **только** WSL-дистрибутив docker-desktop и Desktop запущен в фоне. Engine ответил версией29.8.2. Не было глобального WSL shutdown, удаления images/cache/containers/volumes или управления Ubuntu; retained demo/target не запускались нами. Рестарт общего engine не является обещанием непрерывности чужих Docker workloads.

Повтор готового принятого controller DLL выполнил `prepare-source` из clean source и завершился exit0. Это publish и подготовка образа, **не запуск evaluation worker/PG или model run**. Исторический SourcePreparationFailed JSON сохранён. Контролируемый [успешный результат](verification/run-preparation/prepare-source-retry.json) содержит:

| Identity | Наблюдение |
| --- | --- |
| Source commit | `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f` |
| SourceTree SHA256 | `4eac07820cd056f6f8c6a55de7652ba49e4f87db3ab925576357c3a3566c16a8` |
| Controller BuildClosure | `cf09513f25b4bc52dd56c9db0963cbe5094ab523a692f4d84d447f29c05a4f4e` |
| Published WorkerClosure | `b0156436e89404847c682f5dda4e3f733aab73d128a657d3975f86156641f0ef` |
| Immutable WorkerImage | `sha256:b2096d42c003488fa855acd748f851a6d700c7e6e02c54a834d8efcb760fb91d` |
| Preparation SourceKey | `60f3764fec65bc5d87ebe0d954ad8b807e9d91b3d79c019b320659e1af357aef` |

Остальные Packet/Prompt/Schema/Normalizer/Lexicon/Packages/WireSchema/ResourcePolicy fingerprints совпали с первой clean проверкой §11; [полный observed JSON](verification/run-preparation/preflight-worker-prepared.json) имеет Clean=true, ReadyForExecution=false, ProviderCalls=0. BuildClosure — identity controller closure, WorkerClosure — другой digest опубликованного worker и его companion files, ImageId — local immutable image identity; SourceKey связывает конкретную подготовку, не является разрешением. У image те же закреплённые base pins; provider/tokenizer acceptance, runtime kernel handshake и качество модели повтором не проверялись. Runtime shared cap не повышен, новые fake/kernel probes не запускались.

### Новый draft и закрытый технический blocker

[qualification-manifest.worker-prepared.draft.json](verification/run-preparation/qualification-manifest.worker-prepared.draft.json), SHA256 exact UTF8/LF bytes `72f1ce72117938710aa1bbb55b3433be64ac541a07a689b5a901a1b0a226d782`, отличается от первоначального draft только двумя значениями ExpectedArtifacts: WorkerClosure/WorkerImage. Исходный draft с SHA af2ad8a35d9c28179ebc1f0032a69c83a5e59906d39f87aafcc8901eb8df98a7 сохранён без изменения. ProtocolVersion остаётся1; имя файла не вводит новый protocol.

[Preflight нового exact draft](verification/run-preparation/preflight-worker-prepared-draft.json) на чистой проверочной ветке: exit0, Clean=true, ReadyForExecution=false, ProviderCalls=0. BoundWorkerPreparationRequired исчез; осталось ровно6 причин: SeparateAuthorizationRequired, ModelSelectionPendingOrChanged, PriceLimitsOrPolicyChanged, ReservationNotCovered, AuthorizationExpired, StableEvidenceRootRequired. Authorized=false, empty model/policy/authorization/run identity, pending zero rates, expiry1970 и null evidence root сохранены. Заданные USD0.50/0.01/0.10/48caps по-прежнему только предложение. Этот SHA идентифицирует denied review draft, **не разрешает его исполнение**.

Техническая source-only подготовка worker/image для accepted d318cb49 завершена. После решения пользователя пропустить платную оценку выбор processor/model/тарифа и model execution отложены. Текущий external API budget — USD0; это не следующий автоматически выполняемый шаг. Только новый явный запрос может вернуть обсуждение exact processor/model, terms/verified tariff и fictional calls/spend/time/evidence scope. После заполнения этих параметров нужен новый final manifest, exact SHA и отдельное execution approval. Если выбран другой execution commit или изменены binaries/worker/image, весь binding обновляется; будущий запуск из dirty документальной ветки запрещён. Runtime budget handshake остаётся проверкой перед key access в будущем исполнении, а не результатом сегодняшнего preflight. Main task6.3 по-прежнему unchecked/22of23; новых quality/cost/latency, private-text, holdout или rollout результатов нет.

## 13. Платная оценка отложена, external API budget USD0

Пользователь уточнил, будет ли это платным вызовом за пределами подписки, и отказался от такого варианта. Фактический runtime использует feature-specific direct Anthropic client с endpoint api.anthropic.com; это отдельный API product, не included usage этого ChatGPT/Codex чата. Публичная [официальная справка OpenAI](https://learn.chatgpt.com/docs/pricing) отделяет API token pricing от subscription usage; [Anthropic pricing](https://platform.claude.com/docs/en/about-claude/pricing) описывает тарификацию API. На2026-10-28 проверены только эти публичные страницы, без аккаунта, credentials/billing или model API. Никакие возможные promotional/account credits не считаются проверенными или разрешёнными.

Принят вариант F из исходного сравнения: оставить ручную форму и существующую детерминированную генерацию в прежних границах. Код/evaluator и source-only fingerprints сохраняются; оба draft JSON остаются Authorized=false с исходными SHA. Предложенные USD0.50/48calls внутри исторического draft **не текущий бюджет**: действующее решение пользователя — USD0 и no model execution. Платная qualification не запускается, новая платная модель не выбирается, бесплатные credits не ищутся, local model/weights/services и subscription automation не запускаются.

Никаких новых model calls, quality/cost/latency evidence нет. Task6.3 остаётся открытой/deferred; independent holdout, private-text/real-model acceptance, target и rollout также не приняты. M3 default-off; отсутствие оценки нельзя использовать как waiver для enablement. Manual M1, owner/revision/CAS/Save, catalogue1, обе immutable географии и historical plans остаются прежними. Возврат к модели возможен только после нового явного запроса и отдельного допустимого execution scope. Отсутствие commit/push/PR относилось к этапу фиксации этого решения. Позднее пользователь отдельно разрешил саморевью документов, commit/push/PR в dev и ожидание обычного CI. На этапе публикации PR84 это сохраняло USD0/no-model и требовало отдельного разрешения merge; последующее явное разрешение и результат merge записаны в §14.

## 14. Документальная фиксация PR84 и post-merge CI

После отдельного явного разрешения пользователя [PR84](https://github.com/svasorcery/travel-agency/pull/84) смержен в dev как `75ec04a01272d06f995f5cb925b548934b8f34ab`, reviewed head `dec35bb5e9225110269c8e7f48babd51982fbad5` на fresh base `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f`. [PR CI40746861915](https://github.com/svasorcery/travel-agency/actions/runs/40746861915) прошёл20 применимых jobs; [post-merge CI40755030053](https://github.com/svasorcery/travel-agency/actions/runs/40755030053) —19, оба completed/success/attempt1. Платная AI evaluation пропущена в обеих фазах, general PR-only E2E — дополнительно на push. Exact identities и [sanitized receipt](verification/run-preparation/pr84-ci-receipt.json) описаны в [publication](publication.md#pr84-documentary-acceptance-and-post-merge-ci).

CI standalone preflight на actual merge75ec04a показал Clean=true, ReadyForExecution=false и ProviderCalls=0. Это source inspection опубликованного документационного дерева. Исторические WorkerClosure/Image и оба immutable denied manifests продолжают относиться к d318cb49; ни CI, ни merge PR84 не перепривязывают их к75ec04a и не проверяют будущий runtime handshake или качество модели.

Решение §13 сохраняется: платная evaluation отложена, external model API budget **USD0**,6.3unchecked/deferred/not run, milestone22/23. Нет ни новых real-model outputs/cost/latency, ни нового holdout/private-text/target/rollout acceptance. M3 выключен. Первоначальная документальная актуализация подготовлена отдельно в codex/trips-m3-pr84-receipt от fresh75ec04a без model/evaluator/Docker execution, staging, commit и push. На2026-10-29 пользователь отдельно разрешил её саморевью, commit/push/PR в dev и ожидание обычного CI; merge, модель, включение M3 и расходы API остаются не разрешены. Итог CI этого документационного PR фиксируется в его описании, без дополнительного commit ради receipt.

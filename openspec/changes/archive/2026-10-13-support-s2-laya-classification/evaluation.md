# Runtime and S3 evaluation handoff

Это appendix для будущих отдельных разрешений, не выполненный experiment и не второй implementation plan. S2 source tasks находятся только в [tasks](tasks.md). Зафиксирован [serving/input/question/urgency profile](design.md); ordinary CI использует model-response fixtures, не скачивает модели.

## Runtime gate before enablement

1. Отдельно согласовать self-hosted private endpoint, immutable server/image/config/compute, секрет и privacy perimeter. Operator click не означает право отправлять реальные PII в неизвестный target.
2. Отдельно разрешить model/cache preparation и synthetic inference. Записать serverSHA/release/image digest, bundleSHA/model alias, device/precision/config и профиль вопроса; pin относится к правильному bundle.
3. Подтвердить preload/authenticated health/loadedrevision, отсутствие наружной отправки текста/body logging/tracing, штатный TLS, fixed model routing и input/output metadata на synthetic RU/EN примерах. Измерить memory/load/latency, timeout saturation и CPU fallback. Health200/sample answer не доказывают качество.
4. Включение on-demand suggestion для оператора — отдельный scope после этих receipts. Ручной workflow и rollback off проверяются. Новый profile/checkpoint/precision меняет identity и требует нового receipt.
5. Настоящий Support текст/сбор выборки требуют отдельно согласованного допуска. Никаких моделей, keys, DB/target grants или inference в текущем planning/source gate.

## S3 dataset and baseline

Перед measured run зафиксировать taxonomy/labels/input coverage policy и независимые RU/EN train-free development/calibration/holdout IDs. Предлагаемый минимум для review:80calibration и160holdout examples, оба языка и восемькатегорий, длинные/ambiguous/negation/prompt-instruction/без заказа/urgent cases; отметить малую статистическую мощность. Только synthetic/явно разрешённые redacted texts; сохранять примеры отдельно под утверждённой protection/retention policy. Без повторного выбора удобного holdout после ответов.

Два разметчика независимо определяют category и urgency from supplied authoritative fixtures; disagreement разрешается до inference. ManualRequired/Unclassified — самостоятельный исход, не «Other правильный». Production field claims денег не становятся labels подтверждённого financial outcome.

Simple baseline: фиксированные RU/EN keyword stems по8categories; case-insensitive Unicode-normalized comparison. Несколько category hits/нет hits/отрицание с ambiguous context→Unclassified; никакой модели. Versioned словарь/rules замораживаются до holdout. Baseline и Laya получают одинаковый **полный** allowed state; excluded overflow учитывается как manual handling, а не удаляется из denominator. Урgency-v1 проверяется одинаково отдельно от обеих category classifiers.

## Metrics and numerical acceptance proposal

Считать all-requests coverage/manual fraction, usable-candidate accuracy и macroF1, per-language/per-category confusion matrix, operator override rate, missed verified imminent cases, p50/p95 latency, timeout/truncation/invalid identity, CPU/memory и operator time при отдельно разрешённом realistic flow. Unclassified/abstentions не исключаются из coverage. Raw numerical confidence не интерпретируется как accuracy; token/time counters не считаются currency cost.

Предлагаемый on-demand experiment acceptance для отдельного review: eligible category macroF1≥0.75по каждому языку и улучшение≥0.05vs baseline; coverage≥50%на полном declared input policy потоке; urgency missed verified urgent=0на synthetic boundary suite; warmp95≤3sec и≤10sec request limit, без logging/privacy нарушений. Утверждение «оператор быстрее» требует своего парного измерения, не следует из этих targets. Эти числа **не приняты и не измерены**; final thresholds/targets утверждаются до runs, результаты нельзя переписать задним числом.

Thresholds выбираются только по calibration, включая entropy и max-probability отдельно, затем freeze profile и once-held-out result с confidence intervals/примерным размером групп. Отдельно сообщить случаи outside4messages/512tokens, language drift и false positives. До успешного S3 review automatic routing не включается; для automatic promotion понадобится отдельная policy/tighter risk gate/rollback, которую S2 не предоставляет.
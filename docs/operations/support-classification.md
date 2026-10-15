# Support classification: source configuration and runtime handoff

## Experimental operator mode — 2026-10-15

Пользователь согласовал сохранить Laya и восемь категорий как **экспериментальную подсказку**: запуск по кнопке оператора, отдельное ручное сохранение, лимит запроса 10 секунд. Контракт и scope: [завершённая доработка](../../openspec/changes/archive/2026-10-15-support-classification-experimental-mode/design.md), [текущие требования](../../openspec/specs/support-classification-suggestions/spec.md).

Перед запросом и при просмотре результата UI явно предупреждает: «Экспериментальная функция»; «AI-подсказка может ошибаться. Проверьте категорию перед применением». Подстановка меняет только черновик категории. Приоритет выбирается вручную; сохранение выполняется отдельной кнопкой. Запрос и подстановка не меняют сохранённую категорию, приоритет, версию или источник решения. Автоматического назначения сотрудников, ответов клиенту или действий с заказом нет.

Сохраняются production-вопрос support-category-v1, восемь категорий Payment/Ticketing/Cancellation/Refund/FareConditions/Ancillaries/Technical/Other и guards 0.75/0.60. Unclassified — ручной fallback, не девятая категория модели. A/B-вопросы и дополнительный фильтр ясности не переносятся в продукт. Маловероятный, неполный, просроченный, недостоверный или потерянный ответ оставляет ручную обработку без автоматического повтора.

**Прежняя проверка качества не пройдена.** Для исходного v1 на синтетической AI-разметке было 35 верных рекомендаций из 46 (76.09%) при охвате 41.07%; A/B также не выполнил согласованные критерии. Это не экспертная оценка и не доказательство качества реальных обращений или экономии времени. Новое решение разрешает ограниченный экспериментальный режим, не объявляет старый результат успешным; holdout остаётся закрытым.

Доработка принята в [PR51](https://github.com/svasorcery/travel-agency/pull/51): обычный PR CI с E2E и проверки после слияния прошли. Первоначальная проверка приложения использовала вымышленные данные и ответы-фикстуры; [история проверок](../../openspec/changes/archive/2026-10-15-support-classification-experimental-mode/verification.md) сохраняет её границы.

После отдельного согласования выполнен [изолированный прогон с настоящей Laya](../../openspec/changes/archive/2026-10-15-support-classification-experimental-mode/runtime-pilot.md), настоящими Support API и PostgreSQL, тестовым входом и тремя новыми вымышленными обращениями. Русский пример дал подсказку Refund за 2.786 секунды, английский — LowConfidence/manual за 0.672 секунды; пять сообщений привели к ручному выбору без вызова модели. Выполнены два модельных запроса, оба в пределах 10 секунд. Запрос и подстановка не сохраняли категорию; отдельное подтверждение сохраняло решение оператора. Стенд остановлен. Это проверка подключения, не новая оценка точности, полный Host/Keycloak или разрешение реальных обращений. Default-off и отдельные target/private-text approvals сохраняются.

S2 добавляет рекомендацию по кнопке оператора. Она не сохраняет категорию/приоритет и не выполняет операции заказа. Сохранение остаётся обычным ручным SetTriage. Контракт и source receipts: [canonical S2 change](../../openspec/changes/archive/2026-10-13-support-s2-laya-classification/verification.md).

## Default and prerequisites

`Support:Classification:Enabled` по умолчанию false. Missing/invalid profile also gives Disabled before private-text decryption, order lookup or model HTTP. S1 remains usable. Source validation and demo responses do not enable the model.

Для отдельного runtime approval нужны self-hosted HTTPS origin внутри доверенного Support perimeter, Bearer secret вне Git, immutable image/config receipt, подтверждённый checkpoint/device/precision и запрещённые body/prompt/response logs/hooks/exporters. Arbitrary Support text может содержать PII; structured-field exclusion не анонимизирует его. Реальные тексты/compute/download/network/keys/grants не разрешены source implementation.

## Configuration

Section `Support:Classification` содержит:
- Enabled (false default), BaseUrl (approved HTTPS origin only), ApiKey (runtime secret).
- ServerSourceSha: c73908d96ff73c0062c340a11bf7c54ec7d8bc4d, server release0.4.6.
- ImageDigest: утверждённый sha256 digest; пустое значение выключает feature.
- CheckpointRevision: bundle55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851, forced multilingual subfolder.
- ExpectedDevice/ExpectedPrecision: explicit approved compute/config; precision не является health field.
- MinAnswerConfidence0.75 и MinEntropyConfidence0.60 — source profile guards, **не calibration/accuracy**.
- AllowCpuFallback=false; изменение требует нового profile receipt.

Нет default URL/key/image, startup inference, model auto-routing или автоматической загрузки weights. Standalone multilingual repository SHA не подставляется в bundle. `LAYA_JEV_STRICT=0`, explicit pinned revision/preload/offline serving относятся к отдельно утверждаемому Laya deployment, не к запускаемому здесь сервису.

## Bounded behavior and privacy

POST `/api/support/operator/cases/{caseId}/classification-suggestion` принимает только UUIDv4 requestId и expectedRevision. Body≤1KiB, existing operator scope/client policy, no-store. Модель получает полную1–4message conversation+title без identity/order/passenger/token fields; scalar/byte/token overflow означает manual handling. Scope guards, text protection bindings и case revision проверяются до обработки и после результата.

Admission:2active/3attempts per operator-minute/12per process-minute, queue0,10second total request deadline,2minute suggestion TTL. Model HTTP имеет one inference, no retry/redirect/cookie/body logging. Timeout не доказывает остановку remote compute; новый явный запрос расходует budget. Unknown S1 write не становится повторяемым из-за чтения или recommendation.

Before/after authenticated health проверяет resident revision/device и CPU fallback counter. Identity drift выключает immutable singleton и прекращает дальнейший dispatch/Ready для concurrent attempts; начатый compute остаётся uncertain. Новый receipt/config restart нужен для нового serving profile.

Urgency использует minimal owner-bound Flights source facts: lifecycle uncertainty/expired hold/missing schedule = Unknown. Active future planned segment within24h proposes High; existing High не понижается. Это scheduled source context as-of, не live-flight/payment/refund/bank proof.

## Observability and rollback

Meter `Travel.Support.Classification` records fixed status/reason/category/profile and duration, input byte/token measurements. Text, case/owner/request IDs, tokens and raw provider responses do not become labels/logs. Token counts/duration не являются денежной стоимостью или доказательством качества.

Rollback: set Enabled=false and restart Travel.Host through the separately authorized deployment procedure; this provider holds an immutable IOptions snapshot, so an edited configuration file alone does not replace the active profile. Saved manual category/priority/receipts remain; reset to Unclassified/Normal не выполняется. Clearing model/cache/deployment has a separate scope. [Runtime/S3 evaluation handoff](../../openspec/changes/archive/2026-10-13-support-s2-laya-classification/evaluation.md) separates synthetic smoke, RU/EN calibration/holdout and later automatic routing from source acceptance.

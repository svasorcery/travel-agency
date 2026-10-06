# Контракты и границы исследования

Дата исследования: 2026-10-04. Supplier contracts изучены только по публичным официальным документам/исходникам, без supplier API. Записи о неустановленном CLI ниже относятся к исходному исследованию; позднее T1 установил pin1.14.0, а T2 получил локальную CLI validation. Execution evidence находится в harness-integration.md/process-log.md; full authenticated Travel/OpenSpec verifier PASS получен 2026-10-05 в стандартном never/read-only; прежние timeout и неудачные hypotheses сохранены как история процесса. Contract evidence, design inference и runtime proof разделены.

## Duffel: проверенные документальные факты

[Order Cancellations v2](https://duffel.com/docs/api/v2/order-cancellations): create pending (`POST /air/order_cancellations`, order_id), read by ID, paginated list filtered by order_id, separate confirm (`POST /air/order_cancellations/{id}/actions/confirm`). Согласовать можно только последнее предложение. Схема: id/order_id/live_mode/created_at, nullable expires_at/confirmed_at/refund_amount/refund_currency, refund_to. Null amount означает неизвестную сумму; zero допустим, awaiting-payment hold имеет zero. Сумма может включать услуги и mixed cash/credits; подтверждение связано с возвратом поставщика, отдельная выплата клиенту остаётся обязанностью агентства.

[Cancellation guide](https://duffel.com/docs/guides/cancelling-an-order): available_actions должен содержать cancel; неподдерживаемая API отмена требует ручного пути. Старое предложение возвращает order_cancellation_stale; после подтверждения cancellation содержит confirmed_at и доступна через order.cancellation. Airline credits/voucher нельзя описывать денежной выплатой.

[Orders schema](https://duffel.com/docs/api/orders): available_actions/cancellation/cancelled_at — разные факты. Relevant schema прочитана через официальный indexed excerpt; целая страница недоступна web reader из-за >4MiB. [Get order v2](https://duffel.com/docs/api/v2/orders) подтверждает read by ID. Guide дополнительно подтверждает order.cancellation. Runtime соответствие, задержка видимости и exact null-expiry смысл не проверены.

[Response handling](https://duffel.com/docs/api/overview/response-handling): cancellation-create раздел перечисляет already_cancelled/order_not_cancellable и modified_externally/invalid_order. Классификация 503/202 для **создания bookings** не переносится на cancellation confirm. Общий HTTP статус не доказывает отсутствие внешнего эффекта.

## Выводы для дизайна — это наши решения, не гарантии Duffel

- Не обнаружена гарантия idempotency или безопасного повторного confirm. Пока неизвестность не разрешена, никаких автоматических mutation retries. Отсутствие найденной гарантии не утверждает, что supplier её никогда не поддерживает.
- Известный cancellation ID позволяет читать именно согласованную операцию. Совпадающий order.cancellation может помочь; cancelled_at без correlation доказывает только отмену заказа, не исполнение нашего согласия и не финансовый receipt.
- Потерянный create response не равен confirm uncertainty: без принятого consent Travel не подтверждает. List/read могут найти кандидатов; timestamps/сумма/единственный похожий объект не доказывают, что это наш create, особенно при dashboard/external activity. Автовыбор по newest запрещён. Явный fresh preparation разрешается только когда предыдущая стадия доказанно не могла dispatch confirm; неоднозначность остаётся manual.
- GET confirmed_at=null не доказывает, что старый confirm не выполняется. Timeout, reload, restart, 404, exhausted reads, stale terms после прежней неизвестности не становятся отказом. Даже late 4xx не стирает предыдущую uncertainty.
- Null expiry трактуется консервативно: нет автоматического согласования, пока его смысл не подтверждён официально; UI показывает unavailable/manual. Сумма/валюта также не синтезируются из booking total, FX или нуля.
- Для первого среза предлагается только известная денежная сумма ≥0, известная валюта и поддержанный refund destination; awaiting_payment — zero и подтверждённое unpaid состояние. Credits/vouchers/mixed settlement/unknown amount — explicit manual/unsupported до confirm. Эти ограничения нуждаются в бизнес-утверждении D2.

## Неустановленная supplier семантика

| Вопрос | Текущая граница | Что разрешает следующий gate |
| --- | --- | --- |
| Повтор confirm одного ID после timeout | Не считаем безопасным | Новая официальная гарантия или отдельно разрешённая sandbox-проверка; не входит в текущую работу |
| Видимость reads после dispatch | Нет гарантированного SLA | Bounded reads → unknown/manual, не false rejection |
| Correlation потерянного create | order filter документирован, operation key не найден | Не выдавать найденный объект за свой без доказательства |
| Null expires_at | Nullable документирован, точный operational смысл не установлен | Fail closed до отдельного решения |
| Terminal cancellation и реальное зачисление | Есть supplier cancellation terms, отдельный банковский ledger не проверен | Показывать источник/amount/destination, не утверждать клиентскую выплату |
| API cancellation callback для user operation | Current handler обрабатывает другие facts | Не полагаться на несуществующий/непроверенный webhook |

Локальные управляемые fakes/WireMock докажут поведение Travel при этих условиях, а не фактическое поведение Duffel. Реальных suppliers/платежей и sandbox заказов сейчас нет. Production cancellation остаётся unsupported.

## OpenSpec: версия подтверждена отдельно от исторического brief

[GitHub latest release](https://github.com/Fission-AI/OpenSpec/releases/latest) сейчас ведёт на v1.14.0; публичный npm latest прочитан независимо и возвращает ту же версию. На исходном этапе это была **предложенная версия для согласования**. После approval T1 установил именно этот pin, без автоматического перехода на latest. Tag: `94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5` (`git ls-remote`).

Registry metadata: `@fission-ai/openspec`, Node >=20.19.0; local Node v22.18.0. Tarball `https://registry.npmjs.org/@fission-ai/openspec/-/openspec-1.14.0.tgz`; integrity `sha512-V+zitRK918I6B3EIcYL3gVYoPoPgzknSxE1zL0zUQdbd4NsO5kbrxU5jVceuNNEznD94pbRPCvpCDIa6BeEaPA==`. На исходном этапе integrity только прочитан. При T1 выполнен tool-local ignore-scripts install/ci; launcher проверяет version/lock/SRI и полный runtime tree digest. Source/CLI proof не является supplier или native-agent acceptance.

[Supported tools at v1.14.0](https://github.com/Fission-AI/OpenSpec/blob/v1.14.0/docs/supported-tools.md): Codex использует `.agents/skills`; core профиль содержит propose/explore/apply/update/sync/archive. Дерево содержит marker `.openspec-target`. Нельзя использовать glob openspec-* как исключение inventory.

[CLI reference](https://github.com/Fission-AI/OpenSpec/blob/v1.14.0/docs/cli.md): explicit tools/profile, JSON status/validate, telemetry opt-out. [Spec-driven schema](https://github.com/Fission-AI/OpenSpec/blob/v1.14.0/schemas/spec-driven/schema.yaml) задаёт proposal/specs/design/tasks. Эти документы созданы вручную; validation CLI ожидает approval и установки.

Публичные исходники закреплённого commit прочитаны без запуска: [global-config](https://github.com/Fission-AI/OpenSpec/blob/94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5/src/core/global-config.ts) поддерживает XDG_CONFIG_HOME на Windows; [shared target](https://github.com/Fission-AI/OpenSpec/blob/94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5/src/core/shared-skill-target.ts) пишет toolId + newline; [init](https://github.com/Fission-AI/OpenSpec/blob/94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5/src/core/init.ts) берёт delivery из global config. Поэтому воспроизводимость требует изолированного task config, даже при explicit core profile.

Нет предположения превосходства OpenSpec. Первичная ручная подготовка не измеряла генерацию/валидацию CLI. Последующий T1/T2 теперь отдельно фиксирует installation/generation/validation, адаптацию Travel harness и незавершённую native-agent проверку; журнал сохраняет границы evidence.

## Review уточнение: состав возврата

[Using airline credits](https://duffel.com/docs/guides/using-airline-credits) описывает cash/credit payment composition, но раздел новых APIs явно содержит not-yet-available caveat. Listing payments по order не принимается здесь как доказанно доступный working endpoint. Monetary-only mapping требует positive facts, включая explicit credit-list shape и отдельный original CashOnly proof для original_form_of_payment. При недостатке evidence — Unknown/manual, а не предположение по amount/destination.

## T3 currency recognition source

[SIX ISO4217 maintenance agency](https://www.six-group.com/en/products-services/financial-information/market-reference-data/data-standards.html) links the free current currency/funds list. [XML List One](https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml), published2026-09-17, was read2026-10-05 only as public reference data (not a supplier/payment API). The fresh cancellation code snapshot contains176 unique active codes after excluding testing/no-currency XTS/XXX; XML UTF8 SHA25633139b438657d1cee116ba737807ea71d19d6de4b90f799a09c56f0cc6a1b0ff. No runtime fetch or general Money/history change. New/unknown/deprecated currency takes manual handling; it is not converted or presented as zero.

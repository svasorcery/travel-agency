# Laya: восстановленный контекст для S2

Дата2026-10-12. Read-only источники и будущий integration decision; модели не устанавливались/не запускались. S1 работает без классификатора. Это research appendix канонического change, не отдельный feature plan.

## Что выбрали раньше

В чате «Оценить кейсы для Laya» (`01a0fc3a-6cd4-74c2-a28f-b73170e1eb4e`,2026-10-02) рассмотрели model routing и сортировку клиентских обращений. Пользователь выбрал Support, признал routing недостаточно обоснованным и согласовал место после Flights M3, перед Hotels. Изменение roadmap сохранено commit `624e0bdca444730452021c097b0fb4cde1e93ace`; это решение об очередности, не готовая integration spec или оценка качества.

## Подтверждённый инструмент и реальные интерфейсы

[Репозиторий автора](https://github.com/NandhaKishorM/laya) — `NandhaKishorM/laya`; проверенный main SHA `c73908d96ff73c0062c340a11bf7c54ec7d8bc4d`, release0.4.6. [Документация](https://nandhakishorm.github.io/laya/), [multilingual checkpoint](https://huggingface.co/convaiinnovations/laya-multilingual). Это typed decision engine для choice/score/yes-no, с открытым кодом/весами и Apache2.0; работающий сервер всё равно требует compute/операционного сопровождения.

[Официальный HTTP API](https://nandhakishorm.github.io/laya/http-api/) подтверждает `POST /v1/systemone`: `state`, именованные `questions` и фиксированные choice criteria; optional model/language/budget. Ответ содержит typed answers, probabilities и metadata. `GET /health` показывает liveness; один200не доказывает загруженный нужный checkpoint или его пригодность.

Для S2 понадобится Infrastructure ACL над действительным HTTP protocol; wire DTO не пересекают boundary. Версия server/checkpoint/question schema фиксируется перед экспериментом. Timeout/503/unknown enum/неполный ответ оставляют manual queue; HTTP retry не считается бесплатным или автоматическим правом повторить inference. Выбор размещения и model download согласуются в S2, не выполняются из этого research.

`confidence`, `answer_confidence` и abstention не взаимозаменяемы. [HTTP документация](https://nandhakishorm.github.io/laya/http-api/) отдельно описывает truncation/unknown confidence/abstention. При потерянных обязательных input facts, неподтверждённом checkpoint или отсутствии пригодных confidence facts автоматизация не допускается; нельзя считать high confidence доказательством правильности.

## Что ещё не доказано

[Staged adoption](https://nandhakishorm.github.io/laya/staged-adoption/) рекомендует shadow→comparison→held-out policy→bounded promotion. Это соответствует roadmap: operator решение остаётся authoritative, threshold подбирается на отдельной calibration выборке, RU/EN holdout не подбирается после output. Опубликованный benchmark не является Travel evidence и не задаёт SLA на нашей машине.

Будущий S2 сравнивает Laya с простой baseline классификацией на одинаковых размеченных обращениях, считает ошибки/ручную обработку/latency/compute cost и сохраняет версии. S3 допускает automatic routing только проверенных категорий с rollback/manual fallback. Urgency определяется явными правилами по достоверным booking facts и будущим departure times; model text не устанавливает состояние денег. Laya не отменяет, не платит и не отвечает клиенту автоматически.

Качество на реальном Support-потоке, экономия операторского времени, размещение и целевые пороги пока не приняты. История исследования не подменяет отдельное execution/model/provisioning разрешение.

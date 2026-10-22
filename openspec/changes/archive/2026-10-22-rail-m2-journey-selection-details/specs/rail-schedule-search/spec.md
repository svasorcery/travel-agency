# Rail schedule search — M2 delta

## ADDED Requirements

### Requirement: Local journey ordering
Система SHALL предоставлять сортировку полученной выдачи по отправлению, прибытию и длительности по возрастанию, без новых HTTP-запросов и изменения исходных source facts. По умолчанию SHALL использоваться отправление. Сортировка SHALL применяться только к текущему действительному snapshot, сохраняя признаки неполноты.

#### Scenario: Absolute times and stable ties
- **WHEN** варианты имеют разные даты/offsets либо равные значения сортировки
- **THEN** отправление/прибытие сравниваются по instants, длительность — по durationSeconds, равенство разрешается исходным индексом; исходный массив и точные строки цен не меняются.

#### Scenario: Limited results are not a global ranking
- **WHEN** пользователь сортирует limited выдачу
- **THEN** остаются исходный limited notice и причины, виден текст «Среди полученных вариантов»; самый короткий в списке не объявляется лучшим/самым быстрым поездом на всём направлении.

### Requirement: Local time and carrier filters
Система SHALL фильтровать текущие строки по местному времени отправления, местному времени прибытия и сообщённому названию перевозчика без supplier calls. Фильтры времени SHALL предлагать «Любое», 00:00–06:00, 06:00–12:00, 12:00–18:00, 18:00–24:00 с включённой нижней и исключённой верхней границей; по умолчанию все фильтры выключены.

#### Scenario: Station local clock rather than browser time
- **WHEN** фильтр применяется к ночному рейсу с переходом даты/offset
- **THEN** сравнивается время суток из собственного offset timestamp соответствующего конца; browser/server timezone не используется, дата прибытия остаётся видимой.

#### Scenario: Carrier selection without identity invention
- **WHEN** в snapshot есть несколько названий перевозчика, null либо пустая/whitespace подпись
- **THEN** пользователь выбирает «Все», одно точное название или отдельное «Не указан»; одинаковые строки группируются только как подписи, разные названия не объединяются как одна компания.

#### Scenario: Carrier options remain tied to the whole snapshot
- **WHEN** другие фильтры скрыли все строки конкретного перевозчика
- **THEN** option этого перевозчика остаётся доступной до смены snapshot; options строятся из всей полученной выдачи. Unknown включает только null/blank, а literal source label «Не указан» остаётся отдельным exact значением; UI различает их.

#### Scenario: Reset and boundary controls
- **WHEN** пользователь сбрасывает фильтры либо время равно06:00,12:00,18:00 или00:00
- **THEN** сброс сохраняет sort и возвращает все полученные строки без HTTP; граничная минута относится только к следующему bucket. Новый принятый поиск сбрасывает и sort, и filters; исходные failed/skipped/limited-zero views не получают обещания наличия поездов.

#### Scenario: Combined filters and empty visible list
- **WHEN** одновременно выбраны фильтры и ни одна из полученных строк им не соответствует
- **THEN** условия объединяются AND, UI показывает «Среди полученных вариантов нет подходящих» и сброс фильтров, visibleCount/receivedCount; исходный complete/limited/failed/skipped outcome не переименовывается в отсутствие поездов.

#### Scenario: Change view without extending data lifetime
- **WHEN** меняется сортировка/фильтр или истекает исходный snapshot
- **THEN** изменение представления не вызывает поиск и не продлевает expiresAt; expiry очищает source rows, выбор и подробности. Новый успешный поиск сбрасывает view controls к значениям по умолчанию.

## MODIFIED Requirements

### Requirement: Bounded execution and cancellation
Система SHALL ограничивать поиск и подробности суммарно2 admitted операциями/process без очереди; поиск SHALL сохранять максимум2 children/search,5s/child и8s/search. Отмена caller SHALL прекращать ожидание и запретить позднюю публикацию/cache write; незавершённая работа SHALL удерживать permit своей операции до фактического завершения.

#### Scenario: Capacity exhausted
- **WHEN** две admitted операции поиска/подробностей ещё владеют работой
- **THEN** следующий поиск или запрос подробностей получает429 без supplier calls и без постановки в очередь.

#### Scenario: Cancellation with uncooperative child
- **WHEN** caller отменяет поиск, а child игнорирует отмену
- **THEN** caller освобождается своевременно, child не публикует результат и продолжает занимать ограниченный permit; detached work не растёт с новыми callers.

#### Scenario: Deadline and retry limit
- **WHEN** child не завершился за5s или supplier ответил ошибкой
- **THEN** соответствующий mode timeout/failed без retry; aggregate завершается в пределах8s, сохраняя уже пригодные mode results.

#### Scenario: Mixed search and detail workload
- **WHEN** одновременно выполняются один all-mode search и одни подробности
- **THEN** заняты два общих permits; подробности не получают второй внутренний permit для своей перепроверки и не обходят общий quota ledger. Третья операция отклоняется, без deadlock и независимого дополнительного пула.

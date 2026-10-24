# Независимый аудит Trips planning

Дата: 2026-10-20. Область — [proposal](proposal.md), [design](design.md), [delta spec](specs/trip-plan-workspace/spec.md). Пользователь согласовал сначала документ, затем независимый аудит и исправления, затем детальные задачи. Код/ветка/Git/runtime в scope не входят.

Актуальный результат: ниже сохранён также свежий независимый аудит всего пакета, включая tasks, и повторная проверка его доработок. Предыдущие hashes/verdicts оставлены как история, а не выдаются за проверку последующих версий.

## Порядок и независимость

Автор подготовил три документа; до первого аудита tasks.md отсутствовал. Отдельный агент `/root/trips_design_audit` получил чистый контекст (`fork_turns=none`), исходный пользовательский сценарий и пути документов. Он прочитал документы целиком и проверил фактические Trips/Host/Support/Identity patterns и ADR. Агент не писал файлы; этот отчёт записан автором по его сообщениям. Это независимый от автора design review, не человеческая приёмка и не runtime validation.

Исходные SHA256: proposal `D98CB834742ABEEBBC3858AB520CD0FA1D5A840D8FBB969D9A8B5400BBD31D92`; design `E859A1EC9504F1B3DB9A2964A01458CC1CB5034EC6F9788A6F0D99F9E84CC9D2`; spec `11ED54A4AE11EC9BEE1DA82289D60FDDC15023C2C8EF411F5E254A4FE175AC70`.

## Исходные замечания

| ID | Приоритет | Сценарий | Исправление |
|---|---|---|---|
| R1 | P2 | PUT отправляет A; пользователь правит B; ответ A затирает B/помечает clean. Старый GET revision1 после save revision2 регрессирует state | Captured editor/request epochs и submitted edit version, monotonic revision, сохранение B dirty; DELETE меняет epoch. Добавлены edit-during-save и late-after-save/delete scenarios |
| R2 | P2 | Допустимый private payload после envelope/warnings/escaping превышает128KiB detail; сохранённый план нельзя прочитать | Canonical full future detail sizing до commit, включая reserve/derived/server fields; atomic rejection без revision change; summary-only bounded page; Unicode roundtrip scenario |

P1 не обнаружено. Server-owned body fields не признаны отдельным дефектом: whitelist уже следовал из design; автор дополнительно сделал его нормативным в spec. В личной сверке автор также уточнил owner-claim rejection и immutable destination metadata snapshot; эти уточнения вошли в повторно проверенную версию.

## Повторная проверка исправлений

Аудитор перечитал изменённые design/spec и сообщил: «Обе P2 закрыты. Новых существенных дефектов не обнаружено; дизайн пригоден для составления tasks». Остаточных P1/P2 и блокирующих продуктовых вопросов нет. M1 остаётся промежуточной основой; целевой M2 и его data/quality gates сохранены. Rail не является prerequisite M1.

Повторно проверенные SHA256: design `AA828F5D9BA5665B2CE6E86BE849A411C9DD6EFC87D023AAA0B613EE1563E83D`; spec `C263F929BD2DC2EBCF59610C0BFC9721ED6FD9661EE0736658165CAF329DB2B5`. Proposal не менялся. Только после этого результата создан [tasks.md](tasks.md).

## Личная сверка и остаточные границы

Автор сопоставил замечания с изменёнными нормативными сценариями, сохранил unknown/partial/uncertain states и отделение M1 от генерации. Численные пределы,3города,API и storage choices остаются предложением для утверждения итогового M1 scope. Для отдельного M2 пока не выбираются источники POI/travel facts, модель или paid budget: это новая продуктовая область, а не пропущенный M1 blocker.

CLI validation проверяет структуру, но не бизнес-корректность. Independent review не доказывает качество будущих маршрутов, реальный supplier/model access, concurrency performance или target readiness. Ни Host/DB/schema/keys, ни model/provider requests, ни code tests не запускались в этой planning работе.

## Проверка пакета после первого аудита

После создания tasks тот же независимый аудитор отдельно проверил порядок зависимостей, проверки в каждой группе, scope/authority и traceability. Вердикт: «Consistency подтверждена; существенных замечаний нет». Проверено21задача, все unchecked;15requirements/49scenarios; покрыты все15строк traceability. Итог готов к утверждению M1 scope, а не к объявлению реализации готовой.

Выполнены `node tools/openspec/run.mjs validate trips-m1-structured-planning --strict --json` (exit0,valid=true,issues=[]) и `node tools/openspec/run.mjs status --change trips-m1-structured-planning --json` (все4planning artifacts присутствуют,isPlanningComplete=true). CLI status описывает наличие документов;21implementation checkbox остаются невыполненными.

Дополнительная read-only Node-проверка5Markdown artifacts:18относительных ссылок,0broken;0duplicate requirement/scenario/task IDs;0описаний requirements длиннее500символов;WHEN/THEN присутствуют;15/15requirements имеют traceability и все task refs существуют. SHA256 proposal/design/spec совпадают с записанными review versions. Автор менял только файлы этого нового change; текущая Rail ветка не переключалась, stage/commit/push не выполнялись.

## Свежий независимый аудит полного плана

По прямому поручению пользователя запущен новый агент `/root/trips_full_plan_independent_audit` с чистым контекстом (`fork_turns=none`). Он не участвовал в написании или первом аудите. Получил proposal/design/spec/tasks и исходную согласованную последовательность, но не предыдущий verdict; `review.md` не читал. Проверены целиком четыре planning-файла, фактические Trips/Identity/privacy/Angular patterns и релевантные ADR. Checkout head на старте проверки: `799e31a6b31ff256ba017861cd911e547e4e9896`; план ещё untracked и идентифицирован SHA256, не новым Git commit.

Исходный verdict нового аудитора: подтверждённых P1/P2, обязательных доработок и блокирующих вопросов не найдено. Проверка охватила разумность объёма M1, сохранение целевого M2, HTTP/EF/CAS, owner isolation, protected storage, editor races/uncertain writes, bounded payload, task dependencies/traceability и изоляцию Rail. Это отдельная полная проверка уже существующего пакета, а не личная сверка автора или повтор первого walkthrough.

Единственная рекомендация была необязательной: добавить компактные JSON-примеры POST/PUT/detail, закрепив вложенность дней/блоков, display order и создание новых block IDs до параллельной реализации API и клиента. Автор проверил рекомендацию и уточнил design §4.1 и tasks4.1/5.1: явные write whitelists, client UUID/stable identity, array order, nullable fields, exact monetary types и read-only metadata. Product scope, proposal и нормативный spec не менялись. Примеры — вымышленные contract vectors, не supplier data или реальные записи.

### Проверка доработок свежим аудитором

Тот же новый аудитор повторно прочитал изменённые документы. Итог: «P1/P2, новых дефектов и блокирующих вопросов нет. Optional refinement закрыт». Подтверждены согласованность201/200/204,Location/ETag/CAS/no-op/delete, три валидных JSON-примера, одинаковые submitted/accepted days, отсутствие scope expansion. Файлы аудитор не менял; финальный текст отчёта записал автор по полученным сообщениям.

| Artifact | SHA256 до свежего аудита | SHA256 после доработок и повторной проверки |
|---|---|---|
| proposal.md | `D98CB834742ABEEBBC3858AB520CD0FA1D5A840D8FBB969D9A8B5400BBD31D92` | без изменений |
| design.md | `AA828F5D9BA5665B2CE6E86BE849A411C9DD6EFC87D023AAA0B613EE1563E83D` | `D2311040632CC1905D73FA54D9442D465BCD9740F249046392EB199B47D389BD` |
| tasks.md | `9070A67FAF23F13081397C9FEB9AC86B4FCD2B2DB8F63F280808C22F6DF3DCC5` | `980BEB9E2921AC8A0CFF0EF41136F0EB6B2255DA6461C810039FA715197AC6A2` |
| specs/trip-plan-workspace/spec.md | `C263F929BD2DC2EBCF59610C0BFC9721ED6FD9661EE0736658165CAF329DB2B5` | без изменений |

Структурная проверка после доработок: OpenSpec strict valid=true/issues=[];15requirements/49scenarios;21unchecked task;15traceability rows;3валидных JSON-примера;18relative links/0broken. Все ошибки дополнительной read-only проверки отсутствуют. Изменены только существующие design.md/tasks.md/review.md этого change. Код, ветки, schema, модели, suppliers, публикация и rollout не изменялись. Независимый аудит и доработки завершены; дальнейшая реализация остаётся отдельным действием.

# Независимая проверка плана

Агент: `/root/fare_plan_review`, отдельный read-only context без истории root. Дата: 2026-10-12. Worktree/source base: `163e3d9b467259518a11e6c6c423aa0e3f79a513`.

Прочитаны весь planning corpus и применимые AGENTS; source сверены quote/re-quote, QuoteBindingFactory, ownership/creation reader, mapper/decoder, cache/dedup, AI ledger и локальная SDK XML Anthropic12.20.0. Supplier scopes сверены с официальным Duffel guide. Файлы, runtime, tests и API агент не изменял/не запускал.

## Раунд1: needs revision

Одно важное замечание P2: tolerant reader D2/task3.1 был ограничен allowed/penalty. Existing DuffelBaggageDto.Quantity=int теряет отсутствующее значение как0, а malformed quantity/optional labels могут сломать offer до mapper. Это противоречит Unknown/Unavailable поведению нового объяснения.

Требуемое исправление: покрыть informational baggage type/quantity и optional labels; проверять presence explicit0; сохранить strict required price/party/itinerary и приобретаемые services. Raw fixtures во всех search/refresh/ancillary catalog путях: omitted/null/wrong-type/overflow quantity, malformed labels, unknown без выдуманного0 и без повторного HTTP.

## Disposition

Исправлено в design D2 и task3.1: bounded presence-aware reader всей informational fare-ветки и единый путь для search/refresh/catalog. Добавлен нормативный scenario про malformed quantity/labels; audit дополнен конкретной source ссылкой. Остальная architecture/financial authority не расширена.

Агент не обнаружил других важных замечаний: финансовые границы, historical fallback, PII, default-off AI, сравнение с шаблонами и отдельные acceptance gates покрыты.

## Раунд2: Ready

Агент повторно прочитал исправленный corpus: **P2закрыт, план готов**. D2/task3.1/new scenario согласованы, zero отличается от unknown, все три DTO-пути названы, strict mandatory/service guards сохранены. Существенного остаточного gap не обнаружено. Неблокирующая поправка receipt35→36scenarios исправлена в verification.md. Это независимое принятие плана, не реализации или runtime; tests/API не запускались.

## Повторное саморевью по запросу пользователя

Автор: root, 2026-10-12. Независимый агент в этом раунде не запускался. Предыдущий Ready относится к версии до уточнений ниже; не объявляется независимым review новой редакции. Проверены actual files, source quote/events, Angular consent/client timeout, AI composition и existing ancillary requirements. Это корректировка планирования, не исходного кода.

| ID / важность | Пробел исходного плана и последствие | Исправление и будущая проверка |
|---|---|---|
| S1 / P2 | Snapshot при re-quote не был явно сохранён как отдельная frozen evidence; replay мог бы пересчитать его новым алгоритмом | D3 и task2.2: immutable snapshot в обоих quote events до commit, сохранённые schema/fingerprint/observedAt, copy-only Apply; сценарий Reader or normalization version changes |
| S2 / P2 | Current/Unavailable не покрывали истечение или смену source revision во время post-commit inference | D4/D6, tasks4.2/6.1: Current/Stale/Unavailable, serverObservedAt, bounded final source/time checks и deadline≤expiry/departure; два новых race scenarios. Никакого quote/financial retry |
| S3 / P2 | Whole-offer fare label мог выглядеть как правило возврата выбранных услуг | Отдельное normative requirement и два service scenarios, D4 mandatory NotProvided notice, task4.1. Цена/условия тарифа не доказывают seat/bag/grand-total refund; cancellation contract сохраняется |
| S4 / P2 | Не определён знаменатель AI-quality gate и набор mixed holdout cases; безопасный fallback мог дать ложный model pass | D7/tasks1.2/7.4: fixed32/16 manifest,≥12eligible mixed holdout +≥4ineligible cases, eligibility до output, fallback/timeout/skipped eligible=неуспех B; два evaluation scenarios, bounds с system/schema overhead проверяются fake payload |
| S5 / P3 | No-retry был назван для SDK/caller, но не требовал доказательства actual Wolverine error policy | D5/tasks5.1/5.5: scoped no automatic retry на новых messages; CI fault после possible inference/accounting/delivery, остальные process/NL policies не меняются |
| S6 / P3 | Слово measured могло выдать выбранный12s/4sbudget за результат измерения | D6 обозначает предложенный предел; existing TS client timeout15s сверён в source, performance доказательства только future CI/authorized eval |

Все S1–S6 закрыты в planning artifacts. Текущее согласие quote отдельно сверено: `flights-page.component.ts` устанавливает requiresAcceptance=true/accepted=false для каждого нового quote; новая accept-summary операция не нужна. Default-off AI и A+B recommendation сохраняются; ни реализация, ни публикация не начаты. Новая редакция:16requirements/44scenarios/24невыполненных implementation tasks. Итоговая machine-check evidence — в verification.md.

## Implementation review after source authorization

Independent reviewer: /root/fare_plan_review, read-only whole diff against exact fetched base163e3d9b467259518a11e6c6c423aa0e3f79a513. It did not run .NET, services, keys, paid/model/provider APIs or CI. This is a new source review, not reuse of the earlier planning Ready.

| Finding | Priority | Disposition and regression evidence |
|---|---|---|
| Silent optional AI fallback/presentation in UI |P2|Controlled reasons and AiOrganized label; focused12component/2browser tests; final full58browser/450web |
| Duplicate outer root conditions confirmed last supplier value |P2|Type-level offer wrapper converter detects duplicates; contradictory/equal duplicate raw JSON RED ->GREEN |
| Wrapper converter skipped earlier malformed mandatory member |P2|Deserialize every occurrence preserves strict price/party/itinerary/services behavior; raw mandatory-first duplicate regressions |
| Ancillary informational allowance891 differed from rich Int32 |P2|Same Int32 limits for readonly included allowances; purchasable quantities1..99 unchanged;194API-client tests |
| Generic transport/fakeledger suite insufficient for production composition/accounting |P2|Actual Host/twoAI Programs via retained disposable transport fixture, production EF ledger, fake model only; source built, runtime CI pending |

Reviewer re-read all five fixes and reported Ready for next acceptance gate, zero open confirmed P1/P2. Source runtime, supplier APIs, mandatory CI, actual model/human evaluation and deployment remain unverified. Final small delta (model noncooperative WaitAsync, unsupported historical9adult fallback and generated OpenAPI field) is separately rechecked before closing7.2.

Root self-review also covered snapshot-before-commit/re-quote/replay/held expiry, field-presence/decimal/scope/unknown semantics, typed plan completeness and source binding, deadline/inflight accounting, no-retry boundaries, auth/history/PII, TS decoding and lifetime guards, schema/cache compatibility and honest evaluation denominator. Snapshot and accounting changes are source-only; no financial hashes/authorities/CI retry policies were changed.

Final delta recheck by /root/fare_plan_review: Ready,7.2 may close, no newP1/P2. Re-read model WaitAsync/admission retention, unsupported9adult history/extras notice, generated optional OpenAPI field and CI schema assertions. It performed no runtime/API/test actions.4.3/5.4/5.5/7.3 remain open for mandatory CI. Final source tracking:20/24 complete,4runtime acceptance tasks pending publication.

CI regression delta independently rechecked by /root/fare_plan_review: Ready, no newP1/P2. Non-object baggage array members preserve inventory and produce scoped unknown+invalid notice; absent/null collection remains ordinary unknown; mandatory-invalid guards unchanged. Provider fakeHTTP test retains2offers/2adults/4legs; transport chain checks ignore whitespace only. Reviewer did not run tests/runtime. Merge still gated on complete next-head CI/PR E2E.

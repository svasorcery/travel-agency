# Исследование поставщиков Hotels M2

Срез официальных публичных источников: 2026-10-19. Это сохранённый ход исследования до окончательного выбора. Пользователь затем выбрал Duffel + Hotelbeds для собственных исходников и автономных fictional tests. Решения и границы актуального предложения находятся в [design.md](design.md); прежние фразы о ещё не выбранной паре и будущей подготовке спецификаций ниже являются историей исследования. Это не второй план и не runtime permission.

# Design

## Context

**Состояние: OpenSpec explore / исследование для выбора поставщика, 2026-10-19. Это ещё не согласованный implementation design.** После первоначальных вопросов пользователь поручил заново проверить рынок и ограничения до выбора. Основной агент и два независимых исследователя проверили публичные официальные документы; аккаунты, credentials и supplier endpoints не использовались. Наличие документации не доказывает доступ конкретного человека, лицензию или работу sandbox.

Мотивация и scope — [proposal.md](proposal.md). Подтверждено пользователем: M2 включает **поиск и покупку** второго настоящего поставщика; партнёрского доступа сейчас нет. Последующее решение2026-10-19: сначала прояснить права Duffel; затем выбирать второго либо, если Duffel не подходит, строить агрегацию двух других suppliers, сохраняя M1 отдельно. Рост scope до двух новых интеграций принят как допустимый путь планирования, без разрешения реализации. Остальные решения ниже обозначены как предложения.

Git проверен: чистый `dev`, `HEAD = origin/dev = 75dfba532c0f321286630e3d04fbea70347903a7`; удалённый SHA получен через `git ls-remote origin refs/heads/dev`. Контрольный `fd94b9c91cd4345f1b5a684a3c8b73d2bc84e6b9` является предком, а не требуемой точкой отката. Ветка не переключалась. Обычные CI M1 сообщены пользователем; это исследование не запускает и не переоценивает CI или L1.

### Проверенная основа Hotels

| Факт в текущем коде | Основание / следствие для M2 |
|---|---|
| Search, Booking и optional ReservationReader уже разделены | [StayValues.cs](../../../../modules/hotels/Travel.Modules.Hotels.Core/StayValues.cs). Не нужен общий интерфейс всех supplier endpoints. |
| Поиск обходит `IEnumerable` последовательно, сохраняет statuses, Complete/Limited/Unsupported/Failed | [HotelSearchService.cs](../../../../modules/hotels/Travel.Modules.Hotels.Application/HotelSearchService.cs). M2 меняет исполнение и aggregate semantics, сохраняя смысл каждого источника. |
| В property нет нормализованных координат и structured matching evidence; room — display string; board/cancellation преимущественно scoped text | [Core](../../../../modules/hotels/Travel.Modules.Hotels.Core/StayValues.cs), [Duffel mapper](../../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Duffel/DuffelStaysMapper.cs). Нельзя строить доказанное сопоставление или разбирать условия обратно из русских строк. |
| Reservation сохраняет provider/environment/account, quote revision, protected context/party и единственную attempt | [HotelReservations.cs](../../../../modules/hotels/Travel.Modules.Hotels.Application/HotelReservations.cs), [service](../../../../modules/hotels/Travel.Modules.Hotels.Application/HotelReservationService.cs). Это исходная authority покупки, не canonical property/card. |
| Capacity получает provider key, но текущая реализация имеет один набор Duffel counters | [HotelCapacity.cs](../../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Duffel/HotelCapacity.cs). Добавление второго provider требует реального выбора его limiter; передача другого key сама по себе изоляции не даёт. |
| Observation runner читает только известную booking identity и optional reader выбранного provider | [HotelObservationRunner.cs](../../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Observations/HotelObservationRunner.cs). Supplier с operation-status до hotel booking ID требует отдельного обоснованного расширения, а не выдуманного booking ID. |
| Весь Host-visible wiring уже через Hotels Api.Composition | [facade](../../../../modules/hotels/Travel.Modules.Hotels.Api/Composition/HotelsModule.cs), [ADR0023](../../../../docs/adr/0023-module-api-facades-and-cross-cutting-ownership.md). Host не выбирает supplier DTO/client. |

Использованы root/module AGENTS, [current-state](../../../../docs/architecture/current-state.md), roadmap/Hotels из [концепта](../../../../docs/superpowers/specs/2026-05-03-travel-platform-concept.md), corpus [M1](../2026-10-19-hotels-m1-duffel-booking/design.md), его три specs/tasks/[verification](../2026-10-19-hotels-m1-duffel-booking/verification.md), ADR0001/0006/0007/0008/0013/0014/0023 и privacy precedent0024. Flights [ранжирование](../../../../modules/flights/Travel.Modules.Flights.Application/Search/OfferRanker.cs) и amended ADR0014 полезны как опыт проверки порядка/валют/cache, но Hotels не импортирует Flights internals.

Майские названия suppliers, free sandbox и старые примеры — исторические гипотезы. [Dependency advisories](../../../../docs/operations/2026-10-16-dependency-advisories.md) и [braces backport](../../../../docs/operations/2026-10-16-braces-security-backport.md) прочитаны: local reviewed backport сохраняет upstream3.0.3/version-range finding; runtime/artifact proof нельзя заменить waiver или downgrade OpenSpec. В этом change dependencies, locks, tools и backport не изменяются.

## Goals / Non-Goals

**Goal исследования:** выбрать интеграцию, которую реально можно законно и безопасно проверить в нашем bounded showcase, а затем утвердить конкретные contracts и задачи. Размер каталога и маркетинговое «free API» не заменяют пригодность sandbox, права на данные и recovery.

**Предложенная граница первого вертикального среза:** один номер/1–2 взрослых, небольшой географический набор и проверенные property mappings; параллельный поиск двух разрешённых источников; сравнение; явный выбор source-specific rate; свежие условия/consent; тестовая покупка и private result/reload. Если участие Duffel подтверждено, достаточно одной новой интеграции; иначе планируются две новые. Существующий Duffel M1 happy path сохраняется в своём scope. Это не сокращение M2 до поиска.

**Не предлагаются:** больше двух источников внутри M2 aggregation, global catalogue, fuzzy/ML auto-merge, универсальный aggregator/booking engine, новые services/queues/frameworks, AI ranking, дети/multi-room, real-money payments и post-booking actions. Сохранённый отдельно M1 не считается третьим источником выдачи M2. Обязательные supplier certification cases могут потребовать пересмотра этой границы до выбора поставщика; их нельзя скрытно добавить в реализацию.

## Decisions

### D0. Сначала доступ и права пары источников

Пока нет подтверждённого победителя. Различаем: техническая возможность; публичное разрешение; явный запрет; отсутствие сведений; согласованный доступ конкретного аккаунта. «В документации есть mapping API» не равно «можно склеить его данные с Duffel».

**Новый существенный факт:** [Duffel Services Agreement](https://duffel.com/services-agreement), updated17Jun2026, §2.5(d) запрещает metasearch, если оно явно не разрешено соглашением. §3.1 требует соблюдать supplier restrictions; §3.2 — обновлять контент минимум еженедельно и не скачивать/хостить supplier images. §2.3 отдельно считает search-to-order для Stays. Нужно уточнить применимость metasearch ограничения к нашему OTA flow с покупкой внутри приложения и права на matching/comparison. Этот пункт касается будущей M2, не отменяет реализацию M1 и не разрешает реальные обращения к Duffel.

Нельзя считать, что полный booking автоматически снимает metasearch запрет, что Test-mode исключён из условий, либо что разрешение второго поставщика покрывает Duffel. Конкретное written permission/договорное исключение должно покрывать оба источника, нужную среду и использование данных.

### D0a. Углублённая проверка Duffel — 2026-10-19

**Вывод для реального использования API:** Duffel пока нельзя ни окончательно исключить, ни считать разрешённым участником M2. Найдено официальное упоминание сравнения с другими providers, но именно наш Hotels flow публично не согласован. Для внешнего выполнения следующий шаг — конкретный письменный ответ Duffel; дальнейшее чтение общих статей его не заменит. Это не обязательный предварительный шаг для всякой работы над собственным автономным кодом: уточнение D0b ниже. Гарантию отсутствия любых блокировок/претензий это исследование не даёт.

Основной агент и два новых независимых исследователя проверили договор, product/docs/FAQ/changelog и границы выводов. Аккаунт, индивидуальный Order Form и переписка не изучались; реальных запросов/регистраций/контактов не было.

| Основание | Установленный факт |
|---|---|
| [Services Agreement](https://duffel.com/services-agreement), редакция17Jun2026, §2.5/12.1 | Есть запрет metasearch, но нет определения; эксклюзивность не установлена. Конкурирующий сервис/redistribution имеют ограничения. |
| Там же, §1.1/3.1/3.2 | Test-доступ не содержит найденного исключения для OSS/обучения; действуют supplier rules, обновление контента и ограничения изображений. |
| Там же, §9.2/4.5/7.1/7.1A/8.2 | Возможна приостановка всех/части Services. Фиксированного metasearch штрафа нет; Fines связаны с Orders; предусмотрены indemnity и исключения из liability cap. Сумма риска не установлена. |
| Там же, §2.3/4.1/10/12.4/12.8 | Раздельный Flights/Stays search ratio; pricing/account условия и конфиденциальность; изменения договора и применимость согласованных документов требуют проверки. |
| [Changelog18May2021](https://changelog.duffel.com/announcements/1-90a73325-0915-474b-97a7-423d65e19d19) | Duffel прямо объясняет полезность fare-basis codes сравнением Duffel offers с другими providers/GDS. Это исторический Flights материал, не разрешение текущего Hotels M2. |
| [Ulysse story, Dec2020](https://duffel.com/blog/ulysse-uses-duffel-to-put-customers-first), [NDC/GDS guide](https://duffel.com/ndc/how-does-ndc-compare-to-gds) | Описаны OTA с частичной долей Duffel supply и потребность в разных content providers. Это аргумент против универсального тезиса «второй поставщик запрещён», а не известные нам права чужого договора. |
| [Test mode](https://duffel.com/docs/api/overview/test-mode), [Test Hotels](https://duffel.com/docs/guides/test-hotels) | Документы описывают тестирование; найденного разрешения именно на external aggregation/matching нет. Отсутствие реального списания не означает договорное исключение. |
| [Search FAQ16Feb2021](https://help.duffel.com/hc/en-gb/articles/360019684099-Is-there-any-limit-to-how-many-searches-I-can-make), [Pricing](https://duffel.com/pricing) | Старый Flights FAQ предлагает обсудить use case с поддержкой. Числа1500:1/$0.005 опубликованы для Flights; считать их нашей Stays квотой/ставкой нельзя. |
| [Stays concepts](https://duffel.com/docs/api/overview/stays-key-concepts), [room mapping changelog11Apr2024](https://changelog.duffel.com/announcements/rates-can-now-map-to-a-single-room-across-multiple-sources) | Внутреннее объединение источников самим Duffel не предоставляет нам автоматически аналогичные права на внешнее соединение данных. |

**Оценка основного агента:** запрет всех сравнений был бы слишком широким выводом; разрешение на любое сравнение из OTA/booking документации — тоже. Не найдено самостоятельного общего запрета сопоставлять hotel IDs, аналогичного LiteAPI, но это не закрывает вопрос назначения использования и условий конкретного content. Убедительного публичного допуска всех шагов M2 одновременно не найдено. Старые Flights материалы, слово «sandbox», доступный endpoint, уже написанный mixed-search код и отсутствие реакции поставщика не заменяют согласование.

**Сценарии для решения:**

| Наш вариант | Решение до дополнительного подтверждения |
|---|---|
| Существующие собственные fictional fixtures без API/данных Duffel | Не создают нового внешнего использования для M2; сохраняем отдельную source/fictional границу доказательства. |
| Реальный Duffel Test + другой supplier, общий поиск/matching/ranking, покупка внутри приложения | Разрешение не подтверждено: внешнее выполнение не начинать. |
| Тот же flow в public demo либо Live | Нужны отдельные scope/terms/activation; разрешение private sandbox не переносится автоматически. |
| Чужой rate использует Duffel фото/описание или canonical mapping строится из Duffel data | Отдельно включить эти операции в запрос прав; не считать разрешёнными общей фразой «OTA». |
| Duffel не участвует, M2 использует два других разрешённых источника | Принятый пользователем запасной путь. Исключить Duffel из fan-out, matching index, ranking и источников контента M2; M1 оставить самостоятельным. |

Последняя строка — предложение фактического разделения использования, не юридическая гарантия. Другая вкладка, URL или account сами по себе не делают запрещённый сценарий разрешённым. Не маскировать сравнение названиями экранов и не обходить ограничения сменой ключей. При подготовке общего продукта проверить фактическое разделение Duffel M1 и M2 по применимым условиям; вопрос об их сосуществовании включён в запрос Duffel. Необходимость отдельного разрешения на само такое сосуществование публичными документами не установлена. Отсутствие ответа не создаёт бессрочный блок fallback; сейчас M1 остаётся default-off/fictional без новых supplier calls в рамках этого исследования.

**Что считать достаточным ответом для следующей стадии:** ответ уполномоченного Duffel, привязанный к продукту/аккаунту, средам и описанным операциям. Если нужна оговорка к договору — согласованный Order Form/addendum с явным разрешением и разрешением коллизии §2.5; его юридический эффект проверяется по реальному документу. Это рекомендуемая форма достаточного подтверждения, а не найденное требование исключительно бумажной подписи. «API поддерживает», общий onboarding reply, молчание или ответ о Flights не закрывают Hotels.

Проверить охват: matched hotel IDs/crosswalk; общая карточка и принадлежность facts/images; сравнение/ranking; сохранение transient search/accepted terms; правила supplier content; personal OSS eligibility/фактическая юрисдикция; private Test/public demo/Live отдельно; Stays quotas/fees при0orders. Индивидуальные договоры, ответы и реальные supplier measurements могут быть конфиденциальны: не публиковать их автоматически в OSS corpus. Для review сохранять только разрешённое содержание и безопасную ссылку на evidence.

#### Готовый запрос в Duffel — не отправлен

Канал: официальный Help Centre / [help@duffel.com](mailto:help@duffel.com), указанный в search FAQ. Название ниже — subject, а не подтверждение существования партнёрства. Account/location/legal details нужно передать приватно при обращении; они здесь не выдумываются.

**Subject: Stays permitted use: multi-source in-app booking, property matching and sandbox-only OSS project**

Hello Duffel team,

I am developing an independent, non-commercial open-source travel portfolio project. Its current Duffel Stays implementation is default-off and Test-only. The proposed multi-source feature has not been enabled against Duffel, and I am not requesting live activation at this stage.

The intended flow is:

1. A traveller submits one hotel search. Our backend queries Duffel and an independent accommodation supplier in parallel.
2. We match records for the same physical hotel, group them into a property card, and display and rank the suppliers' offers using whole-stay prices, taxes/local charges, meals and cancellation conditions. Each offer retains its own source and conditions.
3. The traveller selects one specific offer and completes checkout inside our application. The booking is submitted only to the supplier of that offer. There is no automatic purchase through another supplier after an uncertain outcome, no external metasearch redirect and no resale of an API feed.

Under the current Services Agreement, particularly clauses 2.5 and 12.1, is this specific Hotels flow permitted, or would you classify it as prohibited metasearch? If express permission is required, please identify the authorised approval process and the document that would incorporate it into our applicable agreement.

Please confirm the position separately for private Test mode with fictional guests, a public sandbox demonstration, and any future live use. May an individual run this non-commercial OSS sandbox integration without an operating travel agency? We can provide the owner's actual location and account details privately for eligibility checks.

Please also confirm permitted cross-provider property-ID mappings and their retention; metadata/images on combined cards containing another supplier's rates; short-lived search/rate snapshots; protected historical accepted booking terms; required attribution, refresh/deletion and supplier-specific restrictions. Please specify Stays search quotas, search-to-book rules and any fees when there are no real bookings, rather than Flights allowances.

Your 18 May 2021 fare-basis-code announcement discusses comparing Duffel offers with other providers. We recognise that this is historical Flights guidance and are asking for the current Stays position, not assuming it transfers.

If the combined flow is not permitted, may Duffel remain an independent single-source booking path while a separate part of the same product aggregates two other suppliers, with no Duffel searches, prices, metadata, images or mappings used in that aggregation? Please identify any restrictions on that arrangement too.

We would publish original integration code and wholly fictional fixtures only, excluding credentials, personal data, real supplier content and confidential commercial correspondence. Please let us know whether this publication needs any further approval.

Please give an operation-specific written answer and, if necessary, route it to the team authorised to approve this use. A link to a generic API integration guide would not resolve the scope questions above.

Thank you.

#### После ответа

- Ясное разрешение — reconcile условия в том же M2, выбрать второго provider и только затем закончить specs/tasks для согласования. Разрешение supplier не заменяет разрешение пользователя на реализацию/API calls.
- Частичное разрешение — выписать разрешённые/запрещённые операции и предложить конкретную границу M2; не терять обязательный второй реальный booking flow.
- Отказ — перейти к двум другим providers и отдельно проверить права каждого на mutual matching/comparison; существующий M1 не удалять и не переписывать автоматически.
- Нет ответа или общий ответ — статус «не подтверждено», без пробного трафика, повторных регистраций и допущения, что молчание означает согласие. Можно выбрать исключение Duffel из M2 как планировочный путь без ожидания бесконечного исследования.

### D0b. Завершение исследования: исходники и автономное портфолио

Пользователь уточнил, что, скорее всего, будет публиковать только код без хостинга, и попросил независимое решение. Это намерение, не окончательное обещание никогда не использовать API и не разрешение реализации/публикации.

**Моя рекомендация как основного агента:** продолжать проектирование M2 для исходников и автономной демонстрации с сохранённым Duffel. Не требовать ответа Duffel до каждого шага такой работы и не добавлять две новые интеграции только из-за неразрешённого вопроса фактического API use. При этом **не принимать допущение «Flights позволяет — значит Hotels тоже»**. Ответ для source-only опирается на собственный характер кода и отсутствие внешних вызовов/данных, а не на перенос разрешения.

Два независимых исследователя отдельно проверили этот follow-up; основной агент сверил первичные источники и текущие Hotels fixtures:

- [MIT лицензия официального JS SDK](https://github.com/duffelhq/duffel-api-javascript/blob/main/LICENSE) разрешает распространение соответствующего software с notices; [объяснение open source](https://duffel.com/blog/opening-up-to-open-source) поддерживает обычную модель открытых клиентских библиотек. Это **не** лицензия на Supplier Data, весь сайт или любой runtime use.
- [Текущий client guide](https://duffel.com/docs/guides/javascript-client-library) предлагает использовать клиент в собственной codebase; [Stays guide](https://duffel.com/docs/guides/getting-started-with-stays) — собственный язык/HTTP client. Это обоснование обычной интеграционной разработки, не общее согласование multi-source Stays.
- [Website Terms§4–5](https://duffel.com/terms) различают сайт/Services и защищают материалы сайта. Не переносить MIT SDK на скопированный текст документации, photos/logos или реальные responses. Прямого публичного требования согласовывать каждый самостоятельно написанный integration repository не найдено. Это результат ограниченного исследования, не юридическая гарантия.
- Общий metasearch пункт [Services Agreement§2.5(d)](https://duffel.com/services-agreement) не ограничен Flights. Отдельного повторения со словом Hotels для применимости не требуется. Отсутствие хостинга не является найденным исключением. Названия Services и Documentation в договоре разные; оба имеют свои ограничения.

В текущем [Hotels Infrastructure project](../../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Travel.Modules.Hotels.Infrastructure.csproj) нет Duffel SDK dependency; [provider](../../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Duffel/DuffelStaysProvider.cs)/DTO/mapper — собственный .NET HTTP слой. Все5проверенных [JSON fixtures](../../../../tests/hotels/Travel.Modules.Hotels.Tests.Integration/Fixtures/search.json), включая rates/quote/booking201/booking-get, используют `acc_fictional`/`Fictional Garden Hotel`. [Demo](../../../../modules/hotels/README.md) описывает Testing Host с fake HTTP handler. Это проверка текущего содержимого/границы, **не полный аудит происхождения всех строк, истории, лицензий и содержимого репозитория**. Корневая MIT лицензия распространяет наши права и не может сама предоставить отсутствующие права третьих лиц.

| Режим | Независимое решение для следующего планирования |
|---|---|
| Свои исходники и собственные fictional fixtures, никакого Duffel traffic | Продолжать; не найдено основания ставить runtime permission обязательным предварительным условием всего source-only M2. |
| Автономные fake-HTTP/DB/browser tests | Проверять собственную реализацию после согласования source/spec/testing scope; они не устанавливают supplier acceptance. |
| Localhost с настоящим Test API/token | Это уже внешнее использование сервиса; D0a остаётся актуален независимо от хостинга. |
| Public demo или Live с поставщиком | Самостоятельные access/terms/activation и пользовательская authority; source readiness их не заменяет. |

Предлагаемый source-only M2 сохраняет реальные provider-specific implementations документированного протокола; fake HTTP используется для их tests, а не выдаётся за второго внешнего поставщика. В результате честно принимается source/fictional поведение, а реальная совместимость, доступ и договорное разрешение остаются неподтверждёнными. Если документация нового supplier закрыта или не позволяет разработать нужный wire contract, выбрать его для source-фазы всё равно нельзя по выдуманным DTO.

Для будущего репозитория/README: собственный код, сохранённые notices разрешённых заимствований, вымышленные данные/собственные assets; default-off внешние adapters; явная граница source/fictional versus supplier acceptance. Не сохранять реальные supplier responses/content/гостевые данные или конфиденциальные соглашения ради демо, не обещать пользователям право запуска из самого факта наличия MIT LICENSE. Публикация требует своего поручения; сейчас ничего не публикуется.

**Прямой ответ на «к коду не могут придраться»: такую абсолютную гарантию дать нельзя.** Но и останавливать оригинальный автономный код только из-за нерешённой квалификации будущего API сценария оснований не найдено. Для портфолио разумен предложенный source-only путь; переход к настоящим Test calls или Live вернёт вопрос применимых условий. Готовый запрос D0a сохранён для этого случая и не является обязательной текущей зависимостью source-only разработки.

### D1. Перепроверенные основные кандидаты

Публичные документы проверяются на дату исследования; правила конкретного договора могут отличаться. В таблице нет утверждений о фактической доступности нашего аккаунта или санкционной/региональной допустимости.

| Кандидат | Подтверждённое | Вывод для нашего выбора |
|---|---|---|
| Hotelbeds / HBX | Есть Availability → optional CheckRate → Booking; evaluation после регистрации50requests/day. [Getting Started](https://developer.hotelbeds.com/documentation/getting-started/), [workflow](https://developer.hotelbeds.com/documentation/hotels/booking-api/workflow/). | Технически близок текущим Search/Prepare/Create; evaluation quota не годится для нагрузки. |
| Hotelbeds: mapping | [Hotels best practices](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/best-practices/) прямо запрещает hotel mappings в test/evaluation/pre-production. RateKey нельзя разбирать; CheckRate нужен для RECHECK; Booking timeout минимум60s. | Без уточнения/исключения нельзя планировать реальное test matching. Отдельные fictional unit fixtures не докажут разрешение или работу реальной дедупликации. |
| RateHawk / ETG | [Integration Guide](https://docs.emergingtravel.com/docs/integration-guide/) требует registered partner и development credentials через account manager; есть geo/region/hotel-ID search, hotelpage, prebook, form/finish и получение final status. | Перспективный кандидат для полного flow, но не self-service гарантированный доступ. Нужны API/environment scope и коммерческие условия. |
| RateHawk: sandbox | [Sandbox](https://docs.emergingtravel.com/docs/fundamentals/sandbox/) моделирует поиск/booking без реальных финансовых операций; card `now`,3DS и часть cut-off scenarios недоступны. [Requirements](https://docs.emergingtravel.com/docs/integration-requirements/) предусматривают mapping test hotels при certification. [Limits](https://docs.emergingtravel.com/docs/fundamentals/request-limits/) задаются по endpoint/key. | Test mapping в своей системе предусмотрен; это не полная лицензия на cross-source reuse. Rate-limit examples не являются нашей квотой. |
| LiteAPI / Nuitee Connect | Это переименование одной интеграции. [Terms§3/§5](https://liteapi.travel/terms/) запрещают mapping к third-party sources/datasets, enrichment mapping systems и storage вне разрешённого scope. [Pricing](https://docs.liteapi.travel/reference/api-pricing-usage-costs) обусловливает free core reasonable look-to-book; Places$0.01/request, price index$0.05/request. | Под стандартными публичными условиями не подходит центральной M2 dedup. Нельзя обходить запрет собственным matcher вместо платного mapping endpoint. |
| Amadeus | [Официальные developer guides](https://github.com/amadeus4dev/developer-guides) deprecated/archived17Jul2026, направляют к [statement](https://amadeus.com/en/industry-messaging/statement-regarding-amadeus-for-developers-portal); [портал](https://developers.amadeus.com/) больше не прежний Self-Service. | Старый бесплатный Self-Service sandbox не рассматривается как действующее предложение. Enterprise — другой процесс доступа, без доказанного преимущества для нас. |
| Expedia Rapid | [Setup](https://developers.expediagroup.com/rapid/setup) требует approved partnership, restricted development key и site review; test.ean.com отделён от production. [Rate limiting](https://developers.expediagroup.com/rapid/lodging/shopping/rate-limiting) зависит также от hotels/rooms/stay length. | Полноценный booking API; сильный технический резерв. Free self-service доступ, наша квота/стоимость и права на смешанный показ не установлены. |
| Booking.com Demand | [Prerequisites](https://developers.booking.com/demand/docs/getting-started/prerequisites): Managed Affiliate, согласованный контракт, Partner Centre и token/affiliate identity. Полный booking существует в [v3.3 API](https://developers.booking.com/demand/docs/open-api/3.3/demand-api). | Не считать обычную affiliate регистрацию или connectivity/channel-manager API доступом к Demand orders. |
| Booking.com: sandbox/terms | [Sandbox](https://developers.booking.com/demand/docs/getting-started/sandbox) ограничен50/min; тестирование accommodation booking flows **including payments** требует реальной карты и временных charges. [Legacy permitted use](https://legacy.developers.booking.com/api/commercial/index.html?page_url=permitted-use) ограничивает content при price comparison. | Подходящий безденежный full flow не подтверждён. Условие про карту не переносится автоматически на все варианты pay-at-property; их доступность требует отдельной проверки. Legacy правило — предупреждение для договорной проверки, не доказанный полный текст прав v3.3. |

### D1a. Дополнительный обзор рынка

Проверены другие варианты, чтобы не ограничивать выбор майским списком. Здесь «новый кандидат» означает дополнение нашего обзора, а не заявление, что компания появилась после мая2026.

| Кандидат | Найденная возможность | Почему не подтверждённый свободный старт |
|---|---|---|
| DidaTravel | Полные PriceSearch → PriceConfirm → BookingConfirm → BookingSearch; [onboarding](https://apidoc.didatravel.com/getting-started/go-live-process.html) предусматривает mapping, test credentials и certification. | NDA/manager/company questionnaire/IP allowlist; далее pre-production/live tests. Стоимость, индивидуальная eligibility и cross-source license не установлены. |
| Dida: актуализация | [Price Search](https://apidoc.didatravel.com/booking-api/price-search.html)§15 отменяет `DidaApiTestID`, хотя примеры ещё показывают его. Нужен dedicated test account; default lowest/cache price —2persons, multi-hotel realtime требует согласования, nationality обязательна. | Нельзя пользоваться старым sample key или считать default цену предложением для1adult. Точное occupancy и фактическая nationality станут необходимыми входами. |
| Hotel Trader | [Sandbox](https://hoteltrader.knowledgeowl.com/help/sandbox-endpoints-credentials), GraphQL Search/Quote/Book/GetReservation; [Quick Start](https://hoteltrader.knowledgeowl.com/help/quick-start) и [buyer onboarding](https://www.hoteltrader.com/travel-buyers/) предусматривают mapping/сертификацию. | Profile/token создаёт команда поставщика; автоматическая выдача физлицу/OSS не подтверждена. Поддерживается поиск по property IDs, не location/region: нужен разрешённый небольшой source catalogue. |
| Hotel Trader: данные | [Technical Expectations](https://hoteltrader.knowledgeowl.com/help/pull-api-technical-expectations) требуют live search; pre-search cache rates без preapproval может отключить доступ. [FAQ](https://www.hoteltrader.com/support-center/) заявляет free service и одну commercial agreement; это не бесплатные проживания и не лицензия общего каталога. | Нужно согласовать static matching/display, transient selection snapshots, quota/L2B и стоимость при0sales. Sample Book с `paymentInformation:null` не доказывает безкарточный доступ нашего contract. |
| TBO Holidays | [API](https://www.tboholidays.com/partners-width-us.htm) с test environment/full booking; [registration](https://apiintegration.tboholidays.com/Registration.aspx) запрашивает agency details, дальнейшая настройка через manager. | Нет подтверждённого individual self-service admission/цен/quota/mapping rights. [Официальный XMLv7 PDF](https://api.tbotechnology.in/pdf/TBOHolidays_HotelAPI_v7.pdf) имеет ревизию2018: lookup/duplicate examples исторические, нужен текущий поддерживаемый wire contract. |
| WebBeds | Full availability/content/book/amend/cancel и property/room mapping описаны в [buyer support](https://www.webbeds.com/buyers/support/). | Commercial agreement и technical approval; не установлен открытый бесплатный sandbox, license/quota для нашего проекта. B2B резерв. |
| HyperGuest | Direct inventory/full booking и marketplace без отдельных договоров с каждым отелем. [Travel-provider terms](https://www.hyperguest.com/platform-terms-of-service-for-travel-providers), [marketplace terms](https://www.hyperguest.com/platform-marketplace-terms). | Business customer scope, договор/AnnexA pricing; не установлены свободный sandbox, account quotas и пригодный нам lost-response protocol. |
| Travelgate HotelX | [Onboarding](https://docs.travelgate.com/kb/getting-started/onboarding/), [key concepts](https://docs.travelgate.com/docs/get-started/key-concepts/): Search/Quote/Book, mapping и test connections. | Connectivity marketplace требует договора и seller credentials. Сам gateway не даёт независимый второй inventory source; нельзя посчитать demo connection реальным supplier. |
| Vio.com | [Partner API](https://developers.vio.com/docs/getting-started) ориентирован на comparison и booking links/handoff, предусматривает mapping через account manager. | [Текущая affiliate страница](https://www.vio.com/affiliates/api) прямо не принимает pre-launch проекты без established traffic. White-label/MCP не доказывают доступный нам native booking API и не соответствуют выбранному полному Hotels purchase flow. |

Не углублялись в generic scraping/price-only/mapping-only services и channel managers: они не дают требуемый второй полный purchase capability. Новые MCP/AI оболочки сами по себе не меняют доступ, лицензию или наличие book endpoint.

### D1b. Сравнение по существенным протокольным рискам

| Кандидат | Что конкретно меняет проектирование |
|---|---|
| Hotelbeds | [Tax Breakdown](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/tax-breakdown/) по умолчанию выключен и требует activation. Отсутствующие taxes не становятся0. [Certification](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/certification-process/) включает live refundable booking/cancel: это будущие реальные эффекты, не обычный source test. |
| ETG | Рекламируемый free key не равен company/partner admission. [Requirements§3.2](https://docs.emergingtravel.com/docs/integration-requirements/) запрещают cache Hotelpage/Prebook-from-Hotelpage responses; [guide](https://docs.emergingtravel.com/docs/integration-guide/) одновременно упоминает storage до1h. Расхождение и границу исторического accepted-terms receipt нужно уточнить; receipt не объявляется автоматически запрещённым или разрешённым cache. [Create process](https://docs.emergingtravel.com/docs/b2b-api/booking/create-booking-process/) и [Start process](https://docs.emergingtravel.com/docs/b2b-api/booking/start-booking-process/) различаются по effects/recovery; new-ID form retries нельзя переносить на фактическую покупку. |
| Expedia Rapid | [Content](https://developers.expediagroup.com/rapid/lodging/content/about-content-api) прямо предусматривает matching property lists других suppliers. [Handling booking requests](https://developers.expediagroup.com/rapid/flights/reference/handle-booking-reqs?locale=en_US) явно относится и к Lodging: loss не доказывает failure; retrieval использует исходный affiliate reference/email и может лагать. Документ допускает same-ID resend в некоторых ветках; наш запрет повторного create сохраняется, совместимость такого subset надо подтвердить. |
| Booking.com | [v3.3 schema](https://developers.booking.com/demand/docs/open-api/3.3/demand-api) использует `price.display`, `price.total`, `charges/included_in`; часть narrative guides ещё описывает v3.2 `book/extra_charges`. [Migration FAQ](https://developers.booking.com/demand/docs/migration-guide/v3/migration-faqs) запрещает cache price/availability. Наш selection snapshot нельзя молча признать разрешённым cache. [Orders guide](https://developers.booking.com/demand/docs/orders-api/order-preview-create) не установил в этом исследовании точный accommodation-specific replay/lookup contract; car примеры не переносятся на hotels. |
| Dida | [PriceConfirm](https://apidoc.didatravel.com/booking-api/price-confirm.html) содержит included/excluded fees, но помечает их reference-only; [BookingConfirm](https://apidoc.didatravel.com/booking-api/booking-confirm.html)/[BookingSearch](https://apidoc.didatravel.com/booking-api/booking-search.html) имеют source/client references. Lookup по client reference полезен, но не доказывает absence после первого not-found. Cancellation time по Beijing требует явной нормализации, не hotel/browser timezone. |
| Hotel Trader | [Book](https://hoteltrader.knowledgeowl.com/help/book) показывает duplicate client-confirmation rejection; [GetReservation](https://hoteltrader.knowledgeowl.com/help/get-reservation) поддерживает lookup по client confirmation. Это полезно для lost response до hotel ID, но consistency/retention гарантия не установлена. [Pricing guidelines](https://hoteltrader.knowledgeowl.com/help/best-practices-and-guidelines) различают gross/net/tax/pay-at-property и свежую Quote. Требуемые guest ages/fields нужно уточнить по schema, не подставлять возраст из sample. |

### D1c. Предшествующий отбор по условиям внешнего доступа

В **проверенном наборе** не найден вариант, у которого одновременно подтверждены свободный бесплатный доступ для нашего solo/portfolio формата, full search+booking без реальных денег и право на требуемую агрегацию с Duffel. Это не утверждение, что таких условий нельзя получить по соглашению.

**Предварительный вывод первого обзора, не решение пользователя:** для проверки внешнего доступа выделялись **RateHawk/ETG и Expedia Rapid**. У ETG описан отдельный sandbox и test mapping; у Rapid предусмотрен cross-supplier property mapping и documented lost-response recovery. Для обоих нужны пригодные access/terms. **DidaTravel и Hotel Trader** — альтернативы. **Hotelbeds** для настоящего test matching требует разрешить explicit restriction. После уточнения о source-only портфолио инженерный приоритет пересмотрен в D1d; этот абзац не является текущим выбором поставщика.

LiteAPI под стандартными terms исключается из текущей cross-source постановки; Amadeus больше не предлагается по старой Self-Service модели; Booking не является простым безденежным sandbox кандидатом; Vio не подходит pre-launch/full-native-booking требованию; Travelgate не заменяет supplier contract. Никакой выбор не снимает независимый Duffel metasearch gate.

Нельзя обещать, что ETG или Rapid примет физлицо, выдаст нужную среду или разрешит конкретный display/retention scope. Выбор для source-only реализации не устанавливает эти права; source и внешнее acceptance должны оцениваться раздельно. Обобщённый adapter framework в ожидании доступа не предлагается.

### D1d. Итог технического отбора: сколько новых интеграций

Последнее поручение пользователя оставляет открытыми **Duffel +1** и **два новых provider с отключённым Duffel**. Это разрешение продолжить исследование, не согласование конкретной пары. Основной агент и два исследователя сравнили не количество hotels в рекламе, а полный no-card booking subset, восстановление, доступность wire docs и изменение текущего M1.

| Кандидат | Что реально меняется относительно M1 | Оценка для source-only первого M2 |
|---|---|---|
| **Hotelbeds/HBX** | Availability → CheckRate по правилам rate → один Booking. Local quote без обязательного supplier quote ID; opaque rateKey в adapter-owned context. Обычный booking example без карты. | **Первый кандидат:** наименьший пересмотр lifecycle. Нужны content enrichment, mTLS, точные money/conditions и честный Unknown при потерянном ответе. |
| **Expedia Rapid / affiliate_collect** | Shopping → PriceCheck → один Booking. No-card profile существует, но billing contact обязателен; нужны additional private fields и recovery по affiliate reference/email до itinerary ID. | **Второй кандидат:** подходит как ещё один новый source, но шире privacy/контактный контракт. Реальное право на EAC не установлено. |
| **RateHawk/ETG / deposit** | SERP → Hotelpage → Prebook → Form → Finish → final Status. Form/process identity и единственная отправка Finish должны переживать restart; residency и source-required contact/IP расширяют input. | Содержательный, но **существенно более сложный** вариант. Выбирать, если хотим отдельно показать durable asynchronous supplier flow; для самой агрегации он не обязателен. |
| **Hotel Trader / Direct Bill** | Один Book effect; GraphQL envelope/errors, поиск только по source property IDs, guestAges включая взрослых, nightly money evidence, reader по client confirmation. | Резерв. Дополнительные возраста/небольшой каталог и не подтверждённый полный SDL уменьшают выигрыш от короткого happy path. Возраст30 из sample не является правилом. |
| **Dida / balance** | PriceConfirm/prebook + BookingConfirm + обязательный BookingSearch final result. ClientReference/metadata сохраняются; nationality обязательна; Pending возможен и для instant inventory. | Резерв. Меньше card input, но lifecycle дороже. Excluding OnRequest не устраняет обычный Pending и необходимость final-status reader. |

**Проверенные решающие источники и ограничения:**

- HBX [workflow](https://developer.hotelbeds.com/documentation/hotels/booking-api/workflow/) и [mTLS](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/mutual-authentication/): текущая transport policy требует client certificate для Availability/CheckRate/Booking/read/cancel вместе с API key/signature. Test и Production имеют отдельные mTLS endpoints. Это не рекомендация сейчас выпускать сертификаты или читать credentials. Старые curl examples без certificate не отменяют текущую policy.
- HBX [best practices](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/best-practices/): booking timeout минимум60s, opaque rateKey, без повторной покупки. `clientReference` не объявляется доказанным idempotency key. При отсутствии booking reference после timeout допускается Unknown/manual, без list discovery/resend. Сохраняется [test-mapping prohibition](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/best-practices/): выбор исходников не делает настоящую test агрегацию разрешённой.
- Rapid [официальный PaymentRequest5.4.0](https://opensource.expediagroup.com/rapid-java-sdk/rapid-sdk/com.expediagroup.sdk.rapid.models/-payment-request/index.html) прямо определяет `affiliate_collect` без card information с обязательным billing contact. Это конкретный профиль, не обещание поддержки любых платежей; наш customer PSP/реальные деньги не добавляются автоматически. [Booking handling](https://developers.expediagroup.com/rapid/flights/reference/handle-booking-reqs?locale=en_US) требует correlation/retrieve; разрешённые там same-ID resend не переопределяют наш one-dispatch invariant.
- ETG [Form](https://docs.emergingtravel.com/docs/b2b-api/booking/create-booking-process/), [Finish](https://docs.emergingtravel.com/docs/b2b-api/booking/start-booking-process/), [Status](https://docs.emergingtravel.com/docs/affiliate-api/booking/check-booking-process/): `deposit` не требует customer card, но реальный расчёт зависит от договора/credit. Нужен known-operation reader и сохранение stages; нельзя выдавать process ID за hotel booking ID или optional details GET за final-status protocol.
- Dida [BookingSearch](https://apidoc.didatravel.com/booking-api/booking-search.html) требует окончательный ответ именно status2/3/4; Pending может длиться3min, OnRequest120min. Для bounded subset `IsNeedOnRequest:false` требуется в Search и PriceConfirm, но status observation всё равно обязателен. Hotel Trader [FAQ](https://hoteltrader.knowledgeowl.com/help/pull-api-faqs) и [Book](https://hoteltrader.knowledgeowl.com/help/book) подтверждают специфические inputs/идентификаторы, не полноту SDL.

**Качество wire evidence:** HBX имеет публичные operation docs/workflow и официальную [Postman collection](https://www.postman.com/clientapi/apitude-by-hotelbeds/documentation/h58zuu9/bookingapi); полный текущий machine-readable Swagger этим исследованием не проверен. Rapid имеет [latest specification download](https://a.travel-assets.com/documentation-hubs/prod/rapid/latest/en-US/assets/rapid-3-specs.yaml) и [official SDK schema](https://github.com/ExpediaGroup/rapid-java-sdk/blob/main/specs.yaml); перед реализацией их revision согласуется с выбранным API profile. У ETG публичный OpenAPI и narrative docs; у Dida подробные JSON/XML contracts; у Hotel Trader полные operation samples, но часть required/nullability описаний отсылает в playground. Отсутствующие сведения не заменяются придуманными полями. Supplier business endpoints, sample keys, GraphQL introspection и sandbox в ходе этой проверки не вызывались.

Дополнительная обнаруженная неоднозначность ETG: current Form docs описывают legacy Test bookings demo8473727 как связанные с financial responsibility/обязательной cancellation, тогда как general requirements говорит о реальных bookings только в Production. Новый Sandbox описан отдельно. До любых настоящих запросов надо зафиксировать точную среду и разрешить расхождение; слово Test не гарантирует безденежность. Для source-only fictional проверки это не требование запускать такие среды.

**Моя текущая рекомендация:** две интеграции всего — существующий **Duffel + новый Hotelbeds**, с полным вторым no-card test purchase flow. Это закрывает цель многопоставщицкого поиска/сопоставления/ранжирования одной новой реализацией и меньше затрагивает purchase authority. mTLS остаётся реальной обязанностью source adapter; он не скрывается fake HTTP tests. Документированный subset и protocol fixtures проверяют source, не supplier handshake/entitlement/real inventory.

Если выберем **два новых source adapters**, технически предлагается **Hotelbeds + Rapid/affiliate_collect**: оба не требуют нового обязательного form/finish polling lifecycle. Это **не** найденная пара с уже разрешённой настоящей агрегацией — у HBX test mapping restriction остаётся, у обоих account/license/settlement не подтверждены. Для цели «реально запустить агрегацию без спорных условий» сначала нужны применимые terms/access; наличие оригинального исходного кода этот вопрос не закрывает.

Отдельный вариант **Duffel + ETG/deposit** уместен, если пользователь предпочитает дополнительную сложность устойчивого asynchronous booking. Он не объявляется таким же по объёму, как Duffel+HBX. Третий active source сейчас не нужен: он добавляет инфраструктуру/fixtures/обязательства, не требуется для доказательства самой агрегации.

Поставщики ещё **не выбраны пользователем**. После выбора пары — конкретный supplier binding и полный единый proposal/design/specs/tasks; после согласования — отдельный gpt-6.1-sol и личный review основного агента. Реальные вызовы, keys/certificates, миграции, публикация и deploy не выполняются этим выбором.

### D2. Что дополнительно проверять перед выбором

Это содержательные входы для решения, а не разрешение сейчас регистрироваться/писать поставщикам/делать requests.

| Проверка | Что должно стать известно |
|---|---|
| Право доступа | Допускается ли solo/OSS/portfolio без действующей travel agency, сайта с трафиком и минимальных продаж; какая сторона заключает договор; допустимы ли её страна, host и рынки. Не делать обходов KYC/region rules. |
| Sandbox против live | Выдаётся ли отдельный ключ без депозита/карты; можно ли search, current terms, book, final status и cancellation fixtures; какие ошибки нельзя воспроизвести; есть ли обязательная реальная бронь/оплата на certification. |
| Стоимость | Setup/monthly/minimum/endpoint fees, L2B/search-to-book, zero-booking режим, quota на день/QPS/properties-per-second, renewal и billing после free trial; отдельные content/mapping services. |
| Matching и display | Разрешены ли cross-source hotel links, дедуп карточек и сравнение цен с Duffel; можно ли использовать названия/координаты/address/IDs/images в общей карточке; атрибуция и source isolation. |
| Storage | Static refresh/deletion obligations, dynamic rate TTL, сохранение quote/accepted terms для recovery, crosswalk retention, публичные fictional fixtures versus supplier data. Короткий cache тоже использование данных. |
| Реальные цены | Exact1-/2-adult whole-stay scope, net versus traveller sell price/commission, included/excluded/conditional taxes, tax feature activation, pay-at-property currency, eligibility/residency/nationality и cancellation timezone. |
| Recovery | Где начинается необратимое действие, scope/retention idempotency key, lookup по известной operation/client ref, meaning of not-found, final status, late evidence, duplicate/timeout responses и required timeout. |
| Долгосрочная поддержка | Version pin/OpenAPI consistency, deprecation/changelog, certification scope, изменение content IDs и mapping, read/receive после writer-disable; что обязательно даже для непубличного sandbox showcase. |

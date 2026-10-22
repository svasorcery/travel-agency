# Trips M2 Moscow: исследование идентичностей POI

Проверка публичных primary structured records: 2026-10-21. Финальный batch прочитан 2026-10-21T08:16:16.5024146+00:00. Scope — identity/location research для выбранного offline OSM/modelled demo v2; это не data acceptance, field survey или human approval. OSM не скачивался и walking minutes не проверялись.

## Результат и артефакты

Предложены 20 основных кандидатов и 2 резервных. Все 22 имеют один non-deprecated Earth P625 и находятся в bbox [55.72,37.57]–[55.78,37.66]. Основные 20 ограничены примерно [55.7444444,37.6025]–[55.7655556,37.6435473]: центр, без Арбата и кремлёвского внутреннего комплекса. Это проверка координат, не доказательство связности или пригодности выбранного pedestrian anchor.

Оригинальные title/tags/recommendedDwellMinutes — предложения редакции Codex. Counts для основных 20: architecture=14, culture=10, history=15, nature=4. Nature присвоен настоящим garden/park/garden-square сущностям: Александровский сад, Зарядье, Ильинский сквер, Морозовский сад; бульвар не используется для выполнения nature quota. Hours/access у всех остаются unknown; title «снаружи» означает выбранный сценарий осмотра, а не проверенную доступную точку.

Ignored outputs в TestResults/trips-m2/data-research/:

- poi-identity-inventory.json — 22 research records: exact QID/revision, P625 statement/precision, P31 statement/rank/qualifiers и versioned type labels, editorial fields. Schema исследовательская, не контракт runtime package. SHA256: dc80fe2795948038258d8f6136e04805a898714543a184d28bd6d8170d7c0741.
- wikidata-final-inventory-entities.json — полный ответ final wbgetentities lookup; SHA256: dc6e45b623867b681dc2c88793371c5b48389b8002d4e2c82f6f7857d18f8d2c. Время чтения — source retrieval, не дата наблюдения на местности.
- wikidata-semantic-types.json — первичные labels/revisions типов P31; предыдущие lookup/resolution JSON сохраняют диагностику неоднозначностей. Эти ignored outputs не являются публикуемым release bundle и не должны быть единственной долговременной evidence: build должен сохранить нужный subset/receipts по принятому дизайну.

## Основной inventory

В столбце QID@revision ссылка ведёт на точную ревизию. Координаты — latitude, longitude из P625; полный numeric value, statement ID и source precision сохранены в JSON. Semantic proof — конкретный P31 на этой ревизии, а не совпадение строки поиска. Теги и минуты dwell — только наша редакция, не factual source duration.

| Оригинальное название | QID@revision | P625: latitude, longitude | P31 semantic proof | Tags | Dwell min |
|---|---|---|---|---|---|
| Александровский сад | [Q1472498@2500134992](https://www.wikidata.org/w/index.php?title=Q1472498&oldid=2500134992) | 55.752393, 37.61385111 | Q1107656 — сад | nature, history | 40 |
| Здание Большого театра | [Q55657993@2484636752](https://www.wikidata.org/w/index.php?title=Q55657993&oldid=2484636752) | 55.76028, 37.61861 | Q24354 — здание театра | architecture, culture | 25 |
| Храм Христа Спасителя снаружи | [Q194474@2522080784](https://www.wikidata.org/w/index.php?title=Q194474&oldid=2522080784) | 55.74444444444445, 37.605555555555554 | Q56242225 — кафедральный православный собор | architecture, culture | 25 |
| Фасад ГУМа | [Q549498@2551181348](https://www.wikidata.org/w/index.php?title=Q549498&oldid=2551181348) | 55.754722222222, 37.6215 | Q2319498 — памятник архитектуры | architecture, history | 20 |
| Здание Исторического музея | [Q1140429@2531243766](https://www.wikidata.org/w/index.php?title=Q1140429&oldid=2531243766) | 55.755, 37.61805555555556 | Q24699794 — здание музея | architecture, culture, history | 20 |
| Ильинский сквер | [Q21639907@2122381315](https://www.wikidata.org/w/index.php?title=Q21639907&oldid=2122381315) | 55.75553, 37.632779 | Q2026833 — сквер | nature, history | 25 |
| Кремлёвская стена | [Q2898604@2541725668](https://www.wikidata.org/w/index.php?title=Q2898604&oldid=2541725668) | 55.74916667, 37.61666667 | Q57346 — городские стены | architecture, history | 20 |
| Лубянская площадь | [Q2416242@2553003200](https://www.wikidata.org/w/index.php?title=Q2416242&oldid=2553003200) | 55.759444444444, 37.626944444444 | Q174782 — площадь | history, architecture | 15 |
| Манеж снаружи | [Q1889165@2550107249](https://www.wikidata.org/w/index.php?title=Q1889165&oldid=2550107249) | 55.75333333333333, 37.61222222222222 | Q15206795 — выставочное помещение | architecture, culture | 20 |
| Памятник Минину и Пожарскому | [Q187531@2550237700](https://www.wikidata.org/w/index.php?title=Q187531&oldid=2550237700) | 55.752777777778, 37.6225 | Q4989906 — памятник | culture, history | 15 |
| Морозовский сад | [Q4303128@2500134327](https://www.wikidata.org/w/index.php?title=Q4303128&oldid=2500134327) | 55.755436111111, 37.643547222222 | Q22698 — парк | nature, history | 30 |
| Никольская улица | [Q4320845@2500285822](https://www.wikidata.org/w/index.php?title=Q4320845&oldid=2500285822) | 55.757261111111, 37.622683333333 | Q79007 — улица | architecture, history | 30 |
| Здание Политехнического музея | [Q1577987@2519471946](https://www.wikidata.org/w/index.php?title=Q1577987&oldid=2519471946) | 55.75774168, 37.62948612 | Q24699794 — здание музея | architecture, culture, history | 25 |
| Пушкинская площадь | [Q1110313@2503491747](https://www.wikidata.org/w/index.php?title=Q1110313&oldid=2503491747) | 55.765555555556, 37.605833333333 | Q174782 — площадь | culture, history | 20 |
| Красная площадь | [Q41116@2552196548](https://www.wikidata.org/w/index.php?title=Q41116&oldid=2552196548) | 55.754166666667, 37.62 | Q174782 — площадь | architecture, history | 40 |
| Храм Василия Блаженного снаружи | [Q129846@2550241209](https://www.wikidata.org/w/index.php?title=Q129846&oldid=2550241209) | 55.7525, 37.62305555555555 | Q2031836 — православный храм | architecture, culture, history | 25 |
| Театральная площадь | [Q1633670@2550105847](https://www.wikidata.org/w/index.php?title=Q1633670&oldid=2550105847) | 55.759333333333, 37.619777777778 | Q174782 — площадь | architecture, culture | 20 |
| Могила Неизвестного Солдата | [Q2075146@2555186838](https://www.wikidata.org/w/index.php?title=Q2075146&oldid=2555186838) | 55.754722222222, 37.616111111111 | Q1541043 — Памятники неизвестному солдату | history | 15 |
| Тверской бульвар | [Q2921955@2287087362](https://www.wikidata.org/w/index.php?title=Q2921955&oldid=2287087362) | 55.761944444444, 37.6025 | Q83620 — путь сообщения | architecture, culture, history | 35 |
| Парк Зарядье | [Q19908995@2398791113](https://www.wikidata.org/w/index.php?title=Q19908995&oldid=2398791113) | 55.751, 37.629 | Q22746 — городской парк | nature, architecture | 45 |

## Два резерва

| Оригинальное название | QID@revision | P625: latitude, longitude | Semantic proof | Tags | Dwell min |
|---|---|---|---|---|---|
| Сад Эрмитаж | [Q3889320@1854176016](https://www.wikidata.org/w/index.php?title=Q3889320&oldid=1854176016) | 55.770780555556, 37.6103 | Q1959314 — особо охраняемая природная территория России | nature, culture | 40 |
| Милютинский сад | [Q13668132@2500133000](https://www.wikidata.org/w/index.php?title=Q13668132&oldid=2500133000) | 55.756111111111, 37.646666666667 | Q22698 — парк | nature, history | 30 |

Эрмитаж — северный резерв в том же bbox, а не соседняя остановка у Кремля. Его entity смешивает театральную и территориальную классификации; [официальная страница самого сада](https://www.mosgorsad.ru/contacts/) отдельно подтверждает существование городского сада по адресу Каретный ряд. Сайт не импортируется как источник hours/access в demo. Морозовский сад имеет отдельный P31=park и coordinates; весь усадебный участок не объявляется общедоступным. Ильинский сквер имеет P31=garden square и отдельную территорию; также [городской материал](https://stroi.mos.ru/news/sobianin-otkryl-amfitieatr-i-pieshiekhodnyi-marshrut-ot-lubianki-do-zariad-ia) называет сквер частью пешеходного пространства. Минуты из той новости не используются.

## Разрешение прежних неоднозначностей

- Moscow Kremlin: exact title lookup missing означал неправильный sitelink title. Канонический enwiki title у [Q133274@2537378168](https://www.wikidata.org/w/index.php?title=Q133274&oldid=2537378168) — Kremlin; ru — Московский Кремль; P625=55.751666666666665,37.617777777777775. Это fortified complex с внутренними объектами. В основной inventory не включён: внешний осмотр нельзя привязать к его внутреннему centroid. Отдельно выбрана Q2898604, физическая стена; её anchor всё ещё требует проверки и может быть отклонён.
- Polytechnical Museum: canonical enwiki title — Polytechnic Museum, Q1577987. P31 содержит Q24699794=здание музея, поэтому выбранный наружный сценарий не подменяет музейную организацию выдуманным зданием. State Historical Museum Q1140429 тоже содержит явный P31=здание музея.
- Hermitage Garden: canonical title — Moscow Hermitage Garden, Q3889320. Не путать с Hermitage Theatre, Moscow Q19916635, театральным зданием на той же территории.
- Chistye Prudy [Q414437@2538879140](https://www.wikidata.org/w/index.php?title=Q414437&oldid=2538879140): P31=Q4167410, Wikimedia disambiguation page; P625 отсутствует. Это не координатный POI и не ошибка, которую можно исправить координатой станции. Исключён; nature coverage обеспечено садами/сквером выше.
- Bolshoi Theatre Q138908 смешивает organization/theatre company/building. Для наружного объекта выбран более точный Q55657993, самостоятельное здание Большого театра. Q138908 не дублируется в наборе.
- GUM Q549498 имеет P31=department store/public company/architectural landmark. Используется только landmark identity/location под оригинальным названием «Фасад ГУМа»; это не отдельное утверждение о входе или границах здания. Если anchor/physical-point review не подтвердится, исключить или заменить резервом, не рисовать точку вручную.
- Храм Христа Спасителя Q194474 содержит preferred P31=кафедральный православный собор с start=2000. Его normal P31=разрушенный храм ограничен qualifiers 1931–1995. Нельзя трактовать этот исторический qualifier как нынешнее закрытие/отсутствие здания.

Дополнительный raw lookup wikidata-reserve-landmarks.json проверил [Казанский собор Q1140402@2531244521](https://www.wikidata.org/w/index.php?title=Q1140402&oldid=2531244521), P625=55.755405555556,37.619252777778, P31=православный храм/памятник архитектуры, и [Спасскую башню Q2416732@2550241650](https://www.wikidata.org/w/index.php?title=Q2416732&oldid=2550241650), P625=55.7525,37.6214. Они не входят в нормализованные 22, но могут стать явной заменой после фиксации их собственного source retrieval и anchor; не добавлять сверх limits автоматически. У Спасской P625 precision столь же грубая, как у Зарядья, поэтому она не устраняет вопрос accuracy.

## Права, precision и незакрытые проверки

[Wikidata Licensing](https://www.wikidata.org/wiki/Wikidata:Licensing) разрешает structured main-namespace data под CC0; [Data access](https://www.wikidata.org/wiki/Wikidata:Data_access) описывает exact-revision доступ. В inventory переносится только structured identity/location/semantic evidence. Изображения, Wikipedia prose, external website text, часы и provider routing не копируются. Оригинальные tags/dwell/title заданы явно как software editorial proposal; humanCurator/humanReviewer=null.

У Зарядья source P625 precision=0.015961806083939 degrees: она не подтверждает физическую точность в 50m. У части square coordinates precision=null. Даже более мелкая source precision не доказывает вход, обзор фасада, безопасность или физическую доступность. Правило nearest eligible mapped node ≤50m связывает две модельные точки; оно не повышает accuracy исходной координаты. Все source precision/qualifiers сохранены без исправления.

Далее необходимы реальные offline anchor selection/profile checks, минимум18 surviving identities и60 directed derived edges, повторная coverage matrix после исключений и measured structural/Q_demo receipts. Наличие кандидата в этом отчёте не заставляет builder принять его или дополнить недостающий путь. При выпадении ниже quota — честный fail и явная замена по evidence. Ни одна оценка маршрута в минутах не получена здесь физическим измерением; реальные human signoffs/quality не заявляются.

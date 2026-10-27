# Saint Petersburg source dossier

Исследование и подготовка: 26.10.2026. Source bundle d/e содержит 20 POI и 62 направленных пути. Независимый фактологический recheck закрыл P1 по Аничковскому саду; первый зарегистрированный Q_demo прошёл 12/12 случаев. Source/local интеграция продолжается; публикация и rollout не выполнялись. Ни эти проверки, ни редакционные tags не заменяют проверку на местности или human A/B.

## Фактический принятый input и candidate d/e

Один BBBike GET: 101559015 bytes, publication `2026-10-25T04:08:52Z`, download `2026-10-26T10:24:19.735Z`, SHA256 `5cb4baf36d15f9e41ddf1356383e18c2598191b35c80deee6ec307d0765069e4`. Manifest `3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d`; payload `dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12`. Expiry `2026-11-24T04:08:52Z` не продлён.

Две сборки d/e совпали по всем семи файлам. Граф: 104395 raw coordinates, 10846 retained nodes, 3413 ways; 20 POI / 62 paths. Время 10.636/9.130 s, peak working set 79773696/80633856 bytes. Preparation profile: 204 группы / 851 уникальный excluded way, 27794 bytes, SHA256 `ebeab33d161ae0a794374c698de8b0d0e3ab57c441a28596edb2953a66e96a95`. Шесть точек не имеют выбранных входящих/исходящих связей; полной связности не заявляем. Tag coverage: architecture16/culture13/history18/nature3. Bounding box `[59.923,30.290,59.947,30.346]`.

Palace Square и Михайловский замок исключены из-за >50m anchors, радиус/координаты не менялись. Исаакиевский сквер заменён точной CC0 identity Екатерининского сада. После failed 18/52 build добавлены Певческий/Зелёный мосты. После independent P1 Аничков дворец заменён Малой Садовой, весь Аничков сад исключён из графа. Все изменения предшествовали первому Q_demo, замороженные Q inputs не менялись. [Фактологический аудит](data-audit.md), [измерения/отказы](execution.md), [maintenance runbook](../../../../tools/trips/data-maintenance.md).

Human curation/review часы не измерены: исходная оценка 10–19 human-hours остаётся оценкой, elapsed agent/tool time не выдаётся за человеческие затраты. Полевой обход, часы доступа, цены, безопасность и free access не подтверждены. Human A/B scores остаются пустыми.

| POI | Wikidata revision | Coordinate (lat, lon) | Anchor / distance | Tags |
| --- | --- | --- | --- | --- |
| Главное Адмиралтейство — наружный осмотр | [Q359185 / 2550827986](https://www.wikidata.org/w/index.php?title=Q359185&oldid=2550827986) | 59.937458333333, 30.30855 | 4419116858 / 16.312 m | architecture, history |
| Александровский сад — прогулка | [Q3162435 / 2406049804](https://www.wikidata.org/w/index.php?title=Q3162435&oldid=2406049804) | 59.936944444444, 30.309444444444 | 1303929276 / 8.381 m | history, nature |
| Александринский театр — наружный осмотр | [Q1186567 / 2517802914](https://www.wikidata.org/w/index.php?title=Q1186567&oldid=2517802914) | 59.93177501, 30.3363139 | 10747553237 / 23.234 m | architecture, culture, history |
| Аничков мост — прогулка | [Q550953 / 2472007249](https://www.wikidata.org/w/index.php?title=Q550953&oldid=2472007249) | 59.933333333333, 30.343333333333 | 3759544242 / 23.379 m | architecture, culture, history |
| Площадь Искусств — наружный ансамбль | [Q3902462 / 2502129937](https://www.wikidata.org/w/index.php?title=Q3902462&oldid=2502129937) | 59.9373, 30.3317 | 1901762040 / 2.938 m | architecture, culture |
| Банковский мост — прогулка | [Q1193149 / 2558197758](https://www.wikidata.org/w/index.php?title=Q1193149&oldid=2558197758) | 59.932161111111, 30.324855555556 | 10951537274 / 9.605 m | architecture, culture, history |
| Медный всадник — наружный осмотр | [Q672444 / 2558194382](https://www.wikidata.org/w/index.php?title=Q672444&oldid=2558194382) | 59.93655556, 30.30216944 | 4418268334 / 20.890 m | culture, history |
| Екатерининский сквер — прогулка | [Q118129094 / 2341121586](https://www.wikidata.org/w/index.php?title=Q118129094&oldid=2341121586) | 59.933187, 30.336876 | 5603203897 / 3.381 m | history, nature |
| Марсово поле — прогулка | [Q1194336 / 2483387978](https://www.wikidata.org/w/index.php?title=Q1194336&oldid=2483387978) | 59.943555555556, 30.331833333333 | 1645108606 / 2.815 m | history, nature |
| Арка Главного штаба — наружный осмотр | [Q2622043 / 2529617985](https://www.wikidata.org/w/index.php?title=Q2622043&oldid=2529617985) | 59.9381, 30.3172 | 1613010468 / 1.484 m | architecture, culture, history |
| Зелёный мост — наружный осмотр | [Q186970 / 2458403596](https://www.wikidata.org/w/index.php?title=Q186970&oldid=2458403596) | 59.936111111111, 30.319722222222 | 3759211533 / 12.502 m | architecture, history |
| Казанский собор — наружный осмотр | [Q404571 / 2550668873](https://www.wikidata.org/w/index.php?title=Q404571&oldid=2550668873) | 59.934227777778, 30.324594444444 | 11274341751 / 39.523 m | architecture, culture, history |
| Малая Садовая — прогулочная остановка | [Q4277303 / 2500281435](https://www.wikidata.org/w/index.php?title=Q4277303&oldid=2500281435) | 59.934722222222, 30.3375 | 8798305670 / 3.188 m | architecture, culture, history |
| Мариинский дворец — наружный осмотр | [Q1229016 / 2558196040](https://www.wikidata.org/w/index.php?title=Q1229016&oldid=2558196040) | 59.930833333333, 30.309444444444 | 2406025687 / 21.323 m | architecture, history |
| Певческий мост — прогулка | [Q1093454 / 2558820829](https://www.wikidata.org/w/index.php?title=Q1093454&oldid=2558820829) | 59.940011111111, 30.319363888889 | 3588556780 / 24.553 m | architecture, history |
| Исаакиевский собор — наружный осмотр | [Q215423 / 2555500450](https://www.wikidata.org/w/index.php?title=Q215423&oldid=2555500450) | 59.934047, 30.30595 | 4218224319 / 38.018 m | architecture, culture, history |
| Спас на Крови — наружный осмотр | [Q215208 / 2523518448](https://www.wikidata.org/w/index.php?title=Q215208&oldid=2523518448) | 59.94, 30.328611111111 | 1399229432 / 31.909 m | architecture, culture, history |
| Дом Зингера — наружный осмотр | [Q737377 / 2559416073](https://www.wikidata.org/w/index.php?title=Q737377&oldid=2559416073) | 59.935833333333, 30.325555555556 | 11546534608 / 21.795 m | architecture, culture |
| Строгановский дворец — наружный осмотр | [Q1461235 / 2558195696](https://www.wikidata.org/w/index.php?title=Q1461235&oldid=2558195696) | 59.93573611, 30.32067222 | 3165417109 / 19.258 m | architecture, culture, history |
| Юсуповский дворец — наружный осмотр | [Q2991838 / 2558196281](https://www.wikidata.org/w/index.php?title=Q2991838&oldid=2558196281) | 59.9295, 30.2987 | 4059279624 / 11.916 m | architecture, culture, history |

## Историческое planning-досье (не current inventory)

Ниже сохранены исходные гипотезы и ограничения до подготовки данных. Pending в этой части описывает тот момент; текущие факты приведены выше.

## Источники, права и ограничения

| Источник | Что проверено / допустимая роль | Ограничение |
| --- | --- | --- |
| [BBBike SPb downloads](https://download.bbbike.org/osm/bbbike/SanktPetersburg/) | Есть OSM XML gzip и boundary/checksum; совместимый вход текущего helper | Публичная ссылка mutable; размер страницы округлён, hash ещё не получен |
| [BBBike help/terms](https://extract.bbbike.org/extract.html) | Сервис описывает free end-user use, fair usage и отсутствие гарантий; коммерческое обслуживание выделено отдельно | Лицензия OSM и условия сервиса — разные вопросы. Здесь планируется единичная подготовка default-off demo; масштабное/коммерческое использование не предполагается и требует отдельного разбора условий, без перехода на платный план |
| [OSM copyright](https://www.openstreetmap.org/copyright), [ODbL](https://opendatacommons.org/licenses/odbl/1-0/) | Данные OSM под ODbL; credit/лицензионное уведомление и offer производной базы — часть release | Не использовать чужие map tiles, Google/Yandex маршруты, описания или API. Не публиковать private plans как часть data offer |
| [Wikidata licensing](https://www.wikidata.org/wiki/Wikidata:Licensing) | Structured identity/coordinates — CC0; exact entity revision и выбранные statements надо сохранить | Правила не распространяются автоматически на фото, другие namespace texts, сайты по ссылкам и произвольные imported datasets |
| [Ансамбль центральных площадей](https://visit-petersburg.ru/route/tp-48-ansambl-tsentralnykh-ploshchadey/) | Обоснование группы Дворцовая/Адмиралтейство/Исаакий, Александровского сада и сквера | Только независимая проверка названий/смысла района; текст/фото/готовую геометрию и длительность не переносить |
| [Центральный маршрут](https://visit-petersburg.ru/route/marshrut-tsentralnyy-progulki-po-vechernemu-peterburgu/) | Связывает Невский, площадь Искусств, замок, Марсово поле и район Спаса | Это не текущий path/access feed и не измерение нашей модели |
| [Михайловский сад, оператор](https://rusmuseum.ru/museums/mikhailovsky-garden/) | На странице есть часы, выходной вторник и ограничение времени входа | Сад исключён как POI/транзит. Просмотр страницы не превращает часы в разрешённые verified facts |
| [Сезонный режим, оператор](https://app.rusmuseum.ru/mikhailovsky-garden/news/?layoutMode=classic) | Страница отдельно сообщает сезонные режимы и закрытие на просушку | Датированная публикация не доказывает доступность будущей даты; не переносить её как live schedule |

Рекомендуемый набор: Wikidata CC0 + один OSM extract + оригинальная редакционная подборка. Туристический портал и сайты операторов служат проверке фактов/исключений; открытая лицензия на их содержимое здесь не установлена. Их тексты/фото не входят в package. Это технический rights register, не юридическое заключение и не разрешение на rollout.

## Живые метаданные входа

26.10.2026 выполнен один успешный анонимный HEAD без redirects по [SanktPetersburg.osm.gz](https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz):

- HTTP 200; Last-Modified: `Sun, 25 Oct 2026 04:08:52 GMT`.
- Content-Length: `101559015` bytes (около 96.85 MiB); Content-Type: `application/x-openstreetmap+xml`.
- ETag: `"381984294"`; он не считается криптографическим hash.
- GET, SHA256, expanded size, bbox graph counts, anchors и runtime measurements отсутствуют.
- Если будущий GET подтвердит эту же публикацию, map allowance заканчивается не позднее `2026-11-24T04:08:52Z`; действительный expiry также ограничивается identity evidence. Это условная граница, не установленный срок ещё не созданного release.

Первый запрос из sandbox был отклонён сетевой средой, после разрешённого чтения HEAD выполнен успешно. Повторных скачиваний/платных запросов не было.

Publication — возраст выгрузки, не дата проверки каждой улицы. BBBike предупреждает об отсутствии contributor/version/timestamp metadata в обычных extracts; поэтому raw SHA и node/way/path IDs нужны для воспроизводимости, но не доказывают свежий осмотр объекта. При apply нужен новый HEAD/GET receipt; текущая наблюдённая публикация не закрепляется как будущая.

## Предложенный район и inventory

BBox подготовки: south/west/north/east = `59.923/30.290/59.947/30.346`. Он не является результатом измеренного graph coverage. Все позиции ниже — кандидаты, не released POIs. Названия относятся к наружному виду или прогулочному пространству; вход внутрь не предполагается. Теги — авторские предварительные рекомендации: A architecture, C culture, H history, N nature. Dwell предложен в пределах 15–30 минут и не измерен.

| # | Предлагаемая точка / смысл | Теги | Проверочная опора / ещё нужно |
| --- | --- | --- | --- |
| 1 | Дворцовая площадь, ансамбль | A,H | [Wikidata Q1132624](https://www.wikidata.org/wiki/Q1132624); видны несколько coordinate statements — выбрать один обоснованно, закрепить revision |
| 2 | Главный штаб, наружный вид арки | A,C,H | [Wikidata Q2622043](https://www.wikidata.org/wiki/Q2622043); проверить <=50m публичный anchor без прохода во двор |
| 3 | Главное Адмиралтейство, наружный вид | A,H | Центральные площади; Wikidata revision/anchor pending |
| 4 | Александровский сад, прогулочная часть | N,H | Центральные площади; конкретный identity/anchor/access/path audit pending |
| 5 | Медный всадник | C,H | Центральные площади; не добавлять отдельный дублирующий stop «Сенатская площадь» |
| 6 | Исаакиевский собор, наружный вид | A,C,H | [Wikidata Q215423](https://www.wikidata.org/wiki/Q215423); колоннада/интерьер исключены, anchor pending |
| 7 | Исаакиевский сквер, прогулочная часть | N | Центральные площади; не подменять identity сквера [Q2352588 площади](https://www.wikidata.org/wiki/Q2352588), точная CC0 entity ещё нужна |
| 8 | Мариинский дворец, вид с площади | A,H | [Городской портал](https://visit-petersburg.ru/leisure/places/tp-194711-mariinskiy-dvorets/); вход/экскурсия исключены |
| 9 | Юсуповский дворец, фасад с Мойки | A,C,H | [Городской маршрут](https://visit-petersburg.ru/en/route/ocharovanie-kolomny/); двор/интерьер исключены, identity pending |
| 10 | Казанский собор, наружный ансамбль | A,C,H | [Описание Банковского моста](https://visit-petersburg.ru/leisure/places/tp-194695-bankovskiy-most/) подтверждает объект в районе; entity/anchor pending |
| 11 | Строгановский дворец, фасад | A,C,H | Предварительный редакционный кандидат; точная identity/anchor проверка обязательна |
| 12 | Дом Зингера, фасад | A,C | Описание Банковского моста; никаких food/shopping claims |
| 13 | Банковский мост, скульптуры/наружный осмотр | A,C,H | [Городской портал](https://visit-petersburg.ru/leisure/places/tp-194695-bankovskiy-most/); нынешнее состояние/проходимость не подтверждены |
| 14 | Александринский театр, фасад | A,C,H | Предварительный редакционный кандидат; identity/anchor pending |
| 15 | Аничков дворец, фасад с Невского | A,H | [Городской портал](https://visit-petersburg.ru/leisure/dostoprimechatelnosti/tp-194694-anichkov-dvorets/); сад/двор не использовать |
| 16 | Аничков мост, скульптуры | A,C,H | [Городской портал](https://visit-petersburg.ru/leisure/places/anichkov-most/); восточный край выбранного среза |
| 17 | Площадь Искусств, ансамбль | A,C | Центральный маршрут; не считать отдельно соседний Михайловский дворец ещё одним stop |
| 18 | Спас на Крови, наружный вид | A,C,H | Центральный маршрут/описание Банковского моста; интерьер исключён |
| 19 | Михайловский замок, вид с улицы | A,H | Центральный маршрут; дворы/музейный сад исключены |
| 20 | Марсово поле, прогулочное пространство | N,H | Центральный маршрут; мемориал не позиционировать как развлекательную площадку |

Для всех 20 отсутствуют package-ready coordinates, revision hashes, OSM anchors и measured edges. Три N-точки дают потенциальный минимальный набор, но он ещё не доказан; это первый data gate. Как минимум три разных A/C/H уже предполагаются редакционной выборкой, но числовая полнота проверяется после admissible inventory. Для повторно используемых primary pages таблица содержит лишь названия/расположение, без копирования авторских описаний.

Одна Wikidata coordinate в середине крупного здания или площади может не дать anchor в пределах 50m. Нельзя сдвигать её, выдавая собственную позицию за CC0 исходник. При невозможности получить корректную привязку точка исключается/заменяется; если 18 подходящих объектов/три nature не набираются, пакет не принимается и план пересматривается. Координаты и IDs не выдуманы ради заполнения таблицы.

## Предметная проверка переходов

Это гипотезы соседства для проверки на snapshot, не уже доказанные edges и не расписание на три дня:

| Группа | Конкретная последовательность проверки | Что проверить |
| --- | --- | --- |
| Запад | Дворцовая → Главный штаб → Адмиралтейство → Александровский сад → Медный всадник → Исаакий → Исаакиевский сквер → Мариинский дворец → Юсуповский | mapped pedestrian crossings, допустимые направления, отсутствие закрытых дворов |
| Невский/канал | Главный штаб → Строгановский → Казанский → Зингер; Казанский → Банковский; Зингер → Александринский → Аничков дворец → Аничков мост | переходы через Невский, канал Грибоедова, Фонтанку; отдельная валидация каждого направления |
| Север центра | Зингер → Спас → Марсово поле → Михайловский замок → площадь Искусств → Невский | пути по улицам, не через Михайловский/Летний сады; корректный 1-й Садовый мост, без Невы |

Builder по общим правилам выбирает до пяти ближайших достижимых исходящих связей на точку: при 20 POI максимум 100, допускается минимум 60 после строгого отбора. Эти цепочки не подменяют nearest-five selector и не требуют искусственно включить каждое желаемое ребро. Зафиксировать actual components/in-out degree, excluded reasons и path proof. Если нужное соседство не выбрано или разорвано, это остаётся фактом покрытия. Новые geometry/way IDs берутся только из сохранённого input.

Не принимать короткий path, который проходит через музейный сад/двор или выходит на Неву. SPb profile содержит hashed way exclusions и проверяется по каждой selected path. Нет runtime router, прямолинейных repairs и inferred opening schedule. Approximate anchor сам по себе не доказывает безопасность конечного подхода. Сезон, снег, ветер, события, временные ограждения, лестницы и доступность для маломобильных людей не калиброваны.

## Воспроизводимость, обновление и бюджет

До release сохранить raw gzip вне Git; source URL, same-response publication/download time/length/SHA256; exact Wikidata revisions и retrieval; inventory; exclusion profile; tool hash; fixed build instant. SPb offer включает payload, manifest, input-manifest, inventory, walking-evidence, attribution и preparation-profile (семь файлов). Raw source не публикуется автоматически. Для разрешённого public release проверить достаточность ODbL offer/reproduction inputs отдельно от private plans и условий download service.

Два build должны совпасть по каждому offered byte, пройти strict package-check, current-time expiry check и caps. Сроки 90d identity/30d map/7d verified access не возобновляются чтением. В этом выпуске verified access отсутствует. Последующие refresh сравниваются только с SPb baseline. Moscow v1 payload SHA256 `631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08`, manifest SHA256 `8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43` и expiry `2026-11-17T03:55:13Z` не меняются.

Плановая оценка, не измеренный результат и не обещание срока:

| Работа с данными | Первый пакет | Повторное обслуживание |
| --- | --- | --- |
| Проверка 20 identities/revisions/anchors и прав | 4–7 часов | 1–2 часа |
| Paths, exclusions, связность и фактологический аудит | 4–8 часов | 2–4 часа при изменении графа |
| Две сборки, receipts, QA/разбор отказов | 2–4 часа | 1–2 часа |
| Итого ручной труд, без программирования и human field review | 10–19 часов | 4–8 часов на цикл |

API-покупки: 0. Нагрузка: одна выгрузка порядка 97 MiB и два offline build; временный expanded footprint ограничен 2 GiB входа плюс bounded graph/outputs. Время и память не экстраполировать с Москвы. Если первый bounded build не проходит лимиты, остановиться и предложить более узкий разрешённый input или пересмотр среза, не скачивать альтернативы/менять parser автоматически.

Программная адаптация и тесты оцениваются отдельно после review плана; главный измеряемый риск здесь — ручные anchors/path exclusions. Human A/B и выездная калибровка не включены в часы: исполнители/время не согласованы. При apply вести реальные curation/recheck часы, причины отказов, доли filled/partial/empty/unknown без owner/brief labels; это основание решать о следующем городе, не основание автоматически его добавлять.

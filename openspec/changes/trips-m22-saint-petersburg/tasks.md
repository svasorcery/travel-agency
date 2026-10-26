# Tasks

26.10.2026 пользователь разрешил реализацию после саморевью. Разрешены bounded source/local работы и изолированные fictional проверки плана; публикация, общий target/rollout, Moscow v2 и изменение существующего demo остаются отдельно. Planning evidence находится в verification.md; новые результаты — в execution.md.

## 1. Совместимость до адаптации

- [x] 1.1 Зафиксировать source baseline и московские bytes/manifest/expiry/URL, retained v1 HTTP corpus и fixtures старого M1 catalogue-v1/codec для Moscow и SPb; проверить SHA256, plaintext/detail/TS round trips и owner/revision guards до изменений. Отдельно сохранить доказательство, что fixture произведён старым кодеком, без доступа к пользовательскому demo.
- [x] 1.2 Заморозить 12 SPb real-package case inputs/ожидаемые outcomes/coverage/пороги и 12 structural/negative inputs до просмотра результатов генератора; проверить охват 1/2/3 дней, pace, interest mixes/nature/food/none и date boundaries по design, не выставляя human scores.

## 2. Общая подготовка и строгая загрузка

- [x] 2.1 Параметризовать существующий tools/trips/build-moscow.cs двумя закрытыми city profiles, сохранив Moscow defaults/output format; проверить существующий build-moscow.test.mjs и новые synthetic SPb bbox/source/anchor/duplicate/direction/resource cases. Документировать аргумент destination и сохраняемый legacy запуск.
- [x] 2.2 Добавить bounded SPb preparation-profile с excluded ways, его deterministic hash и проверяемое исключение путей до nearest-five; проверить сад/двор/Neva negatives, перестановку входов, exact path proof и неизменность Moscow profile/model/selector/output.
- [x] 2.3 Расширить закрытый Core profile allowlist и связь profile/destination с прямыми ItineraryFacts/Validator tests; обобщить CuratedMoscowFacts/Release в один строгий reader с двумя trusted descriptors и city-specific file sets, без directory discovery; обновить read-only PackageVerification/package-check. Проверить existing CuratedMoscowFactsTests и новые mixed-city/hash/bbox/profile/file/malformed/expiry/withdrawal tests через фактический loader.
- [x] 2.4 Сохранить совместимость всех шести Moscow v1 файлов при реальной репродукции retained raw до исходного expiry; если срок уже прошёл, честно записать отказ и выполнить offline synthetic/golden format parity без clock bypass. Проверить неизменность runtime pin, public assets и всех tracked Moscow bytes; обновить инструкцию reproduction с этой границей.

## 3. City-scoped maintenance и первый пакет

- [x] 3.1 Расширить moscow-refresh.mjs без копии acquisition/build logic: destination, check/not-installed, prepare-initial, subsequent prepare, reproduce-current; сохранить старые Moscow команды. Проверить moscow-refresh.test.mjs на bootstrap/repeat-bootstrap/wrong-city/unchanged/newer-same-bytes/HEAD-GET disagreement/redirect/caps/unsafe output и expiry-during-build, без live network в CI.
- [x] 3.2 Подготовить точные CC0 entity revisions/coordinates/anchors для предложенных 20 POI и обоснованный excluded-way profile по одному permitted SPb snapshot; сохранить same-download publication/length/SHA256 и права. Проверить 18–20 eligible POI/три на тег/<=50m anchors, отсутствие садов/дворов/Neva и выдуманных coordinate shifts; при провале data gate остановиться с dossier delta.
- [x] 3.3 Выполнить два реальных bounded build с одинаковыми input/profile/inventory/helper/as-of, сравнить все семь SPb offered files и strict package-check, а также expiry на фактическом завершении; сохранить sanitized resource/graph/component/degree/path/hash receipt. Принимать только 60–100 реальных направленных связей, без fabricated repairs; результат candidate-needs-review.
- [x] 3.4 Провести независимый фактологический аудит каждой identity/anchor/path/exclusion и близких POI на дублирование; исправить findings до freeze release identity и не подменять им human review. Обновить source-dossier фактическими hash/counts/curation hours, rights register и maintenance runbook.
- [x] 3.5 После D_demo проверки добавить SPb v1 source bundle, explicit trusted manifest pin и соответствующий public data offer в source change; проверить parity embedded/public files, immutable identity и 2KiB/256KiB limits. Это source data acceptance, не publication или feature activation; Moscow v2 не создавать.

## 4. Application и HTTP протокол

- [x] 4.1 Передать authoritative DestinationSelection в IItineraryFactSource, добавить выбор одного из двух trusted releases в Composition и изолированный withdrawal; сохранить общий admission/deadline. Проверить ItineraryCandidateServiceTests/CompositionTests: foreign/unknown/stale/unsupported zero-source behavior, mixed-city rejection, один общий gate и независимость отказов/M1 readiness.
- [x] 4.2 Реализовать explicit header v2 на прежних routes в guards/endpoints/encoder, сохранив весь no-header v1; проверить TripsItineraryHttpTests и ResponseCodecTests на v1 golden, v2 два города, Kaliningrad unsupported, duplicate/unknown header/query, exact empty body/If-Match, status mapping, disabled-before-auth, no-store и availability без plan storage.
- [x] 4.3 Связать strict city/protocol/profile/offer tuple при encoding, final revision/expiry и полном UTF-8 бюджете, без новой persistence; проверить ItineraryCandidateValidatorTests, реальные serialized response/ordinary M1 reserve и hostile mixed facts. Обновить module/API документацию и сверить actual Host OpenAPI с объявленными header/query/outcomes после разрешённого local test.

## 5. Client и явное принятие

- [x] 5.1 В shared/ts/api-client добавить раздельные v1/v2 types/decoder ветви и явные v2 transport methods; сохранить старые fixtures. Проверить trips-itinerary.decoder.spec.ts/real-demo.spec.ts и API service tests: valid обеих city, unknown fields/duplicates, wrong dataset/hash/source/profile/offer/zone/catalogue, malformed time/revision и отсутствие transport cache.
- [x] 5.2 В trips-itinerary-state/page использовать v2 availability сохранённого destination и guard protocol/city/session/epoch при запросе и adoption; сохранить отдельный Save и unknown explanations. Проверить state/component tests и Trips browser cases на Moscow/SPb/Kaliningrad, A→B→A, edits/logout/navigation, stale availability/CAS, keyboard/focus и zero-write до Save.
- [x] 5.3 Обновить городские подписи/ссылки/exclusions и пользовательскую документацию без implementation jargon; проверить что unsupported, unavailable, empty, partial и unknown не смешаны и не обещают free/open/safe/within-budget.
- [x] 5.4 Сохранить Rail consumer без расширения station inference/real retention и standalone поведения; выполнить trips-rail-projection/state tests и Trips/standalone Rail browser regression, подтвердить fictional-only append и обычный M1 CAS.

## 6. Исторические планы и real-service E2E

- [x] 6.1 Расширить TripsCodecProtectionTests/TripsPackageLifecycleTests/TripsHostTests: old-codec Moscow/SPb snapshots, new SPb intentions, package swap/expiry/corruption/withdrawal и Host restart; проверить exact full detail/ciphertext/owner/revision/timestamps/key equality и успешную явную manual edit после отказа generation.
- [x] 6.2 Добавить именованные Moscow/SPb test profiles в local-stack/E2eHostFactory/runner с валидируемым fixed time, не меняя interactive real time и старый Moscow instant. Проверить lifecycle/unit/synthetic Chromium tests: mismatched profile/clock refusal, wall-clock JWT, original one-POST response capture, no production clock hook, foreign-resource refusal и cleanup.
- [x] 6.3 Выполнить полный прежний Moscow E2E с сохранением всех assertions и SPb E2E с nine-stop positive case; в SPb run сохранить historical Moscow M1 plan через real endpoints. Проверить обоих после полного Host/Keycloak/PostgreSQL stop/restart, свежего PKCE и exact full detail/row/key comparison; проверить foreign owner и stale writes для обоих, original responses и отсутствие private/browser external leakage.
- [x] 6.4 Подключить оба профиля последовательно к существующему ordinary CI/full-stack aggregate и обновить tools/trips/README.md; структурными CI tests доказать, что один skipped/failed профиль либо cleanup не дают общий pass. Сохранить существующие timeouts/retries, default-off и санитизацию evidence; не использовать остановленный user demo.

## 7. Механическая оценка и source acceptance

- [x] 7.1 Привязать frozen SPb corpus из 1.2 к exact dataset/profile/policy/decoder/helper hashes и clocks до первого исполнения; выполнить один зарегистрированный Q_demo run и отдельно structural/negative suite, проверить все заранее объявленные thresholds и raw-body decoders. Сохранить failures/partial/empty/unknown с полным denominator, без выдачи agent scores за human A/B.
- [x] 7.2 Провести независимый source/spec/privacy/data review готовой реализации и scoped recheck исправлений; проверить отсутствие открытых actionable findings, отдельные D_demo/S/Q_demo/factual receipts, сохранённый Moscow corpus, docs и strict OpenSpec validation.
- [x] 7.3 Выполнить релевантные финальные проверки: Trips Unit/Integration, Host lifecycle/HTTP, architecture, TS/browser и оба full-stack profiles; formatter/harness checks по затронутым файлам. Сохранить exact source/run identities и ограничения, не приписывая local pass обычному CI, target или rollout.

## 8. Отдельные последующие решения

- [ ] 8.1 После отдельного разрешения на publication записать финальные PR и post-merge ordinary CI receipts с выполненными обоими city profiles; проверить SHA/ancestry и итоговый aggregate, не выводить merge permission из green CI.
  Publication update: the user explicitly authorized commit, push, PR and CI repair on2026-10-26, with merge forbidden. The PR/CI portion is in progress; this task remains open for the separately authorized future merge/post-merge receipt.
- [x] 8.2 При documentary finalization явно оставить human A/B с пустыми scores, field calibration, target provisioning и M2 rollout task6.3 отдельными pending gates; проверить, что source acceptance не меняет flags, v1 expiry, demo archives или human results. Реальное прохождение этих gates не входит в данный source change.

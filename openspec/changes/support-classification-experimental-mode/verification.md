# Проверка экспериментального режима — 2026-10-15

## Что согласовано и сделано

Пользователь разрешил оставить Laya и восемь категорий, добавить пометку «экспериментальная», обновить требования и проверить в тестовом приложении. Применение — только отдельным ручным подтверждением; лимит запроса — 10 секунд.

Ветка codex/support-laya-evaluation, исходный HEAD 68ff0e186dc5e2d33eed5acfc93b366f5ce835a6. На момент локальной проверки ветка и HEAD не изменялись; изменения ещё не были staged/committed/pushed, CI и deployment не запускались.

- В существующем операторском интерфейсе постоянно видны «Экспериментальная функция» и предупреждение проверить категорию перед применением. Предупреждение связано с кнопкой через accessible description; клиент его не видит.
- Сохранены Laya, production-вопрос support-category-v1, восемь категорий и guards 0.75/0.60. Unclassified остаётся ручным fallback. A/B-вопросы и фильтр ясности в продукт не переносились.
- Запрос и подстановка не меняют сохранённую категорию, приоритет, revision или triageSource. Только отдельная кнопка сохранения создаёт решение оператора.
- Текущий capability contract синхронизирован с delta: одна новая маркировка/таксономия и три уточнения об экспериментальном допуске, 10 секундах и отдельной оценке качества. Эксплуатационная и demo-документация обновлены.
- Предыдущая синтетическая оценка осталась отрицательной; разрешение экспериментального режима не объявляется успешным прохождением проверки качества.

## Результаты локальной проверки

| Проверка | Результат |
| --- | --- |
| Полный Support browser workflow в Chromium | 19/19 passed |
| Angular Support page/state/API tests | 49/49 passed, окончательный шаблон пересобран |
| Support unit tests | 98/98 passed |
| Node fixture API tests | 13/13 passed |
| Strict OpenSpec change validation | PASS |
| Все текущие capability specs | 7/7 PASS |
| Biome CI на изменённых HTML/TS | exit 0, 0 ошибок; 3 style warnings и 21 informational suggestion в browser-файле |
| CSharpier на изменённом C#-тесте | PASS |
| git diff --check | PASS |
| Независимое статическое AI-ревью ограниченного diff | P1/P2 замечаний нет |

Браузерная проверка до правки UI обнаружила отсутствующее предупреждение; после правки прошли все 19 сценариев. Успешный candidate-сценарий проверяет все восемь категорий плюс Unclassified, предупреждение до/после запроса, сохранённые значения после запроса и после подстановки, затем одно подтверждённое ручное сохранение с источником Operator и отсутствие автоматического запроса после reload.

Unit-проверка использует виртуальное время: ответ на 9999 мс ещё допустим, на 10000 и 11000 мс возвращается Uncertain/manual без повтора и сохранения. Production deadline не менялся.

Первый browser-запуск не дошёл до тестов из-за 120-секундного ожидания запуска стенда. Отдельная подготовка карты проекта Nx завершилась успешно, после чего стенд и сценарии запустились с прежними лимитами; webServer timeout не повышался. Отдельный рабочий каталог использует junction на уже установленные зависимости основного checkout: package-lock SHA256 15d1354c5179b465a42d5579de2ef1302531ffbf08bcdd1558c92eabd3b1c5c1 совпал, версии Angular/Playwright/Nx/TypeScript/Biome сверены. Новых npm-пакетов и браузеров не скачивали.

## Команды

Из корня рабочего каталога, с NX_DAEMON=false и NX_NO_CLOUD=true для Nx/Playwright:

- node node_modules/@playwright/test/cli.js test -c tests/travel-e2e/playwright.flights-demo.config.ts support-workflow.spec.ts --reporter=list
- node node_modules/nx/dist/bin/nx.js test web --watch=false --include='**/support-page.component.spec.ts' --include='**/support-state.service.spec.ts' --include='**/support-api.service.spec.ts'
- dotnet test tests/support/Travel.Modules.Support.Tests.Unit/Travel.Modules.Support.Tests.Unit.csproj --no-restore --maxcpucount:1 --logger 'trx;LogFileName=experimental-support-unit.trx' --results-directory artifacts/support-experimental
- node --test tools/demo/support-cases.test.mjs
- node node_modules/@biomejs/biome/bin/biome ci apps/web/src/app/support/support-page.component.html tests/travel-e2e/demo/support-workflow.spec.ts
- dotnet csharpier check tests/support/Travel.Modules.Support.Tests.Unit/SupportClassificationServiceTests.cs
- node tools/openspec/run.mjs validate support-classification-experimental-mode --strict
- node tools/openspec/run.mjs validate --specs --strict
- git diff --check

## Границы доказательств

Тестовое приложение использовало вымышленные обращения и ответы модели, перехваченные browser fixture. Это проверка интерфейса и ручного подтверждения, не новая проверка точности Laya. Реальная модель, платные API, Host/DB и target-среды не запускались; holdout не открывался. Feature default-off сохранён, реальные обращения не включены. Тестовые web/API серверы после проверки остановлены. Будущий PR/CI и включение в целевой среде — следующие отдельные шаги.

## Исправление первого PR CI — 2026-10-15

[PR51](https://github.com/svasorcery/travel-agency/pull/51) создан по явному разрешению пользователя. Первый [CI38736240752](https://github.com/svasorcery/travel-agency/actions/runs/38736240752) на 50a407ad67a08aa7c74c49ba840cb23a6ffba069 прошёл lint и frontend-affected, но остановился на restore: NU1902/NU1903 для PKCS10.0.0. Остальные .NET-задачи были пропущены по зависимости, не приняты как пройденные.

Локальный restore Flights Infrastructure воспроизвёл обе ошибки. dotnet nuget why подтвердил MailKit4.16.0 -> MimeKit4.16.0 -> PKCS10.0.0. Официальные [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) и [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) указывают исправленную версию10.0.13. Проверенные NuGet nuspec даже для MailKit/MimeKit4.18.1 сохраняют допустимый нижний предел10.0.0.

Узкая правка: центральная версия PKCS10.0.13 и явная ссылка рядом с существующим MailKit в Flights Infrastructure. Версии MailKit/MimeKit, NuGet Audit, warnings-as-errors, CI workflow и Laya configuration не менялись; исключений для уязвимостей нет.

Проверки исправления:

- dotnet restore Travel.slnx --no-cache: PASS с включённым аудитом.
- Все7 project.assets.json с PKCS выбирают ровно10.0.13.
- dotnet build Travel.slnx --configuration Release --no-restore --maxcpucount:1: PASS,0ошибок;27локальных предупреждений AD0001 от ASP.NET RouteHandlerAnalyzer в Host integration test project, без подавления.
- Release Flights unit tests:1481/1481 PASS.
- Release architecture tests:183/183 PASS.
- CSharpier для изменённых props/csproj и git diff --check: PASS.
- Независимое статическое AI-ревью узкой правки: P1/P2 нет.

Новый коммит требует отдельного полного PR CI. Его окончательный URL, head SHA и результат записываются в описании PR после завершения; эти локальные проверки не объявляются успешным удалённым CI заранее. На реальных обращениях Laya остаётся выключенной.

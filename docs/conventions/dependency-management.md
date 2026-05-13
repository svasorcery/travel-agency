# Convention: Dependency Management

## Что это

Renovate — бесплатное hosted-приложение GitHub, которое отслеживает все манифесты
зависимостей в репо и автоматически открывает PR при выходе новых версий.
Покрывает NuGet, npm, Docker-образы и GitHub Actions.

## Почему

**Renovate вместо Dependabot** — единый конфиг (`renovate.json`) для всего
полиглот-стека: NuGet, npm, docker-теги и Actions одновременно. Поддерживает
семантические префиксы коммитов (`chore(deps):`), группировку по экосистеме,
батчинг минорных обновлений и rate-limiting. Dependabot требует отдельных
`dependabot.yml`-секций на каждую экосистему и не умеет группировать cross-ecosystem.

## Конфиг

`renovate.json` в корне репо — единственная точка настройки.

```json
{
  "$schema": "https://docs.renovatebot.com/renovate-schema.json",
  "extends": ["config:recommended"],
  "commitMessagePrefix": "chore(deps):",
  "schedule": ["on the first day of the month"],
  "packageRules": [
    { "matchManagers": ["nuget"], "groupName": "NuGet packages" },
    { "matchManagers": ["npm"],   "groupName": "npm packages" },
    { "matchUpdateTypes": ["major"], "addLabels": ["breaking"] }
  ]
}
```

## Активация

Renovate **настроен, но не активирован** как GitHub App на организации.

Чтобы активировать:
1. Перейти на https://github.com/apps/renovate
2. Установить на `svasorcery/travel-agency`

До этого `renovate.json` присутствует в репо, но никаких PR открываться не будет.

## Что отслеживается

| Манифест | Экосистема |
|----------|-----------|
| `Directory.Packages.props` | NuGet (Central Package Management) |
| `package.json` + `package-lock.json` | npm |
| `apps/Travel.AppHost/Program.cs` | Docker-образы через Aspire `.WithImage(name, tag)` — Renovate может не подхватить автоматически; пересмотреть в Subproject 1 |
| `.github/workflows/*.yml` | GitHub Actions (e.g. `actions/setup-node@v4`) |

## Стратегия обновлений

- Расписание: раз в месяц (первый день)
- Минорные / патч: группируются по экосистеме в один PR, мёрджатся автоматически
  при зелёных проверках
- Мажорные: отдельный PR с меткой `breaking`, мёрдж вручную

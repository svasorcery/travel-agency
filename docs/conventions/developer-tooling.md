# Convention: Developer Tooling

## Что это

Git-хуки, управляемые Lefthook, запускают форматтеры и линтеры перед коммитом и пушем.
Commitlint проверяет текст сообщения коммита на соответствие Conventional Commits.
Commitizen предоставляет интерактивный wizard (`npm run commit`) для авторов,
которые предпочитают не держать формат в голове.

## Почему

**Lefthook вместо Husky** — Go-бинарь без зависимости от Node.js; работает в любом
окружении (CI, Windows, Dev Container) без `npm install`. Конфиг хранится в одном
`lefthook.yml`, а не разбросан по `package.json` и shell-скриптам.

**Conventional Commits** дают машиночитаемую историю: `feat:`, `fix:`, `chore:` и т.д.
Это позволяет автоматически генерировать CHANGELOG и правильно инкрементировать версии
(`standard-version` / `release-please`) в будущих подпроектах.

**Commitizen** (`npm run commit`) — дружелюбная альтернатива ручному набору: wizard
спрашивает тип, scope, описание и генерирует готовое сообщение, удовлетворяющее
commitlint.

## Конфиги

| Файл | Назначение |
|------|-----------|
| `lefthook.yml` | корень репо — определяет все хуки |
| `commitlint.config.mjs` | корень репо — правила для commit-msg |
| `package.json` → `config.commitizen` | путь к адаптеру `cz-conventional-changelog` |

## Реальные хуки

### pre-commit

```yaml
pre-commit:
  parallel: true
  commands:
    csharpier:
      glob: "**/*.cs"
      run: dotnet csharpier {staged_files}
    biome:
      glob: "**/*.{ts,json}"
      exclude: "**/package-lock.json"
      run: npx biome check --write {staged_files}
```

### commit-msg

```yaml
commit-msg:
  commands:
    commitlint:
      run: npx commitlint --edit {1}
```

### pre-push

```yaml
pre-push:
  commands:
    arch-tests:
      glob: "src/Modules/**/*.cs"
      run: dotnet test --filter Category=Architecture
```

## Полезные команды

```bash
npm run commit                    # commitizen wizard (рекомендуется)
npx lefthook run pre-commit       # запустить хук вручную
npx lefthook run pre-push         # запустить arch-тесты вручную
git commit --no-verify            # ⛔ НЕ ДЕЛАТЬ — обходит все хуки
```

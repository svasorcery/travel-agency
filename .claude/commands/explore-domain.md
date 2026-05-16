When this command is invoked, explore the current state of a domain module and produce a summary card.

Steps:
1. Identify the module: use argument if provided, otherwise ask "Which module? (flights / hotels / rail / trips / identity)"
2. Read: modules/{name}/CLAUDE.md
3. Scan and summarize:
   - Aggregates in modules/{name}/Travel.Modules.{Name}.Core/ — list with state machines
   - Value Objects in modules/{name}/Travel.Modules.{Name}.Core/
   - Domain Events in modules/{name}/Travel.Modules.{Name}.Core/
   - Wolverine Handlers in modules/{name}/Travel.Modules.{Name}.Application/
   - Provider interfaces in modules/{name}/Travel.Modules.{Name}.Core/Providers/
4. Check test coverage:
   - Unit tests in tests/{name}/...Tests.Unit/ — which handlers/aggregates are covered?
   - Integration tests in tests/{name}/...Tests.Integration/
5. Output:

---
## {ModuleName} — Current State

### Aggregates
[list with states]

### Value Objects
[list]

### Domain Events
[list]

### Handlers
| Handler | Type | Tests |
|---------|------|-------|
| FooHandler | Command | ✅ |
| BarHandler | Query   | ❌ |

### Providers
[list]

### TODO
[handlers without tests, stubs not yet implemented, missing value objects]
---

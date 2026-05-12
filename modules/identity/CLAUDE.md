# Identity module

**Status:** Foundation-implemented (OIDC через Keycloak)

## Bounded context
Аутентификация и авторизация пользователей. OIDC через Keycloak, JWT bearer tokens.

## Implementation
- `Travel.Modules.Identity.Infrastructure.IdentityServiceCollectionExtensions.AddIdentityModule()` — JWT bearer + Keycloak authority
- Realm: `travel` (см. `infra/keycloak/travel-realm.json`)
- Тестовый пользователь: `dev@travel.local` / `dev123`

## Tests
- Unit/Integration tests появятся когда пишутся защищённые эндпойнты в Flights M1.

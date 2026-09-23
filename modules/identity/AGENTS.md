# Identity module

Identity is thin Foundation authentication. `AddIdentityModule()` configures JWT bearer from `Keycloak:Authority`, defaults the audience to `travel-web`, and disables HTTPS metadata only in Development. The realm fixture is under `infra/keycloak`.

`Travel.Host` owns the authenticated fallback policy and security middleware order. `Flights.Api.Composition` contributes the `flights:book` policy. `Identity.Api.Composition` contributes JWT/Keycloak infrastructure registration; Identity Core and Application remain marker-only, with no full identity domain behavior.

Do not copy development credentials into instructions, source, tests, or output.

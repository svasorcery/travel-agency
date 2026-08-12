# Identity module

Identity is thin Foundation authentication. `AddIdentityModule()` configures JWT bearer from `Keycloak:Authority`, defaults the audience to `travel-web`, and disables HTTPS metadata only in Development. The realm fixture is under `infra/keycloak`.

`Travel.Host` currently owns fallback and Flights authorization policies. Identity Core, Application, Api, and test projects otherwise contain no real domain behavior.

Do not copy development credentials into instructions, source, tests, or output.

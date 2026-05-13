# 0010. Identity Provider — Keycloak Self-Hosted

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform requires authentication and authorisation for booking flows, user profile management, and AI agent interactions. The requirements are: OIDC-compliant token issuance (to integrate with Angular's `HttpClient` interceptors and WolverineFx.Http bearer token validation), support for email/password registration, social login (Google, GitHub) for contributor convenience, and a local development experience that does not require network calls to a third-party SaaS.

The identity decision also carries a portfolio signal: "knows that identity is not a feature to build from scratch, knows how to integrate a production-grade IdP, understands OIDC flows." A hand-rolled identity solution would fail on the last two dimensions; delegating entirely to a SaaS obscures the integration knowledge.

## Decision

The platform uses **Keycloak** as its identity provider, self-hosted in Docker Compose for local development and deployed as a container on the same VPS as the application in production. Keycloak runs as an Aspire-orchestrated resource (`AddKeycloakContainer(...)`) in local development. It provides: OIDC/OAuth 2.0 token issuance, email/password authentication, social provider integration (Google, GitHub OAuth apps), realm-based multi-tenancy if required in future subprojects, and a production-grade admin console for user management. `Travel.Host` validates JWT tokens issued by Keycloak using standard ASP.NET Core bearer authentication middleware — no Keycloak-specific SDK is imported into the application code.

## Alternatives Considered

### Option A: Auth0 (SaaS)

Auth0 is a managed Identity-as-a-Service with OIDC support, social providers, and a generous free tier.

Rejected because: Auth0 introduces a network dependency into local development (all authentication flows hit Auth0 servers, even in dev). Local-only development is a project requirement for contributors without stable internet access or in restricted network environments. Additionally, a showcase demonstrating self-hosting capabilities is more instructive for developers building their own platforms — self-hosted Keycloak is the pattern used in real enterprise deployments that cannot send user data to a third party.

### Option B: IdentityServer / Duende IdentityServer

The .NET-native OIDC server. Duende IdentityServer is the commercial successor to the OSS IdentityServer4 (which reached end-of-life in 2022).

Rejected because: Duende IdentityServer requires a commercial license for production use (Community Edition has revenue restrictions). An OSS showcase cannot depend on commercially licensed identity infrastructure. The pre-Duende OSS IdentityServer4 is EOL and unsupported. OpenIddict (OSS, MIT) is a viable alternative but requires more configuration work than Keycloak and lacks the production-grade admin UI.

### Option C: ASP.NET Core Identity (built-in)

ASP.NET Core Identity provides password hashing, user management, and basic cookie/token issuance out of the box with no external service.

Rejected because: ASP.NET Core Identity does not natively issue OIDC-compliant JWTs without additional libraries (e.g., OpenIddict on top). It has no social login support without additional NuGet packages. The admin story (creating users, managing roles, resetting passwords) requires building a custom UI. Keycloak solves all of these problems out of the box and the integration with ASP.NET Core bearer middleware is standard.

## Consequences

### Positive
- Keycloak provides a production-grade OIDC implementation with zero custom code for token issuance, social login, token refresh, and JWKS endpoint. The ASP.NET Core bearer middleware integration is fully standard.
- Local development and production use the same identity server configuration, eliminating the "works on my machine" class of auth bugs caused by mocking identity in dev.
- Keycloak's realm configuration is exportable as JSON; committing realm exports to the repository makes onboarding reproducible.

### Negative / Trade-offs
- Keycloak is a Java application and adds significant memory overhead to the local development stack (~512 MB minimum). On machines with limited RAM, starting the full Aspire stack with Keycloak, NATS, Redis, and Postgres is resource-intensive.
- Keycloak configuration (realm, clients, roles) must be maintained alongside application code. Breaking Keycloak configuration changes (e.g., adding a required claim) are not caught by application tests and must be tested via the Aspire smoke test layer.

### Neutral
- The `AddKeycloakContainer(...)` Aspire integration uses the official `Aspire.Keycloak` package. Keycloak major version upgrades may require coordinated Aspire integration package updates.

## Out of Scope

- Multi-tenancy and Keycloak realm-per-tenant patterns — not in Foundation scope; deferred to a future subproject if multi-tenant booking is required.
- Fine-grained role and permission design — deferred to the Flights subproject when the first role-protected booking endpoint is implemented.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 4.6
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0010), § 4.6
- Keycloak documentation: https://www.keycloak.org/documentation
- Aspire Keycloak integration: https://learn.microsoft.com/en-us/dotnet/aspire/authentication/keycloak-integration

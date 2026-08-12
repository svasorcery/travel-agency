---
name: integration-from-openapi
description: Design or implement a provider anti-corruption layer from OpenAPI or endpoint documentation. Use for external supplier clients, wire DTOs, domain mapping, ErrorOr contracts, and provider constraints.
---

Canonical Travel workflow ID: travel-agency/integration-from-openapi.

# Integration from OpenAPI

1. Establish the module, provider, capability, and authoritative API input. State whether the request is design or implementation.
2. Keep provider clients, wire DTOs, and mapping in Infrastructure. Expose domain-facing Core ports returning `ErrorOr<T>` with explicit imports.
3. Follow existing Client, capability Provider, mapper, and options naming. Keep provider semantics out of domain types.
4. Document field mapping, error translation, resilience, authentication, rate limits, and terms-of-service constraints. Make source edits only within implementation scope.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.

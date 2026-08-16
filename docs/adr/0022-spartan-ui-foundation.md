# 0022. Spartan UI Behind the Travel UI Kit

**Date:** 2026-08-16
**Status:** Accepted
**Deciders:** Travel platform owner

## Context

ADR 0005 selected PrimeNG for the Angular frontend. The repository installed and bootstrapped its theme, but no application template or feature imported a PrimeNG component. The platform therefore had a dependency and upgrade constraint without product UI relying on its API.

The frontend needs accessible headless behavior, full Tailwind v4 control, Angular 21 zoneless compatibility, and an escape hatch from any one vendor API. A component-library replacement is cheap at this stage, before booking screens are built.

## Decision

Replace PrimeNG with Spartan UI 1.3.1 and keep Spartan behind the existing `@travel/ui-kit` boundary:

- `@spartan-ng/brain` supplies maintained accessible behavior primitives.
- The application owns the Helm styling recipes copied from Spartan's official generator output.
- Application and feature code import only Travel-owned primitives such as `TravelButton` from `@travel/ui-kit`.
- Direct TypeScript imports from `@spartan-ng/*`, `primeng/*`, and `@primeng/*` outside `shared/ts/ui-kit` are rejected by ESLint. The application stylesheet imports Spartan's Tailwind preset as the one intentional global-style integration.
- Spartan and Angular CDK versions are pinned deliberately; the lockfile remains the installation authority.
- PrimeNG, its theme package, PrimeIcons, and `tailwindcss-primeui` are removed.

The initial slice adds a real button primitive rather than leaving `ui-kit` as an empty scaffold. It defaults native buttons to `type="button"` while preserving an explicit submit type.

## Alternatives Considered

### Keep PrimeNG

Rejected because the application had not adopted it, while its broad component API and styling integration would become expensive to replace after feature development starts.

### Angular Material

Rejected for the foundation because its Material design language is a stronger visual constraint than the product wants. Angular CDK remains available under Spartan for low-level overlay and accessibility infrastructure.

### Angular Aria with fully custom components

Deferred. It offers an attractive Angular-native direction but would require the team to own substantially more component behavior and styling immediately. The facade keeps a later migration possible without changing feature imports.

### Import Spartan directly in features

Rejected because it would exchange PrimeNG lock-in for Spartan lock-in. The Travel facade is intentionally small, but it owns the public selectors, defaults, and types.

## Consequences

### Positive

- The migration removes unused PrimeNG packages before any feature UI depends on them.
- Product styling remains Tailwind-native and brandable.
- Accessible behavior is sourced from Spartan Brain rather than reimplemented ad hoc.
- The `@travel/ui-kit` boundary makes later vendor replacement incremental.

### Negative / Trade-offs

- Helm code is application-owned and must be reviewed when regenerated or upgraded.
- Spartan has a smaller ecosystem and less all-in-one widget coverage than PrimeNG.
- Complex travel widgets such as date-range pickers and data grids will need explicit product-level evaluation instead of assuming one library supplies every feature.

### Neutral

- This changes the frontend foundation only. It does not implement booking screens or alter backend contracts.
- Historical specifications still describe the original PrimeNG choice; this ADR is the current authority for the UI library.

## References

- Superseded decision: `docs/adr/0005-frontend-stack.md`
- Public facade: `shared/ts/ui-kit/src/index.ts`
- First primitive: `shared/ts/ui-kit/src/lib/button/button.ts`
- Spartan installation: https://www.spartan.ng/documentation/installation
- Spartan CLI: https://www.spartan.ng/documentation/cli

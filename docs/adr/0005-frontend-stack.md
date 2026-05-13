# 0005. Frontend Stack — Angular 21 Zoneless + Signals + NgRx SignalStore + Tailwind v4 + PrimeNG

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform serves SEO-sensitive travel content (search results, destination pages) alongside interactive, data-heavy booking flows (date pickers, multi-city search, seat selection, passenger forms). This combination puts pressure on the frontend framework in two directions simultaneously: server-side rendering for SEO correctness, and fine-grained reactivity for interactive UI without degrading to full-page re-renders.

Angular 21 introduces zoneless change detection as a default (no Zone.js), Signal-based reactive state as the primary data flow primitive, `httpResource` and `rxResource` as built-in server-state utilities, and Angular SSR with per-route hybrid rendering stabilised since v20. These capabilities map directly onto the product's requirements. The question of UI component library is non-trivial: travel applications require comprehensive coverage (DataTable with sorting/filtering, Calendar with range selection, multi-select with search, Autocomplete, Tree, Timeline) and the library must work in unstyled mode to allow a custom Tailwind v4 design system without fighting library-provided CSS specificity.

## Decision

The frontend stack is:

- **Angular 21** in **zoneless** mode (no Zone.js, `ChangeDetectionStrategy.OnPush` everywhere by default)
- **Signals + `httpResource`/`rxResource`** as the primary server-state and reactive-state primitives
- **NgRx SignalStore** for cross-feature shared state where Signals alone are insufficient
- **Standalone components only** — no NgModules
- **Angular SSR** with per-route render mode for hybrid rendering (SSR for content pages, CSR for interactive booking flows)
- **Signal-based forms** (`@angular/forms/signals`) for one form as a forward-looking demo; remaining forms on Reactive Forms until v22 GA
- **Tailwind v4** (4.2+) via `@tailwindcss/postcss` for utility-first styling
- **PrimeNG** in **unstyled mode** + **`tailwindcss-primeui`** as the component library

**UI library selection rationale:** PrimeNG was chosen over Angular Material and Ant Design for three reasons: (1) unstyled mode gives complete visual control with Tailwind without CSS specificity battles; (2) PrimeTek's component coverage (DataTable, Calendar, MultiSelect, Autocomplete, Timeline) is comprehensive for travel use cases; (3) PrimeNG has first-class support for signal-based/zoneless Angular as of v18+. Angular Material's design language conflicts with a custom travel brand; Ant Design's Angular port lags the React version in Signal/zoneless support.

## Alternatives Considered

### Option A: React + Next.js

React 19 with the App Router. Excellent ecosystem, strong SSR story, vast component library choice.

Rejected because: the portfolio objective is demonstrating expertise in the Angular ecosystem specifically. A React showcase is a different portfolio artefact; the author's target audience and existing expertise make Angular the correct choice. Next.js App Router introduces RSC semantics that, while powerful, do not demonstrate Angular-specific architectural decisions (standalone components, Signal Forms, NgRx SignalStore) that are in-demand in enterprise Angular projects.

### Option B: Vue + Nuxt

Vue 3 Composition API + Nuxt 3 for SSR. Clean reactive model, good ergonomics.

Rejected because: the same portfolio-focus reasoning applies. Vue/Nuxt would not demonstrate Angular Signal architecture or NgRx pattern, which are the specific competencies the platform is designed to exhibit.

## Consequences

### Positive
- Zoneless Angular eliminates the Zone.js change-detection overhead, enabling better performance for data-heavy travel search result pages.
- `httpResource` provides a built-in, Signal-aware data-fetching primitive that eliminates boilerplate RxJS subscription management for most HTTP use cases.
- PrimeNG unstyled + `tailwindcss-primeui` allows the design system to be driven entirely by Tailwind utility classes, making design changes cheap and consistent.

### Negative / Trade-offs
- Zoneless Angular is new in v21; some third-party libraries that rely on Zone.js patching for async tracking are incompatible and require manual migration or exclusion.
- Signal-based forms are experimental in Angular 21 and will have API changes before v22 GA stabilisation. The one demo form written with signal forms will require migration; all other forms remain on Reactive Forms until v22.
- `tailwindcss-primeui` is a relatively young integration layer; breaking changes between Tailwind v4 and PrimeTek updates are possible and require vigilance.

### Neutral
- Code is written to Angular 22 expected conventions (OnPush default, Signal Forms direction, selectorless components) from the start. The `ng update` migration path to v22 is expected to be mechanical for the patterns chosen.

## Out of Scope

- Specific component-level design decisions (colour palette, spacing scale, component variants) — these are implementation details in `libs/ui/`.
- Storybook 10 configuration and visual regression testing strategy — covered in the testing conventions.
- Angular SSR deployment configuration — covered in the deployment documentation.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 5.2
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0005), § 5.2
- PrimeNG unstyled mode documentation: https://primeng.org/theming
- tailwindcss-primeui: https://github.com/primefaces/tailwindcss-primeui
- NgRx SignalStore: https://ngrx.io/guide/signals/signal-store

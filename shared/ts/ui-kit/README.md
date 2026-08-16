# Travel UI kit

`@travel/ui-kit` is the only UI-component API available to application and feature code.

Spartan Brain primitives and owned Helm styling recipes are implementation details of this library. Do not import their TypeScript APIs outside `shared/ts/ui-kit`; ESLint enforces that boundary. The global application stylesheet is the single intentional consumer of Spartan's Tailwind preset.

The first primitive is `TravelButton`. Add further primitives test-first and expose a Travel-owned API rather than re-exporting Spartan types directly.

Run `nx test ui-kit` for focused unit tests and `nx build ui-kit` to verify the published library boundary.

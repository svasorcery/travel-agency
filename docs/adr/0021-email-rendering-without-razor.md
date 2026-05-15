# 0021. Email Rendering via HTML Token-Replacement (no Razor)

**Date:** 2026-05-14
**Status:** Accepted
**Deciders:** M1 remediation author

## Context

Flights M1 sends two transactional emails: `OrderConfirmation` (on `OrderConfirmed` event) and
`OrderCancellation` (on `OrderCancelled` event). The original M1 design spec (§12.1) called for
Razor templates stored under `Infrastructure/Notifications/Templates/`.

During the M1 implementation sprint, RazorLight 2.3.1 — the standalone Razor-rendering library
suitable for class libraries — was found to be incompatible with .NET 10. RazorLight targets
`netstandard2.0` / `net6.0` and pulls in Roslyn compilation APIs that conflict with .NET 10 SDK
APIs at test time and in the Aspire-hosted environment. Attempts to use the ASP.NET Core Razor view
engine directly from `Infrastructure` would violate the no-cross-layer import rule (Infrastructure
must not take a dependency on the HTTP presentation layer).

M1 email templates are static in structure: the confirmation email always shows the same fields
(guest name, booking reference, itinerary summary, total amount, order id) and the cancellation
email always shows the same fields (guest name, order id, reason). There is no conditional
rendering, no loops, and no partials. This is not expected to change until M2 introduces
multi-passenger itineraries.

## Decision

Email rendering in M1 uses **HTML token-replacement** via `HtmlTemplateEmailRenderer`
(in `Infrastructure/Notifications/Email/`). The renderer:

1. Reads a static `.html` template file from the `Infrastructure/Notifications/Templates/`
   directory at construction time.
2. Replaces named `{{Token}}` placeholders with values using `string.Replace`.
3. **Encodes every substituted value** with `System.Net.WebUtility.HtmlEncode` before replacement
   to prevent XSS if a user-supplied string (e.g. a passenger name) contains HTML-special
   characters.
4. Returns the rendered HTML string to the calling Wolverine handler, which passes it to MailKit.

The `IEmailRenderer` interface abstracts the rendering mechanism; the handler is unaware of the
implementation strategy. A real Razor-based renderer (or any other engine) can be substituted by
providing an alternative `IEmailRenderer` implementation and updating the DI registration in
`FlightsModuleServiceCollectionExtensions`.

## Alternatives Considered

### Option A: RazorLight 2.3.1

Runtime Razor template compilation via the `RazorLight` NuGet package.

Rejected because RazorLight does not run on .NET 10 (dependency on Roslyn APIs removed in .NET 10
SDK). The package is not updated for .NET 10 compatibility as of May 2026.

### Option B: ASP.NET Core Razor view engine

Use the MVC Razor view engine via `IRazorViewEngine` injected into the Infrastructure layer.

Rejected because this requires `Infrastructure` to depend on `Microsoft.AspNetCore.Mvc.Razor` —
a presentation-layer package. This violates the architectural boundary that keeps Infrastructure
free of HTTP/presentation concerns and would pull the full MVC stack into a project that should
only contain persistence adapters.

### Option C: Scriban / Fluid template engines

Lightweight, .NET-10-compatible template engines with Handlebars-like syntax.

Considered but deferred. Adding a template engine dependency for two static templates with no
conditional logic is over-engineering for M1. The token-replacement approach is simpler, has no
third-party dependencies, and is trivially testable. If M2 introduces multi-passenger itineraries
requiring loops or conditionals in templates, migrating to Scriban or a similar engine is the
natural next step; the `IEmailRenderer` interface makes this migration non-breaking.

## Consequences

### Positive

- No third-party template-engine dependency; zero additional NuGet packages.
- `HtmlTemplateEmailRenderer` is a pure `string → string` function; unit tests cover it with no
  special infrastructure.
- `WebUtility.HtmlEncode` on every token prevents XSS in confirmation/cancellation emails if a
  passenger name contains `<`, `>`, `&`, or `"`.
- `IEmailRenderer` abstraction keeps the change invisible to Wolverine handlers.

### Negative / Trade-offs

- Template files are static HTML with `{{Token}}` syntax — not a real template language. Conditional
  rendering (e.g. show ticket numbers only after ticketing, show refund amount only on cancellation)
  requires either multiple template files or string manipulation in the renderer. This is acceptable
  for M1's two static templates.
- Template authoring is not ergonomic for designers: no IDE support for `{{Token}}` syntax,
  no template inheritance, no partials.

### Neutral

- The `IEmailRenderer` interface is already designed for substitution: when M2 requires richer
  email templates, `HtmlTemplateEmailRenderer` is replaced, not the handlers.

## References

- Flights M1 design spec: `docs/superpowers/specs/2026-05-13-flights-m1-design.md` §12.1
- M1 remediation design spec: `docs/superpowers/specs/2026-05-14-flights-m1-remediation-design.md`
  (decision D6)
- Implementation: `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Email/HtmlTemplateEmailRenderer.cs`

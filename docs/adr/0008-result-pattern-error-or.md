# 0008. Result Pattern — ErrorOr

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform's application and domain layers frequently need to communicate failure in a way that does not involve exceptions. Exceptions are appropriate for unexpected, unrecoverable failures (hardware faults, null dereferences, violated invariants that should never occur in production). They are not appropriate for expected, recoverable outcomes: "offer not found," "passenger details failed validation," "booking already confirmed." Using exceptions for expected outcomes forces callers to discover failure modes through `catch` blocks, pollutes stack traces, and prevents the compiler from reminding callers that failure is possible.

The .NET ecosystem offers several Result/discriminated-union libraries: a hand-rolled `Result<T>`, `OneOf`, `FluentResults`, `CSharpFunctionalExtensions`, `LanguageExt`, and `ErrorOr`. Each makes different trade-offs between type complexity, HTTP integration, and ergonomics. The decision also intersects with HTTP error responses: when an application-layer handler returns a domain error, the HTTP layer must map it to an appropriate ProblemDetails response without manual per-endpoint `if (result.IsError)` branches.

## Decision

The platform uses **ErrorOr** (NuGet: `ErrorOr`, author: Amichai Mantinband) as the Result pattern library throughout the application and domain layers. ErrorOr provides:
- A generic `ErrorOr<T>` type that is either a value or a list of `Error` instances.
- Built-in HTTP-taxonomy error kinds: `Error.Validation`, `Error.NotFound`, `Error.Conflict`, `Error.Unauthorized`, `Error.Unexpected`, `Error.Failure` — mapping directly to HTTP status codes without custom logic.
- Native multi-error support: a single operation can return multiple validation errors without wrapping them in a collection manually.
- Ergonomic `.Then()` / `.FailIf()` / `.Match()` chaining.

`ErrorOr` is **not exposed via `global using`**. It is imported with an explicit `using ErrorOr;` directive in each file that uses it. This keeps the dependency visible in code review and prevents it from silently permeating layers (e.g., domain events or infrastructure models) where it does not belong.

## Alternatives Considered

### Option A: Custom `Result<T>` (hand-rolled, ~30 lines)

A project-specific generic result type: `Result<T>` with `IsSuccess`, `Value`, and `Error` properties. Common in clean-architecture tutorial codebases.

Rejected because: this is NIH in 2026. The hand-rolled version lacks built-in multi-error support, lacks HTTP taxonomy (requiring a custom mapping table somewhere), and lacks the ergonomic chaining API. It also means every contributor must learn a project-specific convention instead of a named, documented OSS library. The authoring cost is low but the ongoing maintenance cost of a custom type is non-zero and growing.

### Option B: OneOf / LanguageExt

Discriminated union libraries. `OneOf` is popular for general-purpose unions; `LanguageExt` provides a full functional-programming toolkit (Option, Either, Aff, Eff monads).

Rejected because: `OneOf` has no HTTP error taxonomy and requires per-project mapping conventions identical to the custom Result problem. `LanguageExt` introduces a large functional programming vocabulary (Aff, Eff, monad transformers) that is foreign to the majority of .NET developers and makes the codebase harder to onboard into. The portfolio goal is demonstrating clean architecture patterns to a broad .NET audience, not functional programming expertise.

### Option C: FluentResults / CSharpFunctionalExtensions

`FluentResults` is a well-designed library with multi-error and metadata support. `CSharpFunctionalExtensions` pairs Maybe and Result types.

Rejected because: neither has the built-in HTTP status code taxonomy that `ErrorOr` provides. Both require a custom mapping layer to translate domain errors to ProblemDetails responses. `ErrorOr`'s HTTP-taxonomy errors make WolverineFx.Http integration cleaner: the handler returns `ErrorOr<T>` and the endpoint layer maps `Error.NotFound` → 404 without a switch statement.

## Consequences

### Positive
- WolverineFx.Http endpoint handlers can return `ErrorOr<T>` and the HTTP layer maps the error kind to a ProblemDetails status code without per-endpoint mapping logic.
- Multi-error support means a FluentValidation `ValidationResult` maps directly to `ErrorOr<T>` with multiple `Error.Validation` entries — no intermediate aggregation type needed.
- `ErrorOr` is authored by Amichai Mantinband, whose .NET clean-architecture content is widely followed; the library is recognisable to the developer audience this platform targets.

### Negative / Trade-offs
- `ErrorOr` is a third-party library with a single principal maintainer. If maintenance lapses, the project would need to fork or migrate. This risk is mitigated by the library's simple surface area (it could be vendored if necessary) but the dependency remains.
- The HTTP error taxonomy is opinionated: `Error.Validation` maps to 422, `Error.NotFound` to 404, etc. Edge cases that require non-standard HTTP mappings need custom `Error.Custom(type, description, code)` instances, which are less self-documenting than the built-in kinds.

### Neutral
- The explicit `using ErrorOr;` import convention (no global using) is a project-wide rule enforced via code review and Roslynator analyser configuration where possible. It is not automatically enforceable by the compiler.

## Out of Scope

- How WolverineFx.Http specifically maps `ErrorOr` error kinds to ProblemDetails — that is an implementation detail in `Travel.Shared.Web`.
- Exception handling policy (which exceptions are caught at which layer) — covered in CLAUDE.md conventions.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 5.1
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0008), § 5.1
- ErrorOr NuGet: https://www.nuget.org/packages/ErrorOr
- ErrorOr GitHub: https://github.com/amantinband/error-or

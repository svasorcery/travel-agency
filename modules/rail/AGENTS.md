# Rail module

Rail M1 has a default-off read-only source implementation for Yandex.Rasp station selection and direct train/suburban schedules, independent optional ticket facts and exact prices. Core/Application/Infrastructure/Api remain separate; Application owns normalized ports, Infrastructure owns wire parsing and provider HTTP.

DB/GTFS, multiple sources, inventory and booking remain future scope. Only temporary bounded RAM data is used; there is no persistence/migration. The current contract is ../../openspec/specs/rail-schedule-search/spec.md; all25completed tasks and historical evidence are in ../../openspec/changes/archive/2026-10-20-rail-m1-yandex-search. See README.md and ../../docs/architecture/rail-completion-review.md for acceptance and pending external gates.

Host imports the implemented Api.Composition facade only. Real source access remains default-off, startup does not call suppliers, fictional transport is explicitly selected and cannot replace a real failure. Source/local, actual isolated Host/OpenAPI, the fixed fictional measured profile and ordinary PR/post-merge CI are accepted. Real source/banner and target rollout still require separate acceptance/authorization. Other module composition belongs to its own facade.

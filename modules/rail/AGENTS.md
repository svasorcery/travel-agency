# Rail module

Rail M1 has a default-off read-only source implementation for Yandex.Rasp station selection and direct train/suburban schedules, independent optional ticket facts and exact prices. Core/Application/Infrastructure/Api remain separate; Application owns normalized ports, Infrastructure owns wire parsing and provider HTTP.

DB/GTFS, multiple sources, inventory and booking remain future scope. Only temporary bounded RAM data is used; there is no persistence/migration. See README.md and the active rail-m1-yandex-search change for exact source evidence and pending external gates.

Host imports the implemented Api.Composition facade only. Real source access remains default-off, startup does not call suppliers, fictional transport is explicitly selected and cannot replace a real failure. Actual Host/DB, measured load/memory, real source/banner and target rollout require their separate acceptance/authorization; source tests do not imply those gates passed. Trips remains unwired.

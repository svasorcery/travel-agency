# Review of the former root src/ tree

**Reviewed:** 2026-09-23
**Base:** bec2ad2084bd8f01bbf139ac7e33ac195eb5d037 (the fetched origin/dev WS4 merge)
**Scope:** tracked root src/ history and any fixture or supplier knowledge relevant to the current Travel Platform.

## Current state

The root src/ path has no tracked entries at the reviewed base (git ls-tree -r --name-only HEAD src returned no paths), and Test-Path src returned false. Application source now lives under apps/, modules/ and shared/. This review changes no source or database content.

## Deletion history inspected

| Commit | Tracked root src/ change | Review finding |
|---|---|---|
| d17eca9dcdc190665bfce10a50211715db02a93e, 2026-05-12, “chore: wipe legacy code before Foundation rewrite” | Deleted 50 tracked files: old Viajante Bootstrapper, Shared abstractions/dispatchers/middleware and four Rail scaffold projects. | The deleted set contained no tracked tests or fixture files and no working supplier client. The current [AppHost](../../apps/Travel.AppHost/Program.cs), [Host](../../apps/Travel.Host/Program.cs) and [Shared](../../shared/AGENTS.md) follow different composition and messaging contracts. |
| a7c8c67a0e7e96926fec3c5dd7ebe28f8422257a, 2026-05-15, “feat(flights): add payment and nl-search duration histograms” | Added 12 Rail files under root src/ after the earlier wipe. | The commit subject concerns Flights; this review cannot establish why Rail files were reintroduced. |
| 4f8f28ddecef155797cfd54b7f408b709dfeb3c6, 2026-08-21, “chore(rail): remove legacy source tree” | Deleted those 12 tracked Rail files. | No root src/ files remain. |

Commands used for the bounded history check:

    git diff --name-status d17eca9d^ d17eca9d -- src
    git diff --name-status a7c8c67^ a7c8c67 -- src
    git diff --name-status 4f8f28d^ 4f8f28d -- src
    git ls-tree -r --name-only HEAD src

## Reusable material and limits

The later Rail set included a large Layer5827 DTO describing one RZD response shape; an IRailProvider interface listing station, train, route and car queries; an empty IRailOrderProvider; a StationDto; and a StationCode value object that trims/uppercases input but rejects only blank text. The RZD Parser and provider Extensions classes were empty. The files had no tracked tests, recorded HTTP endpoint, credential flow, wire sample or source documentation that would validate the DTO against a current supplier contract.

This is **historical design material**, not a current anti-corruption layer or a verified fixture. The [Rail module](../../modules/rail/AGENTS.md) is currently a read-only schedule scaffold with Yandex.Rasp and DB/GTFS as candidates. Restoring the old RZD DTO or broad provider interface now would introduce an unverified supplier assumption and booking-shaped API into that scaffold. If RZD is selected in a future Rail specification, recover these exact historical paths from Git for comparison and validate them against current provider documentation and captured responses before implementing a new Infrastructure client.

The first wipe's Bootstrapper and in-memory message broker are generic legacy infrastructure. Their current counterparts use AppHost orchestration and Wolverine/Marten; no uniquely needed fixture or provider rule was identified in the inspected files. This review does not assert that every historical source line was functionally equivalent to the new platform.

## Authorization and next gate

The [Foundation implementation plan](../superpowers/plans/2026-05-04-foundation.md) contains an explicit “AUTHORIZED DESTRUCTIVE WIPE” statement, and Git records the wipe commit. Those repository artifacts do not independently prove the earlier conversation or a dedicated review before the later Rail deletion. No separate pre-deletion review evidence for 4f8f28d was found in the inspected repository docs.

The remediation design requires a dedicated review **before a future root src/ deletion**. That operation is not applicable at this base because the path is already absent. This retrospective report does not claim the earlier gate was satisfied. Any future restoration or supplier implementation needs its own reviewed scope; no code restoration is part of WS5.

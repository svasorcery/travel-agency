# Design

## Context

See proposal.md for the two explanation gaps. The user selected this bounded change after deferring human acceptance. Current source is the accepted dev commit `2a2763e15f3093fb6c011be8e47eb003ae8bf41a`; work is isolated on `codex/trips-demo-explanations`.

The page already receives strictly decoded covered/unmet interests whose union is verified against the saved brief by `TripsItineraryStateService.matchesFoundation`. The released walking-evidence assumptions are byte-bound and checked by `CuratedMoscowFacts`; changing that text would change the release.

## Decisions

- Derive preview-day explanations in the existing component from the immutable current preview. Use the union of covered/unmet interests as the selected set, intersect it with each day's stop topics, and preserve `tripInterests` order. Do not read the mutable brief draft or add response fields. A component projection avoids both stale draft coupling and a new backend contract.
- Keep the existing whole-trip covered/unmet values and filled/partial semantics. Label their scope. Show the daily list under its day heading, with explicit empty lists and an empty-selection message. Describe coverage as the topics assigned to stops, not a score or a new daily quota.
- Correct only the rendered walking explanation. Keep the original source assumption string, server constants, loader, builder, data files and tests' byte-pinned fixtures unchanged. Changing the hashed string or weakening its validation would violate the user's v1 boundary.
- Use existing fictional HTTP/component fixtures for daily coverage behavior. Existing state/adoption tests continue to check no preview mutation and explicit Save. Do not regenerate the real Q corpus or restart the retained local demo for a presentation-only change.

## Risks / Trade-offs

- Topic coverage is not subjective interest satisfaction: retain visible human-quality caveats and explain that the lists use stop topics.
- Current preview can disappear on owner/plan/edit changes: derive the daily view from that same signal, so it clears with the preview.
- Original package wording remains broad: label it as original source text and put the precise interpretation in the ordinary explanation, without rewriting historical evidence.

## Verification and scope

Run the existing focused component baseline, new failing daily behavior cases, focused green run, the complete web unit suite and production build. Check formatting, OpenSpec and unchanged v1 hashes; request an independent source review. The subsequent publication step is a separate PR to dev with ordinary CI. No migrations, live Host/DB writes, source refresh, merge or real-user enablement is included.

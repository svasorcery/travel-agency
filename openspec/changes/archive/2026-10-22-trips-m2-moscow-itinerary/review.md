# Trips M2 independent planning review

Date: 2026-10-20. Verdict: **changes required — one P2**. No confirmed P1 or P3. No other material planning issue was found across the full reviewed corpus; the correction below needs a scoped independent recheck.

## Independence, snapshot and scope

Reviewer: `/root/trips_m2_plan_audit`, dispatched separately without the author's conversation history. The review treated every choice except Moscow and the structured wishes → proposed route → explicit editing/acceptance scenario as an author's proposal, not as a fixed human requirement. It independently read the full proposal, research, design, delta specification and task list, then checked their assumptions against actual tracked source and accepted M1 evidence.

Workspace: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-plan/travel-agency`. HEAD/base: `30cd68d892cf72994c83047a22ceff782814796f`. Tracked source had no local diff during review. M1 closure was read with `git show` at `3b95b749107da9cdde07d9d8ca173293ab38764c`: archived finalization, design (including section 7), implementation-review and accepted canonical trip-plan-workspace specification. This review does not assert that the open closure PR has merged or independently attest its newest CI run.

| Reviewed document | SHA256 |
|---|---|
| proposal.md | B1D0674DFD24CFAF0EC9F1AD56DE89826239C4E02D0B6454BDDB49BA0E6C8B7B |
| research.md | 30A4D661CEBCCDD5112A30676D77DD075234D0C641893F5C5CFEC8960A7D79AC |
| design.md | D18CA6A08AF2C34B3A4F6F7509F813C4C12D7B4EAF274FC5E737C4DB7FF4A3FF |
| specs/trip-itinerary-candidates/spec.md | 25E7083461846986B483CC8E292CCBD36EDAC6B206C05977DA014CD2D5509D37 |
| tasks.md | 159924052560A8C55D2C2C75C58F92A78963803F6194AAFC152AE7E197A6AF05 |

The author corrected only the research document's relative source links during the review; the table binds the corrected file actually re-read. Later author verification receipts are separate from this substantive audit.

Read root and Trips AGENTS.md; also Travel.AI instructions for the stated unused AI boundary. Checked Core brief/value/block/plan invariants; Application owner-first reads, ports, CAS and full-detail precommit limits; Infrastructure canonical codec, store and protection/readiness paths; API endpoint/guard/response-policy/composition contracts; TS wire types/decoder/client; Angular availability, editor state, component and related tests; lean HTTP/composition/privacy and unit seams, architecture inventory and fictional browser configuration/fixture.

No branch switch, stage, commit, push, migration, product-source/runtime-data edit, Host/DB/model/provider runtime or paid API execution occurred. No application tests or quality runs were executed by this planning reviewer. The only authored artifact is this review. Public read-only [Wikidata licensing](https://www.wikidata.org/wiki/Wikidata:Licensing), [Wikidata data-access guidance](https://www.wikidata.org/wiki/Wikidata:Data_access) and [OpenStreetMap copyright](https://www.openstreetmap.org/copyright) were checked: the proposed structured-data CC0 boundary and the separate OSM obligations are consistent with those pages. This does not accept any concrete POI or walking/access record.

## R1 — P2: define the dataset recheck that gates preview transfer

**Location:** `design.md:99`; supporting task `tasks.md:30` and transfer requirement `specs/trip-itinerary-candidates/spec.md:135-148`.

The transfer rule requires an unexpired candidate and the same active dataset version, but the only specified pre-transfer network action is the owner plan GET. That existing v1 detail contains only M1 plan fields and an ETag, not generation enablement, dataset status or identity (actual `shared/ts/api-client/src/trips.types.ts:55-64`, `modules/trips/Travel.Modules.Trips.Api/Endpoints/TripPlanEndpoints.cs:92-115`). Existing availability reads only the city catalogue, also without that information (`apps/web/src/app/trips/trips-availability.service.ts:35-40`). Task 3.4 mentions a new availability seam, but the corpus does not define its contract, mandate it in the transfer flow, or specify what transfer does when it fails. The delta transfer requirement likewise omits dataset status/version.

**Failure/impact:** generate from package A, replace or withdraw A (or disable generation), then leave the saved plan revision unchanged and transfer its preview. The described owner GET succeeds and cannot distinguish this situation from an active A. One builder can stage the obsolete candidate until its local expiry; another can add a blocking status request and refuse it. Those are different observable freshness/rollback guarantees, despite both claiming to follow this plan. The existing statement that withdrawal is best-effort does not specify which observation is mandatory before transfer.

**Minimal correction:** define the generation-availability read as an explicit M2 API/client contract, including independently disabled versus enabled-unavailable versus ready, current dataset ID/version/hash and release validity when ready, authenticated/no-store bounded responses and its pre-auth disabled guard. Require that read alongside the authoritative owner revision recheck before transfer; define invalidation on changed/withdrawn identity, feature-off, read failure and local/session/epoch changes while either read is pending. Preserve the explicitly bounded guarantee: a subsequent withdrawal can still race after the last successful check, and ordinary M1 saving remains an intention-only CAS. Add the corresponding delta scenarios and task coverage using actual API JSON, including package switch/withdrawal with an unchanged plan revision. An alternative deliberate observed-only rule would need to remove the stronger same-active-version promise consistently; it cannot remain an implementation guess.

## Full-plan assessment and fresh-builder check

The bounded deterministic first route is a coherent alternative to AI for this consumer; no model/provider/profile choice is missing when the chosen design has no model consumer. This audit does not request a generic engine, speculative provider integration or paid runtime. The data dossier remains a real release dependency with its own rights, evidence, dates, cost and independent human checks; fictional IDs and successful unit tests cannot satisfy it. The 30-day request horizon does not imply that every individual fact stays known through that horizon: excluded expired edges and visible unknowns/partial outcomes are explicit.

The source/private-data boundary is consistent with M1. Owner checking precedes private payload processing; generation is read-only and revision-bound; default-off generation is separate from M1 readiness; acceptance remains explicit M1 PUT/CAS with no automatic retry/rebase. Late replies, dirty form/editor state, uncertainty, deletion and logout have named guards and failure tests. Full canonical size preflight reuses the existing port rather than silently extending its format. Estimates/times default to null, exact M1 money remains authoritative, and preview source links are constrained.

Empty-plan-only generation and editable historical notes after save are explicit scope/trust trade-offs, not omitted implementation decisions. Neither choice violates the stated first-route scenario. Source facts and walking estimates are displayed for review without a claim of feasibility or a trusted badge after reload. Supplier integrations, raw Rail persistence/TTL, M3 free-text extraction, permanent provenance and pin/merge remain outside this retained consumer.

Acceptance separates structural code, real data, human route suitability, CI, target and rollout. The frozen-case and confirmatory-set rules preserve denominators and failed results. Unassigned curators/reviewers and absent accepted data/quality are pending release work, not grounds to invent evidence or to run implementation from this planning request. Task dependencies and traceability otherwise cover the delta requirements and the observed M1 seams.

A fresh builder has enough decisions for the selected scope, data schema/freshness, algorithm bounds, outcome semantics, ordinary adoption/write boundary and verification gates. R1 is the remaining material decision: what authoritative generation-state observation is required before moving preview content into the editor, and what failure blocks that action. Resolving it changes API, strict client, state-machine tests and acceptance scenarios together. No user-as-reviewer substitution is needed; the author can choose and document the bounded rule, then request the scoped recheck.

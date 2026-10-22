# Trips M2 v1 actual API wire contract

Published before implementation for task4.1;current API source accepted by independent task review/recheck. Production `ItineraryCandidateResponseEncoder` and actual HTTP vectors are authoritative. No optional keys: nullable values are explicit null. All object properties below are exact; reject unknown/duplicate keys. Complete response UTF8 <=131072 bytes, depth <=16; availability <=2048 bytes/depth4. Requests POST `{}` <=1024 actual bytes with no properties, no query, JSON UTF8, canonical strong quoted positive Int64 If-Match. GET availability no query/body.

```ts
type Utc = string; // exact yyyy-MM-ddTHH:mm:ss.fffffffZ
// Positive canonical decimal Int64 string; never JS number. Other strings below bounded as specified.
type Revision = string;
type Destination = { destinationId:string; timeZone:string; catalogueVersion:string };
type Unsupported = Destination & {
 candidateVersion:1; outcome:'unsupported'; baseRevision:Revision;
 policyVersion:string; generatedAt:Utc;
 reason:'destinationUnsupported'|'dateRangeUnsupported'|'historicalDatesUnsupported'|'horizonUnsupported';
};
type Ready = {
 candidateVersion:1; status:'ready'; destinationId:'moscow'; datasetId:string;
 datasetVersion:string; datasetHash:string; policyVersion:string;
 dataMode:'osm-modelled-demo'; humanQuality:'not-evaluated'; releaseValidUntil:Utc;
}; // EXACT TEN FIELDS; never source facts or private plan data.
type Preparation = { preparedBy:string; humanCurator:null; humanReviewer:null;
 calibrationStatus:'not-evaluated'; buildAsOfUtc:Utc };
type ModelSource = { sourceUrl:string; inputSha256:string; publicationInputSha256:string;
 snapshotPublishedAtUtc:Utc; downloadedAtUtc:Utc; buildAsOfUtc:Utc;
 profileVersion:'profile-v1'; pairSelectorVersion:'pair-selector-v1'; modelVersion:'model-v1';
 validUntil:Utc; assumptions:string; calibrationStatus:'not-evaluated' };
type Evidence = { id:string; kind:'identity-location'|'access'|'walking'; scopeId:string;
 method:'source-record'|'observed'|'modelled'; rightsBasis:'cc0'|'original-observation'|'explicit-permission'|'odbl';
 rightsEvidence:string; sourceLabel:string; sourceUrl:string; retrievedAtUtc:Utc; processedAtUtc:Utc;
 observedAt:Utc|null; validUntil:Utc;
 permittedFields:('identity'|'coordinates'|'opening-hours'|'walking-duration'|'restrictions')[] };
type Fact = { kind:'identity-location'|'access'|'walking'; state:'known'|'unknown';
 evidenceId:string|null; unknownReason:string|null };
type Interval = { day:0|1|2|3|4|5|6; startMinute:number; endMinute:number };
type Block = { poiId:string; proposedBlockId:string; title:string; date:string;
 startTime:null; endTime:null; estimate:null; note:string; dwellMinutes:number; tags:string[];
 wikidataEntityId:string; wikidataRevision:string; latitude:number; longitude:number;
 facts:Fact[]; openingIntervals:Interval[] };
type Path = { pathId:string; pathHash:string; pathLengthMillimetres:string; crossingCount:number;
 fromAnchorNodeId:string; toAnchorNodeId:string; modelVersion:'model-v1'; calibrationStatus:'not-evaluated' };
type Edge = { edgeId:string; fromPoiId:string; toPoiId:string; minimumMinutes:number; maximumMinutes:number;
 evidenceId:string; restrictions:string; method:'modelled'; rightsBasis:'odbl'; modelledWalk:Path };
type Day = { date:string; blocks:Block[]; edges:Edge[] };
type Computed = Destination & {
 candidateVersion:1; outcome:'candidate'|'empty'; baseRevision:Revision;
 datasetId:string; datasetVersion:string; datasetHash:string; policyVersion:string;
 generatedAt:Utc; expiresAt:Utc; releaseValidUntil:Utc;
 dataMode:'osm-modelled-demo'; humanQuality:'not-evaluated'; preparation:Preparation;
 modelSource:ModelSource; attribution:string; derivedDataOffer:'/trips-data/moscow-outdoor/1/moscow-v1.manifest.json';
 coverage:'filled'|'partial'; coveredInterests:string[]; unmetInterests:string[];
 reasons:string[]; exclusions:string[]; uncertainties:string[]; searchLimited:boolean; expansionCount:number;
 days:Day[]; evidence:Evidence[];
};
type Candidate = Unsupported | Computed;
type Problem = { type:'about:blank'; title:string; status:number; code:string; traceId?:string };
```

Bounds: identifiers 1..64 ASCII lowercase letters/digits/hyphen (versions may dot); hash exactly64 lowercase hex; titles <=100; notes <=500 Unicode scalars; sourceLabel actual URI host <=253; HTTPS URLs <=256, no credentials/fragment. source rights/restrictions/coverage text <=500. Blocks <=12 overall/4 per day, days1..3 (each date yyyy-MM-dd), edges<=48, block fact references<=96, distinct evidence entries<=48. Each day exactly adjacent directed edges; references bind type and POI/edge scope to evidence. openingIntervals<=56, day0Sunday..6Saturday/minutes0..1440. dwell15..120; minutes1..120; expansionCount0..20000; tags only architecture/culture/history/nature (food always unmet). Reason/exclusion/uncertainty arrays <=16 each, strings <=64; all flags and enums exact. Money/time remain null. Path length/nonzero anchors and Wikidata revisions are decimal STRINGS; coordinates bounded geographic decimals. Evidence contains actual retrieval/software processing metadata, no fabricated human review timestamps. The single top-level modelSource binds every edge path to the same input/build/model; path metadata plus edge evidence retains direction and reproducing identity. Assumptions explicitly describe 75/45m/min plus one modelled minute per crossing as author scenarios, not measured/confidence/guaranteed feasibility. Attribution is fixed OpenStreetMap ODbL text and fixed public derived-data offer, never private export.

HTTP: 401/403/404/428/400/412/409/429/503/504 application/problem+json with RFC7807 `code`; Busy only Retry-After:1. All responses no-store. Scope unsupported does not include package/source/expiry/days. Empty keeps complete computed basis with empty blocks for all requested days. Full invalid projection fails503, never truncates/repairs. Endpoint writes frozen production UTF8 bytes directly. Availability is independent of M1 readiness; effective itinerary flag is false in Production even requested true. Flags are startup-bound; configuration changes require restart. Publication/real-source/target/live enablement remain separate.

Frozen fictional production HTTP vectors are under tests/fixtures/trips-itinerary-v1: candidate.json,partial.json,empty.json,unsupported.json,ready.json,error.json. Exact hashes and raw receipts are in api-implementation.md;P2sizeproof/recheck in api-recheck.md. No real quality data is used.

Vector bytes are actual unformatted production HTTP UTF8, with no terminal newline. Tests compare every byte after substituting ONLY validated random proposedBlockId UUIDs and framework traceId values with those captured in the vector; ready/empty/unsupported have exact unmodified-byte comparison. traceId is the existing ASP.NET ProblemDetails optional tracing extension, not private content.

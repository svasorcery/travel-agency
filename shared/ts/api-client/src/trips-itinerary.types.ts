export type TripItineraryPreparation = {
  preparedBy: string;
  humanCurator: null;
  humanReviewer: null;
  calibrationStatus: 'not-evaluated';
  buildAsOfUtc: string;
};
export type TripItineraryModelSource = {
  sourceUrl: string;
  inputSha256: string;
  publicationInputSha256: string;
  snapshotPublishedAtUtc: string;
  downloadedAtUtc: string;
  buildAsOfUtc: string;
  profileVersion: 'profile-v1';
  pairSelectorVersion: 'pair-selector-v1';
  modelVersion: 'model-v1';
  validUntil: string;
  assumptions: string;
  calibrationStatus: 'not-evaluated';
};
export type TripItineraryEvidence = {
  id: string;
  kind: 'identity-location' | 'access' | 'walking';
  scopeId: string;
  method: 'source-record' | 'observed' | 'modelled';
  rightsBasis: 'cc0' | 'original-observation' | 'explicit-permission' | 'odbl';
  rightsEvidence: string;
  sourceLabel: string;
  sourceUrl: string;
  retrievedAtUtc: string;
  processedAtUtc: string;
  observedAt: string | null;
  validUntil: string;
  permittedFields: ('identity' | 'coordinates' | 'opening-hours' | 'walking-duration' | 'restrictions')[];
};
export type TripItineraryFact = {
  kind: 'identity-location' | 'access' | 'walking';
  state: 'known' | 'unknown';
  evidenceId: string | null;
  unknownReason: string | null;
};
export type TripItineraryInterval = { day: 0 | 1 | 2 | 3 | 4 | 5 | 6; startMinute: number; endMinute: number };
export type TripItineraryBlock = {
  poiId: string;
  proposedBlockId: string;
  title: string;
  date: string;
  startTime: null;
  endTime: null;
  estimate: null;
  note: string;
  dwellMinutes: number;
  tags: string[];
  wikidataEntityId: string;
  wikidataRevision: string;
  latitude: number;
  longitude: number;
  facts: TripItineraryFact[];
  openingIntervals: TripItineraryInterval[];
};
export type TripItineraryPath = {
  pathId: string;
  pathHash: string;
  pathLengthMillimetres: string;
  crossingCount: number;
  fromAnchorNodeId: string;
  toAnchorNodeId: string;
  modelVersion: 'model-v1';
  calibrationStatus: 'not-evaluated';
};
export type TripItineraryEdge = {
  edgeId: string;
  fromPoiId: string;
  toPoiId: string;
  minimumMinutes: number;
  maximumMinutes: number;
  evidenceId: string;
  restrictions: string;
  method: 'modelled';
  rightsBasis: 'odbl';
  modelledWalk: TripItineraryPath;
};
export type TripItineraryDay = { date: string; blocks: TripItineraryBlock[]; edges: TripItineraryEdge[] };
export type TripItineraryUnsupported = {
  candidateVersion: 1;
  outcome: 'unsupported';
  baseRevision: string;
  destinationId: string;
  timeZone: string;
  catalogueVersion: string;
  policyVersion: string;
  generatedAt: string;
  reason: 'destinationUnsupported' | 'dateRangeUnsupported' | 'historicalDatesUnsupported' | 'horizonUnsupported';
};
export type TripItineraryComputed = {
  candidateVersion: 1;
  outcome: 'candidate' | 'empty';
  baseRevision: string;
  destinationId: 'moscow';
  timeZone: 'Europe/Moscow';
  catalogueVersion: string;
  datasetId: string;
  datasetVersion: string;
  datasetHash: string;
  policyVersion: string;
  generatedAt: string;
  expiresAt: string;
  releaseValidUntil: string;
  dataMode: 'osm-modelled-demo';
  humanQuality: 'not-evaluated';
  preparation: TripItineraryPreparation;
  modelSource: TripItineraryModelSource;
  attribution: string;
  derivedDataOffer: '/trips-data/moscow-outdoor/1/moscow-v1.manifest.json';
  coverage: 'filled' | 'partial';
  coveredInterests: string[];
  unmetInterests: string[];
  reasons: string[];
  exclusions: string[];
  uncertainties: string[];
  searchLimited: boolean;
  expansionCount: number;
  days: TripItineraryDay[];
  evidence: TripItineraryEvidence[];
};
export type TripItineraryCandidate = TripItineraryUnsupported | TripItineraryComputed;
export type TripItineraryAvailability = {
  candidateVersion: 1;
  status: 'ready';
  destinationId: 'moscow';
  datasetId: string;
  datasetVersion: string;
  datasetHash: string;
  policyVersion: string;
  dataMode: 'osm-modelled-demo';
  humanQuality: 'not-evaluated';
  releaseValidUntil: string;
};
export type TripItineraryProblem = {
  type: 'about:blank';
  title: string;
  status: number;
  code: string;
  traceId?: string;
};
export type TripItineraryErrorKind =
  | 'disabled'
  | 'not-found'
  | 'unauthenticated'
  | 'forbidden'
  | 'precondition-required'
  | 'invalid-request'
  | 'stale'
  | 'conflict'
  | 'busy'
  | 'unavailable'
  | 'deadline'
  | 'transport'
  | 'timeout';

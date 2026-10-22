export type RailKind = 'complete' | 'limited' | 'skipped' | 'failed';
export type RailMode = 'train' | 'suburban';
export type RailProfile = 'real' | 'fictional';
export interface RailRef {
  source: 'yandex-rasp';
  code: string;
}
export interface RailStation {
  reference: RailRef;
  name: string;
  settlement: string | null;
  region: string | null;
}
export interface RailRequest {
  from: RailRef;
  to: RailRef;
  catalogueGeneration: string;
  departureDate: string;
  transport: RailMode | 'all';
}
export interface RailAttribution {
  frameUrl: string;
  text: string;
  url: string;
  width: 240;
  height: 130;
}
export interface RailPrice {
  state: 'reported' | 'not-provided' | 'unavailable';
  amount: string | null;
  currency: 'RUB' | 'USD' | 'EUR' | null;
  reason: string | null;
  receivedAt: string;
}
export interface RailTickets {
  electronicTicket: 'true' | 'false' | 'unknown';
  electronicTicketReason: string | null;
  faresState: 'not-provided' | 'reported' | 'limited' | 'unavailable';
  fares: { name: string | null; price: RailPrice }[];
  reasons: string[];
  seatAvailability: 'unknown';
  operationalUpdates: 'not-provided';
  damaged: boolean;
}
export interface RailJourney {
  key: string;
  threadReference: string;
  transport: RailMode;
  from: RailRef;
  to: RailRef;
  departure: string;
  arrival: string;
  durationSeconds: number;
  sourceReportedDate: string | null;
  number: string | null;
  title: string | null;
  carrier: string | null;
  departurePlatform: string | null;
  arrivalPlatform: string | null;
  stops: string | null;
  tickets: RailTickets;
  receivedAt: string;
  reasons: string[];
  source: 'yandex-rasp';
  departureTimeZone: null;
  arrivalTimeZone: null;
  sourceUpdatedAt: null;
}
export interface RailModeEvidence {
  transport: RailMode;
  kind: RailKind;
  reasons: string[];
  originalCount: number;
  normalizedCount: number;
  receivedAt: string | null;
}
export interface RailSearchResult {
  kind: RailKind;
  reasons: string[];
  request: RailRequest;
  source: 'yandex-rasp';
  profile: RailProfile;
  profileEpoch: string;
  modes: RailModeEvidence[];
  items: RailJourney[];
  normalizedCount: number;
  receivedAt: string | null;
  expiresAt: string | null;
  completedAt: string;
  attribution: RailAttribution | null;
}
export interface RailSuggestions {
  kind: RailKind;
  reasons: string[];
  suggestions: RailStation[];
  hasMore: boolean;
  catalogueGeneration: string | null;
  source: 'yandex-rasp';
  profile: RailProfile;
  profileEpoch: string;
  receivedAt: string | null;
  expiresAt: string | null;
  attribution: RailAttribution | null;
}
export interface RailJourneyDetailsRequest {
  search: RailRequest;
  expectedProfileEpoch: string;
  selection: { key: string; transport: RailMode; departure: string; arrival: string };
}
export interface RailStopFact<T> {
  state: 'reported' | 'not-provided' | 'unavailable';
  value: T | null;
  reason: string | null;
}
export interface RailRouteStop {
  sequence: number;
  station: RailRef & { name: string };
  arrival: RailStopFact<string>;
  departure: RailStopFact<string>;
  dwellSeconds: RailStopFact<number>;
  platform: RailStopFact<string>;
  reasons: string[];
}
export interface RailJourneyDetailsResult {
  kind: RailKind;
  reasons: string[];
  stage: 'admission' | 'verification' | 'route';
  request: RailJourneyDetailsRequest;
  source: 'yandex-rasp';
  profile: RailProfile;
  profileEpoch: string;
  verification: { transport: RailMode; kind: RailKind; reasons: string[]; receivedAt: string | null } | null;
  routeReceivedAt: string | null;
  expiresAt: string | null;
  completedAt: string;
  stops: RailRouteStop[];
  boardingIndex: number | null;
  alightingIndex: number | null;
  attribution: RailAttribution | null;
}

export type HotelExecutionKind = 'Unsupported' | 'Complete' | 'Limited' | 'Failed';
export type HotelOutcome =
  | 'Quoted'
  | 'Submitted'
  | 'Pending'
  | 'Unknown'
  | 'ReservedAwaitingDetails'
  | 'Confirmed'
  | 'NotCreated'
  | 'Cancelled';
export interface HotelMoney {
  amount: string;
  currency: string;
}
export interface HotelFact {
  text: string;
  scope: string;
}
export interface HotelArea {
  key: string;
  latitude: number;
  longitude: number;
  radiusKm: number;
  fictional: boolean;
}
export interface HotelSearchRequest {
  areaKey: string;
  checkIn: string;
  checkOut: string;
  adults: number;
}
export interface HotelTerms {
  property: { id: string; name: string; address: string | null; rating: string | null; ratingSource: string | null };
  room: string;
  rateId: string;
  dates: { checkIn: string; checkOut: string; nights: number };
  occupancy: { adults: number };
  total: HotelMoney;
  mandatoryLocalPayments: HotelMoney[] | null;
  board: HotelFact[];
  cancellation: { kind: string; rules: HotelFact[] };
  conditions: HotelFact[];
  eligible: boolean;
  paymentTiming: string;
}
export interface HotelOffer {
  source?: HotelSourceIdentity | null;
  price?: HotelPriceEvidence | null;
  roomIdentity?: string | null;
  conditionEvidence?: {
    kind: string;
    source: string;
    board: { code: string; guests: number | null; nights: number | null; source: string }[];
    cancellation: {
      from: string;
      amount: HotelMoney;
      scope: string;
      source: string;
      amountKind: string;
      boundary: string;
    }[];
  } | null;
  ranking?: {
    policyVersion: string;
    scope: string;
    reason: string;
    bucket: string | null;
    total: HotelMoney | null;
  } | null;
  provider: string;
  environment: string;
  selectionKey: string;
  terms: HotelTerms;
  sourceExpiresAt: string | null;
  purchasable: boolean;
  purchaseUnavailableReason: string | null;
}
export interface HotelSourceIdentity {
  provider: string;
  account: string;
  environment: string;
  profile: string;
}
export interface HotelPriceEvidence {
  basis: string;
  supplierPayable: HotelMoney;
  travellerPrice: HotelMoney | null;
  scope: string;
  source: string;
  included: HotelObligation[];
  localObligations: HotelObligation[] | null;
}
export interface HotelObligation {
  inclusion: string;
  amount: HotelMoney | null;
  scope: string;
}
export interface HotelPropertyGroup {
  key: string;
  property: HotelTerms['property'];
  representative: { source: HotelSourceIdentity; propertyId: string };
  offers: HotelOffer[];
  matchReason: string;
  primaryBucket: string | null;
  minima: { bucket: string; total: HotelMoney }[];
  rankingPolicyVersion: string;
}
export interface HotelSourceStatus {
  provider: string;
  kind: HotelExecutionKind;
  reason: string | null;
  attempted?: boolean;
  resultCount?: number | null;
}
export interface HotelExecution {
  kind: HotelExecutionKind;
  items: HotelOffer[];
  reason: string | null;
}
export interface HotelSearchSnapshot {
  schemaVersion?: number;
  groups?: HotelPropertyGroup[];
  rankingPolicyVersion?: string;
  id: string;
  result: HotelExecution;
  statuses: HotelSourceStatus[];
  expiresAt: string;
}
export interface HotelRatesSnapshot {
  schemaVersion?: number;
  groups?: HotelPropertyGroup[];
  rankingPolicyVersion?: string;
  statuses?: HotelSourceStatus[];
  id: string;
  searchId: string;
  result: HotelExecution;
  expiresAt: string;
}
export interface HotelGuestParty {
  guests: { givenName: string; familyName: string }[];
  email: string;
  phone: string;
  adultsConfirmed: boolean;
  checkInConfirmed: boolean;
}
export interface HotelReservation {
  acceptedPrice?: HotelPriceEvidence | null;
  acceptedConditions?: HotelOffer['conditionEvidence'];
  provider?: string;
  environment?: string;
  source?: HotelSourceIdentity | null;
  id: string;
  quoteRevision: number;
  accepted: HotelTerms | null;
  validUntil: string;
  attemptId: string | null;
  outcome: HotelOutcome;
  reviewReasons: string[];
  everReserved: boolean;
  active: boolean;
  facts: { bookingId: string; hotelReference: string | null; actual: HotelTerms | null } | null;
  createdAt: string;
  updatedAt: string;
  privateContentAvailable: boolean;
  firstMismatch?: { observedAt: string; actual: HotelTerms } | null;
}

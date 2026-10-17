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
  provider: string;
  environment: string;
  selectionKey: string;
  terms: HotelTerms;
  sourceExpiresAt: string | null;
  purchasable: boolean;
  purchaseUnavailableReason: string | null;
}
export interface HotelExecution {
  kind: HotelExecutionKind;
  items: HotelOffer[];
  reason: string | null;
}
export interface HotelSearchSnapshot {
  id: string;
  result: HotelExecution;
  statuses: { provider: string; kind: HotelExecutionKind; reason: string | null }[];
  expiresAt: string;
}
export interface HotelRatesSnapshot {
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
}
